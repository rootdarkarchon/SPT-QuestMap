using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using SPTQuestMap.Client.UI;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestActionReconciler : IDisposable
{
    private readonly MonoBehaviour _coroutineOwner;
    private readonly ManualLogSource _log;
    private readonly QuestMapDataAdapter _adapter;
    private readonly Func<AbstractQuestControllerClass?> _currentController;
    private readonly Action<string, string?> _requestRefresh;
    private readonly Dictionary<string, Coroutine> _pending = new(StringComparer.Ordinal);
    private bool _disposed;

    public QuestActionReconciler(
        MonoBehaviour coroutineOwner,
        ManualLogSource log,
        QuestMapDataAdapter adapter,
        Func<AbstractQuestControllerClass?> currentController,
        Action<string, string?> requestRefresh)
    {
        _coroutineOwner = coroutineOwner;
        _log = log;
        _adapter = adapter;
        _currentController = currentController;
        _requestRefresh = requestRefresh;
    }

    public bool HasPending => _pending.Count > 0;

    public void Schedule(string questId, QuestDetailsActionKind action)
    {
        if (_disposed || string.IsNullOrWhiteSpace(questId)) return;
        if (_pending.Remove(questId, out var pending)) _coroutineOwner.StopCoroutine(pending);
        _pending[questId] = _coroutineOwner.StartCoroutine(ReconcileAfterNativeTransaction(questId, action));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var coroutine in _pending.Values) _coroutineOwner.StopCoroutine(coroutine);
        _pending.Clear();
    }

    private IEnumerator ReconcileAfterNativeTransaction(string questId, QuestDetailsActionKind action)
    {
        // Native replacement/handover methods can complete when their popup is
        // shown rather than when the confirmed transaction has propagated into
        // the quest book. Wait for an observable controller-state transition.
        var interactive = action is QuestDetailsActionKind.Replace or QuestDetailsActionKind.Handover;
        var startedAt = Time.realtimeSinceStartup;
        var deadline = startedAt + (interactive ? 120f : 10f);
        var settled = QuestMutationObserved(questId, action);
        while (!_disposed && !settled && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForSecondsRealtime(0.1f);
            settled = QuestMutationObserved(questId, action);
        }
        _pending.Remove(questId);
        if (_disposed) yield break;
        if (!settled)
        {
            _log.LogWarning(
                $"QUESTMAP_M07_ACTION_RECONCILE quest={questId}; action={action}; settled=False; " +
                $"waitMs={(Time.realtimeSinceStartup - startedAt) * 1000f:F0}; refreshSkipped=True");
            yield break;
        }

        QuestMapDebugLog.Info(_log,
            $"QUESTMAP_M07_ACTION_RECONCILE quest={questId}; action={action}; settled=True; " +
            $"waitMs={(Time.realtimeSinceStartup - startedAt) * 1000f:F0}");

        // Give the rest of the native observers one frame after the quest-book
        // mutation before taking the authoritative snapshot.
        yield return null;
        _requestRefresh(
            action == QuestDetailsActionKind.Replace
                ? "quest-details-replace"
                : "quest-details-action-settled",
            questId);
    }

    private bool QuestMutationObserved(string questId, QuestDetailsActionKind action)
    {
        var controller = _currentController();
        var overlay = _adapter.Overlay;
        if (controller is null || overlay is null) return false;

        overlay.QuestsById.TryGetValue(questId, out var prior);
        var currentQuests = controller.Quests.ToArray();
        var liveQuest = currentQuests.LastOrDefault(quest =>
            string.Equals(quest.Id, questId, StringComparison.Ordinal));
        if (action == QuestDetailsActionKind.Replace)
        {
            var priorLiveIds = overlay.QuestsById
                .Where(pair => pair.Value.HasLiveQuest)
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
            return liveQuest is null
                && currentQuests.Any(quest => !priorLiveIds.Contains(quest.Id)
                    && _adapter.Topology?.NodesById.ContainsKey(quest.Id) != true);
        }
        if (liveQuest is null) return prior?.HasLiveQuest == true;

        var current = EftLiveSnapshotAdapter.CaptureQuest(liveQuest);
        if (prior is null || !prior.HasLiveQuest) return true;
        return !string.Equals(prior.ExactStatus, current.ExactStatus, StringComparison.Ordinal)
               || prior.Visible != current.Visible
               || prior.ExpirationTime != current.ExpirationTime
               || prior.HandoverReady != current.HandoverReady
               || !prior.Objectives.SequenceEqual(current.Objectives);
    }
}
