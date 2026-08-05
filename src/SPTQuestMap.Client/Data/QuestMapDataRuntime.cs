using System;
using System.Collections;
using System.Threading.Tasks;
using BepInEx.Logging;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestMapDataRuntime : IDisposable
{
    private readonly MonoBehaviour _coroutineOwner;
    private readonly ManualLogSource _log;
    private readonly QuestMapDataAdapter _adapter;
    private AbstractQuestControllerClass? _latestQuestController;
    private Coroutine? _loadCoroutine;
    private bool _disposed;

    public QuestMapDataRuntime(MonoBehaviour coroutineOwner, ManualLogSource log)
    {
        _coroutineOwner = coroutineOwner;
        _log = log;
        _adapter = new QuestMapDataAdapter(new ServerQuestTopologySource(), log);
    }

    public void ObserveQuestController(AbstractQuestControllerClass questController)
    {
        if (_disposed) return;
        _latestQuestController = questController;

        if (_adapter.Topology is not null)
        {
            RefreshOverlay(questController, true);
            return;
        }

        if (_loadCoroutine is null)
        {
            _loadCoroutine = _coroutineOwner.StartCoroutine(LoadAndOverlay(questController.Quests.Count));
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_loadCoroutine is not null) _coroutineOwner.StopCoroutine(_loadCoroutine);
        _loadCoroutine = null;
        _latestQuestController = null;
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
        RefreshOverlay(controller, false);
        _log.LogInfo(
            "QUESTMAP_M02_BOOK " +
            $"before={liveQuestCountBefore}; after={liveQuestCountAfter}; unchanged={liveQuestCountBefore == liveQuestCountAfter}; " +
            "loadAllCalled=False; templatesInjected=False");
        _log.LogInfo(
            "QUESTMAP_M02_STATE active=True; safelyDisabled=False; " +
            $"topologyVersion={_adapter.Topology?.Version}; layoutNodes={_adapter.Layout?.NodesById.Count ?? 0}");
    }

    private void RefreshOverlay(AbstractQuestControllerClass controller, bool repeated)
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
        }
        catch (Exception exception)
        {
            LogFailure(exception);
        }
    }

    private void LogFailure(Exception exception)
    {
        _log.LogError($"QUESTMAP_M02_ERROR {exception}");
        _log.LogWarning("QUESTMAP_M02_STATE active=False; safelyDisabled=True; reason=read-only data adapter failed; vanilla UI retained");
    }
}
