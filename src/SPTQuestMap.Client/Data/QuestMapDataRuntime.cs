using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.UI;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.UI;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestMapDataRuntime : IDisposable
{
    private const float RaidPollIntervalSeconds = 0.1f;
    private const float RaidPerformanceLogIntervalSeconds = 30f;
    private const int RaidPollCheckerBudget = 16;

    private readonly MonoBehaviour _coroutineOwner;
    private readonly ManualLogSource _log;
    private readonly QuestMapDataAdapter _adapter;
    private readonly QuestMapClientConfiguration _configuration;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly QuestTrackingService _tracking;
    private readonly RaidQuestProgressNotificationStack _raidNotification;
    private readonly Dictionary<QuestsScreen, TraderGraphScreenController> _traderControllers = new();
    private readonly Dictionary<TasksScreen, GlobalTasksScreenController> _globalControllers = new();
    private AbstractQuestControllerClass? _latestQuestController;
    private ReactiveQuestMonitor? _reactiveMonitor;
    private PendingTraderScreen? _pendingTraderScreen;
    private PendingGlobalScreen? _pendingGlobalScreen;
    private Coroutine? _loadCoroutine;
    private Coroutine? _reactiveRefreshCoroutine;
    private Coroutine? _globalMountCoroutine;
    private readonly HashSet<string> _reactiveReasons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reactiveQuestIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _raidPreferredObjectiveIds = new(StringComparer.Ordinal);
    private AbstractQuestControllerClass? _raidQuestController;
    private RaidQuestProgressMonitor? _raidProgressMonitor;
    private string? _raidLocationId;
    private float _nextRaidPollAt;
    private float _nextRaidPerformanceLogAt;
    private int _raidPollCount;
    private int _raidPolledCheckerCount;
    private int _raidPollChangeCount;
    private int _raidOverlayRefreshCount;
    private int _raidQuestPatchCount;
    private double _raidPollTotalMilliseconds;
    private double _raidPollMaximumMilliseconds;
    private double _raidOverlayTotalMilliseconds;
    private double _raidOverlayMaximumMilliseconds;
    private double _raidQuestPatchTotalMilliseconds;
    private double _raidQuestPatchMaximumMilliseconds;
    private double _raidQuestUiTotalMilliseconds;
    private double _raidQuestUiMaximumMilliseconds;
    private bool _raidMonitorActive;
    private bool _disposed;

    internal event Action<InRaidQuestProgressChange>? InRaidQuestProgressChanged;

    public QuestMapDataRuntime(
        MonoBehaviour coroutineOwner,
        ManualLogSource log,
        QuestMapClientConfiguration configuration)
    {
        _coroutineOwner = coroutineOwner;
        _log = log;
        _configuration = configuration;
        _adapter = new QuestMapDataAdapter(new ServerQuestTopologySource(), log);
        _assetCache = coroutineOwner.gameObject.AddComponent<QuestAssetSpriteCache>();
        _assetCache.Bind(log);
        _tracking = new QuestTrackingService(configuration, log);
        _raidNotification = RaidQuestProgressNotificationStack.Create(
            coroutineOwner.transform,
            log,
            () => _configuration.RaidNotificationOpacity.Value,
            _assetCache);
        InRaidQuestProgressChanged += _raidNotification.Show;
    }

    public void UpdateRaidMonitor()
    {
        if (_disposed || Time.unscaledTime < _nextRaidPollAt) return;
        _nextRaidPollAt = Time.unscaledTime + RaidPollIntervalSeconds;
        var startedAt = Stopwatch.GetTimestamp();

        if (!InRaidQuestContext.TryCapture(out var raid))
        {
            EndRaidMonitor();
            return;
        }

        var enteringRaid = !_raidMonitorActive
            || !ReferenceEquals(_raidQuestController, raid.QuestController)
            || !string.Equals(_raidLocationId, raid.LocationId, StringComparison.OrdinalIgnoreCase);
        if (enteringRaid) ResetRaidPerformanceTelemetry();

        if (enteringRaid)
        {
            _raidProgressMonitor?.Dispose();
            _raidMonitorActive = true;
            _raidQuestController = raid.QuestController;
            _raidLocationId = raid.LocationId;
            _tracking.BeginRaid(raid.TrackingLocationIds());
            if (_adapter.Overlay is not null) _tracking.ReloadFavorites(_adapter.Overlay.ProfileId);
            _raidProgressMonitor = new RaidQuestProgressMonitor(
                raid.QuestController,
                RequestRaidProgressRefresh);
            ObserveQuestController(raid.QuestController, false);
            _log.LogInfo(
                "QUESTMAP_M06_RAID_MONITOR active=True; " +
                $"location={raid.LocationId}; intervalMs={RaidPollIntervalSeconds * 1000:0}; " +
                $"checkerBudget={RaidPollCheckerBudget}; screenIndependent=True; " +
                "broadReactiveEvents=False; signal=checker-events+incremental-poll");
            return;
        }

        var pollResult = _raidProgressMonitor?.Poll(RaidPollCheckerBudget) ?? default;
        RecordRaidPoll(ElapsedMilliseconds(startedAt), pollResult);
        LogRaidPerformanceIfDue();
    }

    public void ObserveQuestController(AbstractQuestControllerClass questController) =>
        ObserveQuestController(questController, !_raidMonitorActive);

    private void ObserveQuestController(
        AbstractQuestControllerClass questController,
        bool enableReactiveMonitor)
    {
        if (_disposed) return;
        if (!ReferenceEquals(_latestQuestController, questController))
        {
            _reactiveMonitor?.Dispose();
            _reactiveMonitor = null;
        }
        if (enableReactiveMonitor)
        {
            _reactiveMonitor ??= new ReactiveQuestMonitor(questController, RequestReactiveRefresh);
        }
        else if (_reactiveMonitor is not null)
        {
            _reactiveMonitor.Dispose();
            _reactiveMonitor = null;
        }
        _latestQuestController = questController;

        if (_adapter.Topology is not null)
        {
            if (!RefreshOverlay(questController, true)) return;
            TryMountPendingTraderScreen(questController);
            TryMountPendingGlobalScreen(questController);
            return;
        }

        if (_loadCoroutine is null)
        {
            _loadCoroutine = _coroutineOwner.StartCoroutine(LoadAndOverlay(questController.Quests.Count));
        }
    }

    public void ShowTraderScreen(
        QuestsScreen screen,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        TraderClass trader)
    {
        if (_disposed) return;
        CloseTraderScreen(screen);
        _pendingTraderScreen = new PendingTraderScreen(screen, session, inventoryController, questController, trader);
        ObserveQuestController(questController);
        _reactiveMonitor?.SetInventoryController(inventoryController);

        if (!_configuration.EnableTraderQuestGraph.Value)
        {
            _pendingTraderScreen = null;
            _log.LogInfo($"QUESTMAP_M03_STATE screen={screen.GetInstanceID()}; trader={trader.Id}; active=False; safelyDisabled=True; reason=feature disabled; vanillaRestored=True");
        }
    }

    public void CloseTraderScreen(QuestsScreen screen)
    {
        if (_pendingTraderScreen is not null && ReferenceEquals(_pendingTraderScreen.Screen, screen))
        {
            _pendingTraderScreen = null;
        }

        if (_traderControllers.TryGetValue(screen, out var controller))
        {
            _traderControllers.Remove(screen);
            controller.Dispose();
        }

        if (_pendingTraderScreen is null && _traderControllers.Count == 0
            && _pendingGlobalScreen is null && _globalControllers.Count == 0)
        {
            _reactiveMonitor?.SetInventoryController(null);
        }
    }

    public void ShowGlobalTasksScreen(TasksScreen screen, object[] arguments)
    {
        if (_disposed) return;
        foreach (var stale in _globalControllers
                     .Where(pair => pair.Key == null || !ReferenceEquals(pair.Key, screen))
                     .ToArray())
        {
            _globalControllers.Remove(stale.Key);
            stale.Value.Dispose();
        }
        var questController = arguments.OfType<AbstractQuestControllerClass>().SingleOrDefault();
        var inventoryController = arguments.OfType<InventoryController>().SingleOrDefault();
        var session = arguments.OfType<ISession>().SingleOrDefault();
        if (questController is null || inventoryController is null || session is null)
        {
            _log.LogWarning($"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=required Show argument missing; vanillaRestored=True");
            return;
        }

        if (_globalControllers.TryGetValue(screen, out var cached))
        {
            if (!_configuration.EnableGlobalTasksGraph.Value || !cached.CanResume)
            {
                _globalControllers.Remove(screen);
                cached.Dispose();
                if (!_configuration.EnableGlobalTasksGraph.Value)
                {
                    _log.LogInfo($"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=feature disabled; vanillaRestored=True");
                    return;
                }
            }
            else
            {
                ObserveQuestController(questController);
                _reactiveMonitor?.SetInventoryController(inventoryController);
                var topology = _adapter.Topology;
                var layout = _adapter.Layout;
                var overlay = _adapter.Overlay;
                if (topology is not null && layout is not null && overlay is not null)
                {
                    try
                    {
                        var result = cached.Resume(inventoryController, questController, session, topology, layout, overlay);
                        if (!InRaidQuestContext.TryCapture(out _)) RequestReactiveRefresh("global-screen-open", null);
                        _log.LogInfo($"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=True; cached=True; result={result}");
                        return;
                    }
                    catch (Exception exception)
                    {
                        _globalControllers.Remove(screen);
                        cached.Dispose();
                        _log.LogError($"QUESTMAP_M06_ERROR screen={screen.GetInstanceID()}; phase=cached-resume; {exception}");
                    }
                }
            }
        }

        _pendingGlobalScreen = new PendingGlobalScreen(screen, inventoryController, questController, session);
        ObserveQuestController(questController);
        if (!InRaidQuestContext.TryCapture(out _) && _adapter.Topology is not null)
            RequestReactiveRefresh("global-screen-open", null);
        _reactiveMonitor?.SetInventoryController(inventoryController);
        if (!_configuration.EnableGlobalTasksGraph.Value)
        {
            _pendingGlobalScreen = null;
            _log.LogInfo($"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=feature disabled; vanillaRestored=True");
            return;
        }

        _globalMountCoroutine = _coroutineOwner.StartCoroutine(MountPendingGlobalAfterVanillaLayout());
    }

    public void CloseGlobalTasksScreen(TasksScreen screen)
    {
        if (_pendingGlobalScreen is not null && ReferenceEquals(_pendingGlobalScreen.Screen, screen))
        {
            _pendingGlobalScreen = null;
        }
        if (_globalMountCoroutine is not null)
        {
            _coroutineOwner.StopCoroutine(_globalMountCoroutine);
            _globalMountCoroutine = null;
        }

        if (_globalControllers.TryGetValue(screen, out var controller))
        {
            controller.Suspend();
        }

        if (_pendingTraderScreen is null && _traderControllers.Count == 0
            && _pendingGlobalScreen is null && _globalControllers.Count == 0)
        {
            _reactiveMonitor?.SetInventoryController(null);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_loadCoroutine is not null) _coroutineOwner.StopCoroutine(_loadCoroutine);
        if (_reactiveRefreshCoroutine is not null) _coroutineOwner.StopCoroutine(_reactiveRefreshCoroutine);
        if (_globalMountCoroutine is not null) _coroutineOwner.StopCoroutine(_globalMountCoroutine);
        _loadCoroutine = null;
        _reactiveRefreshCoroutine = null;
        _globalMountCoroutine = null;
        _reactiveMonitor?.Dispose();
        _reactiveMonitor = null;
        _reactiveReasons.Clear();
        _reactiveQuestIds.Clear();
        _raidPreferredObjectiveIds.Clear();
        _raidQuestController = null;
        _raidProgressMonitor?.Dispose();
        _raidProgressMonitor = null;
        _raidLocationId = null;
        _raidMonitorActive = false;
        InRaidQuestProgressChanged -= _raidNotification.Show;
        InRaidQuestProgressChanged = null;
        _raidNotification.Dispose();
        _tracking.Dispose();
        _latestQuestController = null;
        _pendingTraderScreen = null;
        _pendingGlobalScreen = null;
        foreach (var controller in _traderControllers.Values) controller.Dispose();
        _traderControllers.Clear();
        foreach (var controller in _globalControllers.Values) controller.Dispose();
        _globalControllers.Clear();
    }

    private IEnumerator LoadAndOverlay(int liveQuestCountBefore)
    {
        Task loadTask;
        try
        {
            loadTask = _adapter.LoadTopologyAsync();
        }
        catch (Exception exception)
        {
            LogFailure(exception);
            _loadCoroutine = null;
            yield break;
        }

        while (!loadTask.IsCompleted) yield return null;
        _loadCoroutine = null;
        if (_disposed) yield break;
        if (loadTask.IsFaulted)
        {
            LogFailure(loadTask.Exception?.GetBaseException() ?? new InvalidOperationException("Unknown topology load failure."));
            yield break;
        }

        var controller = _latestQuestController;
        if (controller is null)
        {
            _log.LogWarning("QUESTMAP_M02_STATE active=False; safelyDisabled=True; reason=quest controller disappeared during topology load");
            yield break;
        }

        var liveQuestCountAfter = controller.Quests.Count;
        if (!RefreshOverlay(controller, false)) yield break;
        _log.LogInfo(
            "QUESTMAP_M02_BOOK " +
            $"before={liveQuestCountBefore}; after={liveQuestCountAfter}; unchanged={liveQuestCountBefore == liveQuestCountAfter}; " +
            "loadAllCalled=False; templatesInjected=False");
        _log.LogInfo(
            "QUESTMAP_M02_STATE active=True; safelyDisabled=False; " +
            $"topologyVersion={_adapter.Topology?.Version}; layoutNodes={_adapter.Layout?.NodesById.Count ?? 0}");
        TryMountPendingTraderScreen(controller);
        TryMountPendingGlobalScreen(controller);
    }

    private bool RefreshOverlay(AbstractQuestControllerClass controller, bool repeated)
    {
        try
        {
            var topology = _adapter.Topology;
            var layout = _adapter.Layout;
            var overlay = _adapter.RefreshOverlay(controller.Quests, controller.Profile);
            _tracking.EnsureFavoritesLoaded(overlay.ProfileId);
            _log.LogInfo(
                "QUESTMAP_M02_REFRESH " +
                $"repeated={repeated}; topologyReused={ReferenceEquals(topology, _adapter.Topology)}; " +
                $"layoutReused={ReferenceEquals(layout, _adapter.Layout)}; overlayQuests={overlay.QuestsById.Count}");
            return true;
        }
        catch (Exception exception)
        {
            LogFailure(exception);
            return false;
        }
    }

    private void RequestReactiveRefresh(string reason, string? questId)
    {
        if (_disposed) return;
        _reactiveReasons.Add(reason);
        if (!string.IsNullOrWhiteSpace(questId)) _reactiveQuestIds.Add(questId!);
        if (_reactiveRefreshCoroutine is null)
        {
            _reactiveRefreshCoroutine = _coroutineOwner.StartCoroutine(CoalescedReactiveRefresh());
        }
    }

    private IEnumerator CoalescedReactiveRefresh()
    {
        yield return null;
        if (_disposed)
        {
            CompleteReactiveRefresh();
            yield break;
        }

        var controller = _latestQuestController;
        var oldOverlay = _adapter.Overlay;
        var originalTopology = _adapter.Topology;
        var originalLayout = _adapter.Layout;
        var reasons = _reactiveReasons.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var signaledQuestIds = _reactiveQuestIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var preferredObjectiveIds = new Dictionary<string, string>(_raidPreferredObjectiveIds, StringComparer.Ordinal);
        _reactiveReasons.Clear();
        _reactiveQuestIds.Clear();
        _raidPreferredObjectiveIds.Clear();
        if (controller is null || oldOverlay is null || originalTopology is null || originalLayout is null)
        {
            CompleteReactiveRefresh();
            yield break;
        }

        var inRaid = InRaidQuestContext.TryCapture(out _);
        if (inRaid && IsTargetedRaidRefresh(reasons, signaledQuestIds)
            && TryApplyTargetedRaidRefresh(
                controller,
                originalTopology,
                oldOverlay,
                reasons,
                signaledQuestIds,
                preferredObjectiveIds))
        {
            CompleteReactiveRefresh();
            yield break;
        }

        var topologyReloaded = !inRaid && RequiresTopologyReload(reasons, signaledQuestIds, originalTopology);
        if (inRaid)
        {
            if (_configuration.EnableDebugLogging.Value)
            {
                _log.LogInfo(
                    "QUESTMAP_M06_RAID_REFRESH " +
                    $"reasons={string.Join(",", reasons)}; serverProfileSkipped=True; topologyReloaded=False");
            }
        }
        else if (topologyReloaded)
        {
            Task topologyTask;
            try
            {
                topologyTask = _adapter.LoadTopologyAsync();
            }
            catch (Exception exception)
            {
                _log.LogError($"QUESTMAP_M04_ERROR phase=topology-reload; reasons={string.Join(",", reasons)}; {exception}");
                CompleteReactiveRefresh();
                yield break;
            }

            while (!topologyTask.IsCompleted) yield return null;
            if (_disposed)
            {
                CompleteReactiveRefresh();
                yield break;
            }
            if (topologyTask.IsFaulted)
            {
                _log.LogError($"QUESTMAP_M04_ERROR phase=topology-reload; reasons={string.Join(",", reasons)}; {topologyTask.Exception?.GetBaseException()}");
                CompleteReactiveRefresh();
                yield break;
            }
        }
        else
        {
            Task<bool> profileTask;
            try
            {
                profileTask = _adapter.RefreshServerProfileAsync();
            }
            catch (Exception exception)
            {
                _log.LogError($"QUESTMAP_M04_ERROR phase=server-profile-refresh; reasons={string.Join(",", reasons)}; {exception}");
                CompleteReactiveRefresh();
                yield break;
            }

            while (!profileTask.IsCompleted) yield return null;
            if (_disposed)
            {
                CompleteReactiveRefresh();
                yield break;
            }
            if (profileTask.IsFaulted)
            {
                _log.LogError($"QUESTMAP_M04_ERROR phase=server-profile-refresh; reasons={string.Join(",", reasons)}; {profileTask.Exception?.GetBaseException()}");
                CompleteReactiveRefresh();
                yield break;
            }
            topologyReloaded = profileTask.Result;
        }

        QuestProfileOverlay newOverlay;
        try
        {
            var overlayStartedAt = Stopwatch.GetTimestamp();
            newOverlay = _adapter.RefreshOverlay(controller.Quests, controller.Profile, false);
            if (inRaid) RecordRaidOverlayRefresh(ElapsedMilliseconds(overlayStartedAt));
            _reactiveMonitor?.Resync();
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M04_ERROR phase=overlay-refresh; reasons={string.Join(",", reasons)}; {exception}");
            CompleteReactiveRefresh();
            yield break;
        }

        var changedQuestIds = oldOverlay.QuestsById.Keys
            .Concat(newOverlay.QuestsById.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(questId => QuestStateChanged(oldOverlay, newOverlay, questId))
            .OrderBy(questId => questId, StringComparer.Ordinal)
            .ToArray();
        _tracking.ApplyNewQuestTransitions(oldOverlay, newOverlay, changedQuestIds);
        var topology = _adapter.Topology!;
        var layout = _adapter.Layout!;
        if (inRaid) PublishRaidProgressChanges(
            topology,
            oldOverlay,
            newOverlay,
            changedQuestIds,
            preferredObjectiveIds);
        var selectionConsequences = _traderControllers.Values
            .Select(graph => topologyReloaded
                ? graph.RebuildTopology(topology, layout, newOverlay)
                : graph.RefreshOverlay(newOverlay))
            .Concat(_globalControllers.Values.Select(graph => topologyReloaded
                ? graph.RebuildTopology(topology, layout, newOverlay)
                : graph.RefreshOverlay(newOverlay)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        if (_configuration.EnableDebugLogging.Value)
        {
            foreach (var questId in changedQuestIds)
            {
                var oldStatus = oldOverlay.QuestsById.TryGetValue(questId, out var oldState) && oldState.HasLiveQuest
                    ? oldState.ExactStatus
                    : "not-live";
                var newStatus = newOverlay.QuestsById.TryGetValue(questId, out var newState) && newState.HasLiveQuest
                    ? newState.ExactStatus
                    : "not-live";
                _log.LogInfo($"QUESTMAP_M04_TRANSITION quest={questId}; old={oldStatus}; new={newStatus}");
            }

            _log.LogInfo(
                "QUESTMAP_M04_REFRESH " +
                $"reasons={string.Join(",", reasons)}; signaledQuests={string.Join(",", signaledQuestIds)}; " +
                $"changedQuests={changedQuestIds.Length}; topologyInvalidated={topologyReloaded}; " +
                $"topologyReused={ReferenceEquals(originalTopology, _adapter.Topology)}; " +
                $"layoutReused={ReferenceEquals(originalLayout, _adapter.Layout)}; " +
                $"selection={string.Join(",", selectionConsequences)}; detail=native-owned");
        }

        CompleteReactiveRefresh();
    }

    private bool TryApplyTargetedRaidRefresh(
        AbstractQuestControllerClass controller,
        QuestGraphTopology topology,
        QuestProfileOverlay oldOverlay,
        IReadOnlyCollection<string> reasons,
        IReadOnlyCollection<string> signaledQuestIds,
        IReadOnlyDictionary<string, string> preferredObjectiveIds)
    {
        try
        {
            var requestedIds = signaledQuestIds.ToHashSet(StringComparer.Ordinal);
            var liveQuests = controller.Quests
                .Where(quest => requestedIds.Contains(quest.Id))
                .GroupBy(quest => quest.Id, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            if (liveQuests.Length != requestedIds.Count)
            {
                _log.LogWarning(
                    "QUESTMAP_M06_RAID_PATCH_FALLBACK " +
                    $"reason=missing-live-quest; requested={requestedIds.Count}; found={liveQuests.Length}");
                return false;
            }

            var modelStartedAt = Stopwatch.GetTimestamp();
            var newOverlay = _adapter.RefreshLiveQuests(liveQuests, out var previousStates);
            var modelMilliseconds = ElapsedMilliseconds(modelStartedAt);
            var targetedOldOverlay = oldOverlay with
            {
                QuestsById = previousStates
                    .Where(pair => pair.Value is not null)
                    .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal),
            };
            var changedQuestIds = requestedIds
                .Where(questId => QuestStateChanged(targetedOldOverlay, newOverlay, questId))
                .OrderBy(questId => questId, StringComparer.Ordinal)
                .ToArray();
            _tracking.ApplyNewQuestTransitions(targetedOldOverlay, newOverlay, changedQuestIds);
            PublishRaidProgressChanges(
                topology,
                targetedOldOverlay,
                newOverlay,
                changedQuestIds,
                preferredObjectiveIds);

            var uiStartedAt = Stopwatch.GetTimestamp();
            var selectionConsequences = changedQuestIds
                .SelectMany(questId => _traderControllers.Values
                    .Select(graph => graph.RefreshQuest(newOverlay, questId))
                    .Concat(_globalControllers.Values.Select(graph => graph.RefreshQuest(newOverlay, questId))))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var uiMilliseconds = ElapsedMilliseconds(uiStartedAt);
            RecordRaidQuestPatch(modelMilliseconds, uiMilliseconds);

            if (_configuration.EnableDebugLogging.Value)
            {
                foreach (var questId in changedQuestIds)
                {
                    var oldStatus = targetedOldOverlay.QuestsById.TryGetValue(questId, out var oldState) && oldState.HasLiveQuest
                        ? oldState.ExactStatus
                        : "not-live";
                    var newStatus = newOverlay.QuestsById.TryGetValue(questId, out var newState) && newState.HasLiveQuest
                        ? newState.ExactStatus
                        : "not-live";
                    _log.LogInfo($"QUESTMAP_M04_TRANSITION quest={questId}; old={oldStatus}; new={newStatus}");
                }
            }

            _log.LogInfo(
                "QUESTMAP_M06_RAID_PATCH " +
                $"reasons={string.Join(",", reasons)}; requested={requestedIds.Count}; changed={changedQuestIds.Length}; " +
                $"modelMs={modelMilliseconds:0.###}; uiMs={uiMilliseconds:0.###}; " +
                $"selection={string.Join(",", selectionConsequences)}; topologyRebuilt=False; projectionRebuilt=False");
            return true;
        }
        catch (Exception exception)
        {
            _log.LogError(
                "QUESTMAP_M06_RAID_PATCH_FALLBACK " +
                $"reason=exception; events={string.Join(",", reasons)}; {exception}");
            return false;
        }
    }

    private static bool IsTargetedRaidRefresh(
        IReadOnlyCollection<string> reasons,
        IReadOnlyCollection<string> signaledQuestIds) =>
        signaledQuestIds.Count > 0
        && reasons.Count > 0
        && reasons.All(reason => reason is "raid-objective-event" or "raid-status-event");

    private void EndRaidMonitor()
    {
        if (!_raidMonitorActive) return;
        _log.LogInfo(
            "QUESTMAP_M06_RAID_MONITOR active=False; " +
            $"location={_raidLocationId}; reason=raid-ended");
        _raidMonitorActive = false;
        _raidQuestController = null;
        _raidLocationId = null;
        _raidProgressMonitor?.Dispose();
        _raidProgressMonitor = null;
        _raidPreferredObjectiveIds.Clear();
        LogRaidPerformance(true);
        _raidNotification.Hide();
        _tracking.EndRaid();
        _reactiveMonitor?.Dispose();
        _reactiveMonitor = null;
        _latestQuestController = null;
    }

    private void PublishRaidProgressChanges(
        QuestGraphTopology topology,
        QuestProfileOverlay oldOverlay,
        QuestProfileOverlay newOverlay,
        IReadOnlyCollection<string> changedQuestIds,
        IReadOnlyDictionary<string, string> preferredObjectiveIds)
    {
        foreach (var questId in changedQuestIds)
        {
            if (!topology.NodesById.TryGetValue(questId, out var node)
                || !newOverlay.QuestsById.TryGetValue(questId, out var current)
                || !current.HasLiveQuest)
            {
                continue;
            }

            var tracking = _tracking.Resolve(newOverlay.ProfileId, node);
            if (!tracking.Tracked)
            {
                if (_configuration.EnableDebugLogging.Value)
                    _log.LogInfo($"QUESTMAP_M06_RAID_NOTIFICATION_SUPPRESSED quest={questId}; reason=untracked");
                continue;
            }

            oldOverlay.QuestsById.TryGetValue(questId, out var previous);
            var previousById = previous?.Objectives
                .GroupBy(objective => objective.ObjectiveId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
                ?? new Dictionary<string, QuestObjectiveProgress>(StringComparer.Ordinal);
            var changedObjectives = current.Objectives
                .Where(objective => !previousById.TryGetValue(objective.ObjectiveId, out var prior) || prior != objective)
                .ToArray();
            var notificationObjectives = changedObjectives
                .Where(objective => ShouldNotifyProgress(objective, previousById))
                .ToArray();

            if (notificationObjectives.Length == 0)
            {
                if (!string.Equals(previous?.ExactStatus, current.ExactStatus, StringComparison.Ordinal))
                {
                    InRaidQuestProgressChanged?.Invoke(new InRaidQuestProgressChange(
                        node,
                        null,
                        null,
                        current.ExactStatus ?? "Unknown"));
                }
                continue;
            }

            var progress = notificationObjectives
                .OrderByDescending(candidate => IsLeafObjective(node, candidate.ObjectiveId))
                .ThenByDescending(candidate => NumericProgressChanged(candidate, previousById))
                .ThenByDescending(candidate => EffectivePositiveProgressDelta(candidate, previousById))
                .ThenBy(candidate => candidate.Complete)
                .ThenByDescending(candidate => IsPreferredObjective(questId, candidate.ObjectiveId, preferredObjectiveIds))
                .ThenByDescending(candidate => ObjectiveIndex(node, candidate.ObjectiveId))
                .First();
            var definition = node.Objectives.FirstOrDefault(objective =>
                string.Equals(objective.Id, progress.ObjectiveId, StringComparison.Ordinal));
            _log.LogInfo(
                "QUESTMAP_M06_RAID_NOTIFICATION " +
                $"quest={questId}; objective={progress.ObjectiveId}; candidates={notificationObjectives.Length}; " +
                $"suppressed={changedObjectives.Length - notificationObjectives.Length}; " +
                $"leaf={IsLeafObjective(node, progress.ObjectiveId)}; complete={progress.Complete}; " +
                $"preferred={IsPreferredObjective(questId, progress.ObjectiveId, preferredObjectiveIds)}; " +
                $"numericDelta={NumericDelta(progress, previousById):0.##}; effectiveDelta={EffectivePositiveProgressDelta(progress, previousById):0.##}; " +
                $"candidateDetail={DescribeCandidates(node, notificationObjectives, previousById, preferredObjectiveIds, questId)}");
            InRaidQuestProgressChanged?.Invoke(new InRaidQuestProgressChange(
                node,
                definition,
                progress,
                current.ExactStatus ?? "Unknown"));
        }
    }

    private static bool IsLeafObjective(QuestGraphNode node, string objectiveId) =>
        !node.Objectives.Any(objective =>
            string.Equals(objective.ParentId, objectiveId, StringComparison.Ordinal)
            || objective.DependsOn.Contains(objectiveId, StringComparer.Ordinal));

    private static bool NumericProgressChanged(
        QuestObjectiveProgress current,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById) =>
        current.Current.HasValue
        && (!previousById.TryGetValue(current.ObjectiveId, out var previous)
            || !previous.Current.HasValue
            || Math.Abs(current.Current.Value - previous.Current.Value) > 0.0001d);

    private static bool ShouldNotifyProgress(
        QuestObjectiveProgress current,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById)
    {
        previousById.TryGetValue(current.ObjectiveId, out var previous);
        return QuestProgressRules.HasEffectiveIncrease(current, previous);
    }

    private static double EffectivePositiveProgressDelta(
        QuestObjectiveProgress current,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById) =>
        Math.Max(0d, QuestProgressRules.EffectiveValue(current)
            - (previousById.TryGetValue(current.ObjectiveId, out var previous)
                ? QuestProgressRules.EffectiveValue(previous)
                : 0d));

    private static double NumericDelta(
        QuestObjectiveProgress current,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById) =>
        current.Current.GetValueOrDefault()
        - (previousById.TryGetValue(current.ObjectiveId, out var previous)
            ? previous.Current.GetValueOrDefault()
            : 0d);

    private static bool IsPreferredObjective(
        string questId,
        string objectiveId,
        IReadOnlyDictionary<string, string> preferredObjectiveIds) =>
        preferredObjectiveIds.TryGetValue(questId, out var preferred)
        && string.Equals(preferred, objectiveId, StringComparison.Ordinal);

    private static int ObjectiveIndex(QuestGraphNode node, string objectiveId) =>
        node.Objectives.FirstOrDefault(objective =>
            string.Equals(objective.Id, objectiveId, StringComparison.Ordinal))?.Index ?? int.MinValue;

    private static string DescribeCandidates(
        QuestGraphNode node,
        IReadOnlyCollection<QuestObjectiveProgress> candidates,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById,
        IReadOnlyDictionary<string, string> preferredObjectiveIds,
        string questId) =>
        string.Join(",", candidates.Select(candidate =>
            $"{candidate.ObjectiveId}:{ObjectiveIndex(node, candidate.ObjectiveId)}:" +
            $"{NumericDelta(candidate, previousById):0.##}:{candidate.Complete}:" +
            $"{IsPreferredObjective(questId, candidate.ObjectiveId, preferredObjectiveIds)}"));

    private void RequestRaidProgressRefresh(
        string questId,
        string? objectiveId,
        RaidQuestChangeKind changeKind)
    {
        if (!string.IsNullOrWhiteSpace(questId) && !string.IsNullOrWhiteSpace(objectiveId))
            _raidPreferredObjectiveIds[questId] = objectiveId!;
        RequestReactiveRefresh(
            changeKind switch
            {
                RaidQuestChangeKind.Objective => "raid-objective-event",
                RaidQuestChangeKind.Status => "raid-status-event",
                RaidQuestChangeKind.Book => "raid-book-event",
                _ => throw new ArgumentOutOfRangeException(nameof(changeKind), changeKind, null),
            },
            string.IsNullOrWhiteSpace(questId) ? null : questId);
    }

    private void ResetRaidPerformanceTelemetry()
    {
        _raidPollCount = 0;
        _raidPolledCheckerCount = 0;
        _raidPollChangeCount = 0;
        _raidOverlayRefreshCount = 0;
        _raidQuestPatchCount = 0;
        _raidPollTotalMilliseconds = 0;
        _raidPollMaximumMilliseconds = 0;
        _raidOverlayTotalMilliseconds = 0;
        _raidOverlayMaximumMilliseconds = 0;
        _raidQuestPatchTotalMilliseconds = 0;
        _raidQuestPatchMaximumMilliseconds = 0;
        _raidQuestUiTotalMilliseconds = 0;
        _raidQuestUiMaximumMilliseconds = 0;
        _nextRaidPerformanceLogAt = Time.unscaledTime + RaidPerformanceLogIntervalSeconds;
    }

    private void RecordRaidPoll(double elapsedMilliseconds, RaidQuestPollResult result)
    {
        _raidPollCount++;
        _raidPolledCheckerCount += result.ScannedCheckers;
        _raidPollChangeCount += result.Changes;
        _raidPollTotalMilliseconds += elapsedMilliseconds;
        _raidPollMaximumMilliseconds = Math.Max(_raidPollMaximumMilliseconds, elapsedMilliseconds);
    }

    private void RecordRaidOverlayRefresh(double elapsedMilliseconds)
    {
        _raidOverlayRefreshCount++;
        _raidOverlayTotalMilliseconds += elapsedMilliseconds;
        _raidOverlayMaximumMilliseconds = Math.Max(_raidOverlayMaximumMilliseconds, elapsedMilliseconds);
    }

    private void RecordRaidQuestPatch(double modelMilliseconds, double uiMilliseconds)
    {
        _raidQuestPatchCount++;
        _raidQuestPatchTotalMilliseconds += modelMilliseconds;
        _raidQuestPatchMaximumMilliseconds = Math.Max(_raidQuestPatchMaximumMilliseconds, modelMilliseconds);
        _raidQuestUiTotalMilliseconds += uiMilliseconds;
        _raidQuestUiMaximumMilliseconds = Math.Max(_raidQuestUiMaximumMilliseconds, uiMilliseconds);
    }

    private void LogRaidPerformanceIfDue()
    {
        if (Time.unscaledTime < _nextRaidPerformanceLogAt) return;
        LogRaidPerformance(false);
        _nextRaidPerformanceLogAt = Time.unscaledTime + RaidPerformanceLogIntervalSeconds;
    }

    private void LogRaidPerformance(bool final)
    {
        if (_raidPollCount == 0) return;
        _log.LogInfo(
            "QUESTMAP_M06_RAID_PERF " +
            $"final={final}; polls={_raidPollCount}; scannedCheckers={_raidPolledCheckerCount}; " +
            $"pollChanges={_raidPollChangeCount}; " +
            $"pollAvgMs={_raidPollTotalMilliseconds / _raidPollCount:0.###}; pollMaxMs={_raidPollMaximumMilliseconds:0.###}; " +
            $"overlayRefreshes={_raidOverlayRefreshCount}; " +
            $"overlayAvgMs={(_raidOverlayRefreshCount == 0 ? 0 : _raidOverlayTotalMilliseconds / _raidOverlayRefreshCount):0.###}; " +
            $"overlayMaxMs={_raidOverlayMaximumMilliseconds:0.###}; questPatches={_raidQuestPatchCount}; " +
            $"patchAvgMs={(_raidQuestPatchCount == 0 ? 0 : _raidQuestPatchTotalMilliseconds / _raidQuestPatchCount):0.###}; " +
            $"patchMaxMs={_raidQuestPatchMaximumMilliseconds:0.###}; " +
            $"patchUiAvgMs={(_raidQuestPatchCount == 0 ? 0 : _raidQuestUiTotalMilliseconds / _raidQuestPatchCount):0.###}; " +
            $"patchUiMaxMs={_raidQuestUiMaximumMilliseconds:0.###}; broadReactiveEvents=False");
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;

    private void CompleteReactiveRefresh()
    {
        _reactiveRefreshCoroutine = null;
        if (!_disposed && _reactiveReasons.Count > 0)
        {
            _reactiveRefreshCoroutine = _coroutineOwner.StartCoroutine(CoalescedReactiveRefresh());
        }
    }

    private static bool RequiresTopologyReload(
        IReadOnlyCollection<string> reasons,
        IReadOnlyCollection<string> signaledQuestIds,
        QuestGraphTopology topology)
    {
        if (reasons.Contains("repeatable-expired")) return true;
        if (signaledQuestIds.Any(questId => !topology.NodesById.ContainsKey(questId))) return true;
        if (reasons.Any(reason => reason == "quest-book-removed" || reason == "quest-book-removed-range")
            && signaledQuestIds.Any(questId =>
                topology.NodesById.TryGetValue(questId, out var node) && node.ProfileGenerated))
        {
            return true;
        }

        return false;
    }

    private static bool QuestStateChanged(
        QuestProfileOverlay oldOverlay,
        QuestProfileOverlay newOverlay,
        string questId)
    {
        var hasOld = oldOverlay.QuestsById.TryGetValue(questId, out var oldState);
        var hasNew = newOverlay.QuestsById.TryGetValue(questId, out var newState);
        if (!hasOld || !hasNew) return true;
        if (oldState!.HasLiveQuest != newState!.HasLiveQuest
            || oldState.ExactStatus != newState.ExactStatus
            || oldState.Visible != newState.Visible
            || oldState.ExpirationTime != newState.ExpirationTime
            || oldState.HandoverReady != newState.HandoverReady
            || oldState.Objectives.Count != newState.Objectives.Count)
        {
            return true;
        }

        for (var index = 0; index < oldState.Objectives.Count; index++)
        {
            if (oldState.Objectives[index] != newState.Objectives[index]) return true;
        }
        return false;
    }

    private void LogFailure(Exception exception)
    {
        _log.LogError($"QUESTMAP_M02_ERROR {exception}");
        _log.LogWarning("QUESTMAP_M02_STATE active=False; safelyDisabled=True; reason=read-only data adapter failed; vanilla UI retained");
        FailPendingTraderScreen("read-only data adapter failed", exception);
        FailPendingGlobalScreen("read-only data adapter failed", exception);
    }

    private void TryMountPendingTraderScreen(AbstractQuestControllerClass questController)
    {
        var pending = _pendingTraderScreen;
        if (pending is null
            || !ReferenceEquals(pending.QuestController, questController)
            || !_configuration.EnableTraderQuestGraph.Value)
        {
            return;
        }

        var topology = _adapter.Topology;
        var layout = _adapter.Layout;
        var overlay = _adapter.Overlay;
        if (topology is null || layout is null || overlay is null) return;

        _pendingTraderScreen = null;
        var controller = new TraderGraphScreenController(
            pending.Screen,
            pending.Session,
            pending.InventoryController,
            pending.QuestController,
            pending.Trader,
            _log,
            _configuration.EnableDebugLogging.Value,
            _assetCache);
        try
        {
            controller.Mount(
                topology,
                layout,
                overlay,
                _configuration.ForceTraderGraphInitializationFailure.Value);
            _traderControllers[pending.Screen] = controller;
            _log.LogInfo($"QUESTMAP_M03_STATE screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; active=True; safelyDisabled=False; vanillaRestored=False");
        }
        catch (Exception exception)
        {
            controller.Dispose();
            _log.LogError($"QUESTMAP_M03_ERROR screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; {exception}");
            _log.LogWarning($"QUESTMAP_M03_STATE screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; active=False; safelyDisabled=True; reason=graph initialization failed; vanillaRestored=True");
        }
    }

    private void FailPendingTraderScreen(string reason, Exception exception)
    {
        var pending = _pendingTraderScreen;
        if (pending is null) return;
        _pendingTraderScreen = null;
        _log.LogError($"QUESTMAP_M03_ERROR screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; reason={reason}; {exception}");
        _log.LogWarning($"QUESTMAP_M03_STATE screen={pending.Screen.GetInstanceID()}; trader={pending.Trader.Id}; active=False; safelyDisabled=True; reason={reason}; vanillaRestored=True");
    }

    private void TryMountPendingGlobalScreen(AbstractQuestControllerClass questController)
    {
        var pending = _pendingGlobalScreen;
        if (pending is null
            || !ReferenceEquals(pending.QuestController, questController)
            || !pending.VanillaLayoutReady
            || !_configuration.EnableGlobalTasksGraph.Value)
        {
            return;
        }

        var topology = _adapter.Topology;
        var layout = _adapter.Layout;
        var overlay = _adapter.Overlay;
        if (topology is null || layout is null || overlay is null) return;

        _pendingGlobalScreen = null;
        var controller = new GlobalTasksScreenController(
            pending.Screen,
            pending.InventoryController,
            pending.QuestController,
            pending.Session,
            _log,
            _configuration.EnableDebugLogging.Value,
            _assetCache,
            _tracking);
        try
        {
            controller.Mount(
                topology,
                layout,
                overlay,
                _configuration.ForceGlobalTasksGraphInitializationFailure.Value);
            _globalControllers[pending.Screen] = controller;
            _log.LogInfo($"QUESTMAP_M06_STATE screen={pending.Screen.GetInstanceID()}; active=True; safelyDisabled=False; vanillaRestored=False");
        }
        catch (Exception exception)
        {
            controller.Dispose();
            _log.LogError($"QUESTMAP_M06_ERROR screen={pending.Screen.GetInstanceID()}; {exception}");
            _log.LogWarning($"QUESTMAP_M06_STATE screen={pending.Screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=graph initialization failed; vanillaRestored=True");
        }
    }

    private void FailPendingGlobalScreen(string reason, Exception exception)
    {
        var pending = _pendingGlobalScreen;
        if (pending is null) return;
        _pendingGlobalScreen = null;
        _log.LogError($"QUESTMAP_M06_ERROR screen={pending.Screen.GetInstanceID()}; reason={reason}; {exception}");
        _log.LogWarning($"QUESTMAP_M06_STATE screen={pending.Screen.GetInstanceID()}; active=False; safelyDisabled=True; reason={reason}; vanillaRestored=True");
    }

    private IEnumerator MountPendingGlobalAfterVanillaLayout()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        _globalMountCoroutine = null;
        if (_disposed) yield break;
        var pending = _pendingGlobalScreen;
        if (pending is null) yield break;
        pending.VanillaLayoutReady = true;
        TryMountPendingGlobalScreen(pending.QuestController);
    }

    private sealed class PendingTraderScreen
    {
        public PendingTraderScreen(
            QuestsScreen screen,
            ISession session,
            InventoryController inventoryController,
            AbstractQuestControllerClass questController,
            TraderClass trader)
        {
            Screen = screen;
            Session = session;
            InventoryController = inventoryController;
            QuestController = questController;
            Trader = trader;
        }

        public QuestsScreen Screen { get; }

        public ISession Session { get; }

        public InventoryController InventoryController { get; }

        public AbstractQuestControllerClass QuestController { get; }

        public TraderClass Trader { get; }
    }

    private sealed class PendingGlobalScreen
    {
        public PendingGlobalScreen(
            TasksScreen screen,
            InventoryController inventoryController,
            AbstractQuestControllerClass questController,
            ISession session)
        {
            Screen = screen;
            InventoryController = inventoryController;
            QuestController = questController;
            Session = session;
        }

        public TasksScreen Screen { get; }

        public InventoryController InventoryController { get; }

        public AbstractQuestControllerClass QuestController { get; }

        public ISession Session { get; }

        public bool VanillaLayoutReady { get; set; }
    }
}

internal readonly struct InRaidQuestProgressChange
{
    public InRaidQuestProgressChange(
        QuestGraphNode quest,
        QuestObjectiveDefinition? objective,
        QuestObjectiveProgress? progress,
        string exactStatus)
    {
        Quest = quest;
        Objective = objective;
        Progress = progress;
        ExactStatus = exactStatus;
    }

    public QuestGraphNode Quest { get; }

    public QuestObjectiveDefinition? Objective { get; }

    public QuestObjectiveProgress? Progress { get; }

    public string ExactStatus { get; }
}
