using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.UI;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.UI;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestMapDataRuntime : IDisposable
{
    private readonly MonoBehaviour _coroutineOwner;
    private readonly ManualLogSource _log;
    private readonly QuestMapDataAdapter _adapter;
    private readonly QuestMapClientConfiguration _configuration;
    private readonly Dictionary<QuestsScreen, TraderGraphScreenController> _traderControllers = new();
    private AbstractQuestControllerClass? _latestQuestController;
    private PendingTraderScreen? _pendingTraderScreen;
    private Coroutine? _loadCoroutine;
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
        _latestQuestController = questController;

        if (_adapter.Topology is not null)
        {
            if (!RefreshOverlay(questController, true)) return;
            TryMountPendingTraderScreen(questController);
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
    }

    public void Dispose()
    {
        _disposed = true;
        if (_loadCoroutine is not null) _coroutineOwner.StopCoroutine(_loadCoroutine);
        _loadCoroutine = null;
        _latestQuestController = null;
        _pendingTraderScreen = null;
        foreach (var controller in _traderControllers.Values) controller.Dispose();
        _traderControllers.Clear();
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

    private void LogFailure(Exception exception)
    {
        _log.LogError($"QUESTMAP_M02_ERROR {exception}");
        _log.LogWarning("QUESTMAP_M02_STATE active=False; safelyDisabled=True; reason=read-only data adapter failed; vanilla UI retained");
        FailPendingTraderScreen("read-only data adapter failed", exception);
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
            _log);
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
}
