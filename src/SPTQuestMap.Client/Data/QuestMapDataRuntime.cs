using System;
using System.Collections;
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
    private readonly QuestTrackingService _tracking;
    private readonly QuestScreenCoordinator _screens;
    private readonly RaidQuestRuntime _raid;
    private readonly QuestRefreshCoordinator _refresh;
    private Coroutine? _loadCoroutine;
    private bool _loadRequestedForScreen;
    private int? _liveQuestCountBeforeLoad;
    private bool _disposed;

    public QuestMapDataRuntime(
        MonoBehaviour coroutineOwner,
        ManualLogSource log,
        QuestMapClientConfiguration configuration)
    {
        _coroutineOwner = coroutineOwner;
        _log = log;
        _adapter = new QuestMapDataAdapter(new ServerQuestTopologySource(), log);
        var assetCache = coroutineOwner.gameObject.AddComponent<QuestAssetSpriteCache>();
        assetCache.Bind(log);
        _tracking = new QuestTrackingService(configuration, log);
        _screens = new QuestScreenCoordinator(
            coroutineOwner,
            log,
            configuration,
            _adapter,
            assetCache,
            _tracking,
            ObserveQuestController,
            SetInventoryController,
            RequestRefresh,
            ScheduleActionReconciliation);
        _raid = new RaidQuestRuntime(
            coroutineOwner,
            log,
            configuration,
            _adapter,
            assetCache,
            _tracking,
            controller => ObserveQuestController(controller, false),
            ClearObservedQuestController,
            RequestRaidRefresh,
            ClearRaidRefreshSignals);
        _refresh = new QuestRefreshCoordinator(
            coroutineOwner,
            log,
            configuration,
            _adapter,
            _tracking,
            _screens,
            _raid);
    }

    public void UpdateRaidMonitor() => _raid.Update();

    public void WarmTopology()
    {
        if (_disposed || _adapter.Topology is not null || _loadCoroutine is not null) return;

        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M02_PRELOAD started=True; trigger=main-menu; blocking=False");
        _loadCoroutine = _coroutineOwner.StartCoroutine(LoadAndOverlay(backgroundWarmup: true));
    }

    public void ObserveQuestController(AbstractQuestControllerClass questController) =>
        ObserveQuestController(questController, !_raid.Active);

    public void ShowTraderScreen(
        QuestsScreen screen,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        TraderClass trader) =>
        _screens.ShowTraderScreen(screen, session, inventoryController, questController, trader);

    public void CloseTraderScreen(QuestsScreen screen) => _screens.CloseTraderScreen(screen);

    public void ShowGlobalTasksScreen(TasksScreen screen, object[] arguments) =>
        _screens.ShowGlobalTasksScreen(screen, arguments);

    public void CloseGlobalTasksScreen(TasksScreen screen) => _screens.CloseGlobalTasksScreen(screen);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_loadCoroutine is not null) _coroutineOwner.StopCoroutine(_loadCoroutine);
        _loadCoroutine = null;
        _refresh.Dispose();
        _raid.Dispose();
        _tracking.Dispose();
        _screens.Dispose();
    }

    private void ObserveQuestController(
        AbstractQuestControllerClass questController,
        bool enableReactiveMonitor)
    {
        if (_disposed) return;
        _refresh.ObserveController(questController, enableReactiveMonitor);

        if (_adapter.Topology is not null)
        {
            if (!_refresh.RefreshOverlay(questController, true)) return;
            _screens.TryMountPending(questController);
            return;
        }

        _loadRequestedForScreen = true;
        _liveQuestCountBeforeLoad ??= questController.Quests.Count;
        if (_loadCoroutine is null)
            _loadCoroutine = _coroutineOwner.StartCoroutine(LoadAndOverlay(backgroundWarmup: false));
    }

    private IEnumerator LoadAndOverlay(bool backgroundWarmup)
    {
        Task loadTask;
        try
        {
            loadTask = _adapter.LoadTopologyAsync();
        }
        catch (Exception exception)
        {
            _loadCoroutine = null;
            HandleLoadFailure(exception, backgroundWarmup);
            yield break;
        }

        while (!loadTask.IsCompleted) yield return null;
        _loadCoroutine = null;
        if (_disposed) yield break;
        if (loadTask.IsFaulted)
        {
            HandleLoadFailure(
                loadTask.Exception?.GetBaseException()
                    ?? new InvalidOperationException("Unknown topology load failure."),
                backgroundWarmup);
            yield break;
        }

        if (backgroundWarmup)
        {
            QuestMapDebugLog.Info(_log,
                "QUESTMAP_M02_PRELOAD completed=True; trigger=main-menu; blocking=False; " +
                $"screenWaiting={_loadRequestedForScreen}; topologyVersion={_adapter.Topology?.Version}; " +
                $"layoutNodes={_adapter.Layout?.NodesById.Count ?? 0}");
        }

        var controller = _refresh.CurrentController;
        if (controller is null)
        {
            if (_loadRequestedForScreen)
                _log.LogWarning("QUESTMAP_M02_STATE active=False; safelyDisabled=True; reason=quest controller disappeared during topology load");
            ResetLoadRequest();
            yield break;
        }

        var liveQuestCountBefore = _liveQuestCountBeforeLoad ?? controller.Quests.Count;
        var liveQuestCountAfter = controller.Quests.Count;
        if (!_refresh.RefreshOverlay(controller, false))
        {
            ResetLoadRequest();
            yield break;
        }
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M02_BOOK " +
            $"before={liveQuestCountBefore}; after={liveQuestCountAfter}; unchanged={liveQuestCountBefore == liveQuestCountAfter}; " +
            "loadAllCalled=False; templatesInjected=False");
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M02_STATE active=True; safelyDisabled=False; " +
            $"topologyVersion={_adapter.Topology?.Version}; layoutNodes={_adapter.Layout?.NodesById.Count ?? 0}");
        _screens.TryMountPending(controller);
        ResetLoadRequest();
    }

    private void HandleLoadFailure(Exception exception, bool backgroundWarmup)
    {
        if (backgroundWarmup && !_loadRequestedForScreen)
        {
            _log.LogWarning(
                $"QUESTMAP_M02_PRELOAD completed=False; trigger=main-menu; blocking=False; " +
                $"retryOnScreen=True; error={exception.GetType().Name}: {exception.Message}");
            ResetLoadRequest();
            return;
        }

        LogFailure(exception);
        ResetLoadRequest();
    }

    private void ResetLoadRequest()
    {
        _loadRequestedForScreen = false;
        _liveQuestCountBeforeLoad = null;
    }

    private void LogFailure(Exception exception)
    {
        _log.LogError($"QUESTMAP_M02_ERROR {exception}");
        _log.LogWarning("QUESTMAP_M02_STATE active=False; safelyDisabled=True; reason=read-only data adapter failed; vanilla UI retained");
        _screens.FailPendingScreens("read-only data adapter failed", exception);
    }

    private void SetInventoryController(InventoryController? inventoryController) =>
        _refresh.SetInventoryController(inventoryController);

    private void RequestRefresh(string reason, string? questId) =>
        _refresh.Request(reason, questId);

    private void RequestRaidRefresh(string reason, string? questId, string? preferredObjectiveId) =>
        _refresh.Request(reason, questId, preferredObjectiveId);

    private void ScheduleActionReconciliation(string questId, QuestDetailsActionKind action) =>
        _refresh.ScheduleActionReconciliation(questId, action);

    private void ClearObservedQuestController() => _refresh.ClearObservedController();

    private void ClearRaidRefreshSignals() => _refresh.ClearRaidSignals();
}
