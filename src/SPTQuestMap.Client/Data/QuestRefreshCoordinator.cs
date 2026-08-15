using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT.InventoryLogic;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.UI;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestRefreshCoordinator : IDisposable
{
    private readonly MonoBehaviour _coroutineOwner;
    private readonly ManualLogSource _log;
    private readonly QuestMapClientConfiguration _configuration;
    private readonly QuestMapDataAdapter _adapter;
    private readonly QuestTrackingService _tracking;
    private readonly QuestScreenCoordinator _screens;
    private readonly RaidQuestRuntime _raid;
    private readonly QuestActionReconciler _actionReconciler;
    private readonly HashSet<string> _reasons = new(StringComparer.Ordinal);
    private readonly HashSet<string> _questIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _preferredObjectiveIds = new(StringComparer.Ordinal);
    private AbstractQuestControllerClass? _currentController;
    private ReactiveQuestMonitor? _reactiveMonitor;
    private Coroutine? _refreshCoroutine;
    private int _requiredTopologyRefreshGeneration;
    private int _completedTopologyRefreshGeneration;
    private bool _disposed;

    public QuestRefreshCoordinator(
        MonoBehaviour coroutineOwner,
        ManualLogSource log,
        QuestMapClientConfiguration configuration,
        QuestMapDataAdapter adapter,
        QuestTrackingService tracking,
        QuestScreenCoordinator screens,
        RaidQuestRuntime raid)
    {
        _coroutineOwner = coroutineOwner;
        _log = log;
        _configuration = configuration;
        _adapter = adapter;
        _tracking = tracking;
        _screens = screens;
        _raid = raid;
        _actionReconciler = new QuestActionReconciler(
            coroutineOwner,
            log,
            adapter,
            () => _currentController,
            Request);
    }

    public AbstractQuestControllerClass? CurrentController => _currentController;

    public void ObserveController(AbstractQuestControllerClass questController, bool enableReactiveMonitor)
    {
        if (_disposed) return;
        if (!ReferenceEquals(_currentController, questController))
        {
            _reactiveMonitor?.Dispose();
            _reactiveMonitor = null;
        }
        if (enableReactiveMonitor)
        {
            _reactiveMonitor ??= new ReactiveQuestMonitor(questController, Request);
        }
        else if (_reactiveMonitor is not null)
        {
            _reactiveMonitor.Dispose();
            _reactiveMonitor = null;
        }
        _currentController = questController;
    }

    public void ClearObservedController()
    {
        _reactiveMonitor?.Dispose();
        _reactiveMonitor = null;
        _currentController = null;
    }

    public void SetInventoryController(InventoryController? inventoryController) =>
        _reactiveMonitor?.SetInventoryController(inventoryController);

    public bool RefreshOverlay(AbstractQuestControllerClass controller, bool repeated)
    {
        try
        {
            var topology = _adapter.Topology;
            var layout = _adapter.Layout;
            var overlay = _adapter.RefreshOverlay(controller.Quests, controller.Profile);
            _tracking.EnsureFavoritesLoaded(overlay.ProfileId);
            QuestMapDebugLog.Info(_log,
                "QUESTMAP_M02_REFRESH " +
                $"repeated={repeated}; topologyReused={ReferenceEquals(topology, _adapter.Topology)}; " +
                $"layoutReused={ReferenceEquals(layout, _adapter.Layout)}; overlayQuests={overlay.QuestsById.Count}");
            return true;
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M02_ERROR {exception}");
            _log.LogWarning("QUESTMAP_M02_STATE active=False; safelyDisabled=True; reason=read-only data adapter failed; vanilla UI retained");
            _screens.FailPendingScreens("read-only data adapter failed", exception);
            return false;
        }
    }

    public void Request(string reason, string? questId) => Request(reason, questId, null);

    public void Request(string reason, string? questId, string? preferredObjectiveId)
    {
        if (_disposed) return;
        if (string.Equals(reason, "raid-ended", StringComparison.Ordinal))
            _requiredTopologyRefreshGeneration++;
        _reasons.Add(reason);
        if (!string.IsNullOrWhiteSpace(questId)) _questIds.Add(questId!);
        if (!string.IsNullOrWhiteSpace(questId) && !string.IsNullOrWhiteSpace(preferredObjectiveId))
            _preferredObjectiveIds[questId!] = preferredObjectiveId!;
        if (_refreshCoroutine is null)
            _refreshCoroutine = _coroutineOwner.StartCoroutine(CoalescedRefresh());
    }

    public void ScheduleActionReconciliation(string questId, QuestDetailsActionKind action) =>
        _actionReconciler.Schedule(questId, action);

    public void ClearRaidSignals() => _preferredObjectiveIds.Clear();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_refreshCoroutine is not null) _coroutineOwner.StopCoroutine(_refreshCoroutine);
        _refreshCoroutine = null;
        _actionReconciler.Dispose();
        ClearObservedController();
        _reasons.Clear();
        _questIds.Clear();
        _preferredObjectiveIds.Clear();
    }

    private IEnumerator CoalescedRefresh()
    {
        yield return null;
        if (_disposed)
        {
            CompleteRefresh();
            yield break;
        }

        var controller = _currentController;
        var oldOverlay = _adapter.Overlay;
        var originalTopology = _adapter.Topology;
        var originalLayout = _adapter.Layout;
        var reasons = _reasons.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var signaledQuestIds = _questIds.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var preferredObjectiveIds = new Dictionary<string, string>(_preferredObjectiveIds, StringComparer.Ordinal);
        var requiredTopologyRefreshGeneration = _requiredTopologyRefreshGeneration;
        var forceFullTopologyReload = requiredTopologyRefreshGeneration > _completedTopologyRefreshGeneration;
        _reasons.Clear();
        _questIds.Clear();
        _preferredObjectiveIds.Clear();
        var replacementStillSettling = _actionReconciler.HasPending
            && reasons.Any(reason => reason is
                "quest-book-added" or "quest-book-added-range"
                or "quest-book-removed" or "quest-book-removed-range");
        if (replacementStillSettling)
        {
            QuestMapDebugLog.Info(_log,
                "QUESTMAP_M07_REPLACE_DEFER " +
                $"reasons={string.Join(",", reasons)}; quests={string.Join(",", signaledQuestIds)}; " +
                "waitingForReplacementQuest=True");
            CompleteRefresh();
            yield break;
        }
        if (originalTopology is null || originalLayout is null
            || (!forceFullTopologyReload && (controller is null || oldOverlay is null)))
        {
            CompleteRefresh();
            yield break;
        }

        var inRaid = InRaidQuestContext.TryCapture(out _);
        if (inRaid && controller is not null && oldOverlay is not null
            && IsTargetedRaidRefresh(reasons, signaledQuestIds)
            && TryApplyTargetedRaidRefresh(
                controller,
                originalTopology,
                oldOverlay,
                reasons,
                signaledQuestIds,
                preferredObjectiveIds))
        {
            CompleteRefresh();
            yield break;
        }

        var topologyReloaded = !inRaid
            && (forceFullTopologyReload || RequiresTopologyReload(reasons, signaledQuestIds, originalTopology));
        var topologyUpdate = QuestTopologyUpdateKind.None;
        if (inRaid)
        {
            if (_configuration.EnableDebugLogging.Value)
            {
                QuestMapDebugLog.Info(_log,
                    "QUESTMAP_M06_RAID_REFRESH " +
                    $"reasons={string.Join(",", reasons)}; serverProfileSkipped=True; topologyReloaded=False");
            }
        }
        else if (topologyReloaded)
        {
            Task<QuestTopologyUpdateKind> topologyTask;
            try
            {
                topologyTask = forceFullTopologyReload
                    ? _adapter.LoadTopologyAsync()
                    : reasons.Contains("quest-details-replace") || reasons.Contains("repeatable-expired")
                    ? _adapter.LoadRepeatableTopologyDeltaAsync()
                    : _adapter.LoadTopologyAsync();
            }
            catch (Exception exception)
            {
                _log.LogError($"QUESTMAP_M04_ERROR phase=topology-reload; reasons={string.Join(",", reasons)}; {exception}");
                CompleteRefresh();
                yield break;
            }

            while (!topologyTask.IsCompleted) yield return null;
            if (_disposed)
            {
                CompleteRefresh();
                yield break;
            }
            if (topologyTask.IsFaulted)
            {
                _log.LogError($"QUESTMAP_M04_ERROR phase=topology-reload; reasons={string.Join(",", reasons)}; {topologyTask.Exception?.GetBaseException()}");
                CompleteRefresh();
                yield break;
            }
            topologyUpdate = topologyTask.Result;
            if (forceFullTopologyReload)
                _completedTopologyRefreshGeneration = Math.Max(
                    _completedTopologyRefreshGeneration,
                    requiredTopologyRefreshGeneration);
        }
        else
        {
            Task<QuestTopologyUpdateKind> profileTask;
            try
            {
                profileTask = _adapter.RefreshServerProfileAsync();
            }
            catch (Exception exception)
            {
                _log.LogError($"QUESTMAP_M04_ERROR phase=server-profile-refresh; reasons={string.Join(",", reasons)}; {exception}");
                CompleteRefresh();
                yield break;
            }

            while (!profileTask.IsCompleted) yield return null;
            if (_disposed)
            {
                CompleteRefresh();
                yield break;
            }
            if (profileTask.IsFaulted)
            {
                _log.LogError($"QUESTMAP_M04_ERROR phase=server-profile-refresh; reasons={string.Join(",", reasons)}; {profileTask.Exception?.GetBaseException()}");
                CompleteRefresh();
                yield break;
            }
            topologyUpdate = profileTask.Result;
        }
        topologyReloaded = topologyUpdate != QuestTopologyUpdateKind.None;

        controller = _currentController;
        if (controller is null || oldOverlay is null)
        {
            QuestMapDebugLog.Info(_log,
                "QUESTMAP_M04_POST_RAID_TOPOLOGY " +
                $"completed=True; topologyVersion={_adapter.Topology?.Version}; " +
                "overlayDeferred=True; reason=no-menu-quest-controller");
            CompleteRefresh();
            yield break;
        }

        QuestProfileOverlay newOverlay;
        try
        {
            var overlayStartedAt = Stopwatch.GetTimestamp();
            newOverlay = _adapter.RefreshOverlay(controller.Quests, controller.Profile, false);
            if (inRaid) _raid.RecordOverlayRefresh(ElapsedMilliseconds(overlayStartedAt));
            _reactiveMonitor?.Resync();
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M04_ERROR phase=overlay-refresh; reasons={string.Join(",", reasons)}; {exception}");
            CompleteRefresh();
            yield break;
        }

        var changedQuestIds = QuestOverlayChangeDetector.FindChangedQuestIds(oldOverlay, newOverlay);
        var topology = _adapter.Topology!;
        _tracking.ApplyNewQuestTransitions(oldOverlay, newOverlay, changedQuestIds, topology.NodesById);
        var layout = _adapter.Layout!;
        if (inRaid)
            _raid.PublishProgressChanges(topology, oldOverlay, newOverlay, changedQuestIds, preferredObjectiveIds);
        if (inRaid) _raid.RefreshTrackedListIfVisible();
        var refreshAllNativeControls = !reasons.Contains("quest-details-action-settled");
        var repeatableTopologyDelta = topologyUpdate == QuestTopologyUpdateKind.ProfileGeneratedDelta;
        var pendingHandoverIds = _actionReconciler.PendingHandoverQuestIds.ToHashSet(StringComparer.Ordinal);
        var forcedActionQuestIds = reasons.Any(reason => reason is
                "quest-details-action-settled" or "quest-details-action-timeout")
            ? signaledQuestIds
            : [];
        var uiChangedQuestIds = changedQuestIds
            .Concat(forcedActionQuestIds)
            .Where(questId => !pendingHandoverIds.Contains(questId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(questId => questId, StringComparer.Ordinal)
            .ToArray();
        var batchChangedQuests = !topologyReloaded
            && (uiChangedQuestIds.Length > 0 || pendingHandoverIds.Count > 0);
        string[] selectionConsequences;
        try
        {
            selectionConsequences = batchChangedQuests
                ? _screens.RefreshQuests(newOverlay, uiChangedQuestIds)
                : _screens.RefreshAll(
                    topologyReloaded,
                    repeatableTopologyDelta,
                    topology,
                    layout,
                    newOverlay,
                    refreshAllNativeControls);
        }
        catch (Exception exception)
        {
            _log.LogError(
                "QUESTMAP_M07_UI_REFRESH_ERROR " +
                $"reasons={string.Join(",", reasons)}; topologyDelta={repeatableTopologyDelta}; " +
                $"batchedQuests={batchChangedQuests}; {exception}");
            CompleteRefresh();
            yield break;
        }

        if (_configuration.EnableDebugLogging.Value)
        {
            LogTransitions(oldOverlay, newOverlay, changedQuestIds);
            QuestMapDebugLog.Info(_log,
                "QUESTMAP_M04_REFRESH " +
                $"reasons={string.Join(",", reasons)}; signaledQuests={string.Join(",", signaledQuestIds)}; " +
                $"changedQuests={changedQuestIds.Length}; topologyInvalidated={topologyReloaded}; " +
                $"batchedQuests={batchChangedQuests}; protectedHandovers={pendingHandoverIds.Count}; serverProfileSkipped={inRaid}; " +
                $"topologyReused={ReferenceEquals(originalTopology, _adapter.Topology)}; " +
                $"layoutReused={ReferenceEquals(originalLayout, _adapter.Layout)}; " +
                $"selection={string.Join(",", selectionConsequences)}; detail=native-owned");
        }

        CompleteRefresh();
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
                .Where(questId => QuestOverlayChangeDetector.HasQuestChanged(targetedOldOverlay, newOverlay, questId))
                .OrderBy(questId => questId, StringComparer.Ordinal)
                .ToArray();
            _tracking.ApplyNewQuestTransitions(targetedOldOverlay, newOverlay, changedQuestIds, topology.NodesById);
            _raid.PublishProgressChanges(
                topology,
                targetedOldOverlay,
                newOverlay,
                changedQuestIds,
                preferredObjectiveIds);
            _raid.RefreshTrackedListIfVisible();

            var uiStartedAt = Stopwatch.GetTimestamp();
            var selectionConsequences = _screens.RefreshQuests(newOverlay, changedQuestIds);
            var uiMilliseconds = ElapsedMilliseconds(uiStartedAt);
            _raid.RecordQuestPatch(modelMilliseconds, uiMilliseconds);

            if (_configuration.EnableDebugLogging.Value)
                LogTransitions(targetedOldOverlay, newOverlay, changedQuestIds);

            QuestMapDebugLog.Info(_log,
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

    private void CompleteRefresh()
    {
        _refreshCoroutine = null;
        if (!_disposed && _reasons.Count > 0)
            _refreshCoroutine = _coroutineOwner.StartCoroutine(CoalescedRefresh());
    }

    private void LogTransitions(
        QuestProfileOverlay oldOverlay,
        QuestProfileOverlay newOverlay,
        IEnumerable<string> changedQuestIds)
    {
        foreach (var questId in changedQuestIds)
        {
            var oldStatus = oldOverlay.QuestsById.TryGetValue(questId, out var oldState) && oldState.HasLiveQuest
                ? oldState.ExactStatus
                : "not-live";
            var newStatus = newOverlay.QuestsById.TryGetValue(questId, out var newState) && newState.HasLiveQuest
                ? newState.ExactStatus
                : "not-live";
            QuestMapDebugLog.Info(_log, $"QUESTMAP_M04_TRANSITION quest={questId}; old={oldStatus}; new={newStatus}");
        }
    }

    private static bool IsTargetedRaidRefresh(
        IReadOnlyCollection<string> reasons,
        IReadOnlyCollection<string> signaledQuestIds) =>
        signaledQuestIds.Count > 0
        && reasons.Count > 0
        && reasons.All(reason => reason is "raid-objective-event" or "raid-status-event");

    private static bool RequiresTopologyReload(
        IReadOnlyCollection<string> reasons,
        IReadOnlyCollection<string> signaledQuestIds,
        QuestGraphTopology topology)
    {
        if (reasons.Contains("repeatable-expired") || reasons.Contains("quest-details-replace")) return true;
        if (signaledQuestIds.Any(questId => !topology.NodesById.ContainsKey(questId))) return true;
        if (reasons.Any(reason => reason == "quest-book-removed" || reason == "quest-book-removed-range")
            && signaledQuestIds.Any(questId =>
                topology.NodesById.TryGetValue(questId, out var node) && node.ProfileGenerated)) return true;
        return false;
    }

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
}
