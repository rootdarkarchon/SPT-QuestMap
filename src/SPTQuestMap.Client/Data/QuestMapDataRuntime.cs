using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.UI;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.UI;
using SPTQuestMap.Core.Models;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestMapDataRuntime : IDisposable
{
    private readonly MonoBehaviour _coroutineOwner;
    private readonly ManualLogSource _log;
    private readonly QuestMapDataAdapter _adapter;
    private readonly QuestMapClientConfiguration _configuration;
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
    private bool _disposed;

    public QuestMapDataRuntime(
        MonoBehaviour coroutineOwner,
        ManualLogSource log,
        QuestMapClientConfiguration configuration)
    {
        _coroutineOwner = coroutineOwner;
        _log = log;
        _configuration = configuration;
        _adapter = new QuestMapDataAdapter(new ServerQuestTopologySource(), log);
    }

    public void ObserveQuestController(AbstractQuestControllerClass questController)
    {
        if (_disposed) return;
        if (!ReferenceEquals(_latestQuestController, questController))
        {
            _reactiveMonitor?.Dispose();
            _reactiveMonitor = new ReactiveQuestMonitor(questController, RequestReactiveRefresh);
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
        CloseGlobalTasksScreen(screen);
        var questController = arguments.OfType<AbstractQuestControllerClass>().SingleOrDefault();
        var inventoryController = arguments.OfType<InventoryController>().SingleOrDefault();
        var session = arguments.OfType<ISession>().SingleOrDefault();
        if (questController is null || inventoryController is null || session is null)
        {
            _log.LogWarning($"QUESTMAP_M06_STATE screen={screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=required Show argument missing; vanillaRestored=True");
            return;
        }

        _pendingGlobalScreen = new PendingGlobalScreen(screen, inventoryController, questController, session);
        ObserveQuestController(questController);
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
            _globalControllers.Remove(screen);
            controller.Dispose();
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
        _reactiveReasons.Clear();
        _reactiveQuestIds.Clear();
        if (controller is null || oldOverlay is null || originalTopology is null || originalLayout is null)
        {
            CompleteReactiveRefresh();
            yield break;
        }

        var topologyReloaded = RequiresTopologyReload(reasons, signaledQuestIds, originalTopology);
        if (topologyReloaded)
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
            newOverlay = _adapter.RefreshOverlay(controller.Quests, controller.Profile, false);
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
        var topology = _adapter.Topology!;
        var layout = _adapter.Layout!;
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
            _configuration.EnableDebugLogging.Value);
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
            _configuration.EnableDebugLogging.Value);
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
