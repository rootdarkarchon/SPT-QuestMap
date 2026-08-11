using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal static class NativeQuestTableActions
{
    public const string LightkeeperTraderId = "638f541a29ffd1183d187f57";
    public const string BtrDriverTraderId = "656f0f98d80a697f855d34b1";

    private static readonly FieldInfo ObjectivesBlockField = AccessTools.Field(typeof(QuestView), "_objectivesBlock")
        ?? throw new MissingFieldException(typeof(QuestView).FullName, "_objectivesBlock");
    private static readonly FieldInfo ObjectivePrefabField = AccessTools.Field(typeof(QuestObjectivesView), "_objectivePrefab")
        ?? throw new MissingFieldException(typeof(QuestObjectivesView).FullName, "_objectivePrefab");
    private static readonly FieldInfo HandoverButtonField = AccessTools.Field(typeof(QuestObjectiveView), "_handoverButton")
        ?? throw new MissingFieldException(typeof(QuestObjectiveView).FullName, "_handoverButton");
    // EFT's QuestObjectivesView reuses one configured objective prefab for its
    // selected-quest view list. Avoid repeating a global Resources scan for
    // every QuestMap objective host.
    private static QuestObjectiveView? _cachedObjectivePrefab;

    public static void ResolveHandoverDeferred(
        RectTransform lifetimeRoot,
        bool selectedDetails,
        Func<bool> stillValid,
        Func<NativeQuestHandoverAction?> resolve,
        Action<NativeQuestHandoverAction?> completed,
        ManualLogSource log,
        string questId,
        string objectiveId)
    {
        var schedulerRoot = lifetimeRoot.root.gameObject;
        var scheduler = schedulerRoot.GetComponent<NativeQuestHandoverResolver>()
            ?? schedulerRoot.AddComponent<NativeQuestHandoverResolver>();
        scheduler.Enqueue(new NativeHandoverResolutionRequest(
            stillValid, resolve, completed, log, questId, objectiveId), selectedDetails);
    }

    public static NativeQuestHandoverAction? TryCreateHandover(
        RectTransform parent,
        QuestClass quest,
        string objectiveId,
        AbstractQuestControllerClass questController,
        InventoryController inventoryController)
    {
        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finish)) return null;
        var condition = finish.IEnumerable_0.FirstOrDefault(value => string.Equals(value.id, objectiveId, StringComparison.Ordinal));
        if (condition is not (ConditionHandoverItem or ConditionWeaponAssembly)) return null;
        var prefab = FindObjectivePrefab();
        if (prefab is null) return null;
        var host = UnityEngine.Object.Instantiate(prefab, parent, false);
        host.gameObject.name = $"QuestMapTableHandover-{quest.Id}-{objectiveId}";
        host.Show(quest, condition, questController, inventoryController, null, condition.IsNecessary);
        var nativeButton = HandoverButtonField.GetValue(host) as DefaultUIButton;
        if (nativeButton is not null && nativeButton.gameObject.activeSelf && nativeButton.Interactable)
            return new NativeQuestHandoverAction(host, quest);
        host.Close();
        UnityEngine.Object.Destroy(host.gameObject);
        return null;
    }

    public static async Task ReplaceAsync(
        RectTransform parent,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        QuestClass quest,
        string traderId,
        ManualLogSource log)
    {
        var source = FindQuestViewSource();
        var trader = session.Traders.FirstOrDefault(value => string.Equals(value.Id, traderId, StringComparison.Ordinal));
        if (source is null || trader is null) return;
        var clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
        clone.name = "QuestMapTableReplaceHost";
        clone.SetActive(false);
        var view = clone.GetComponent<QuestView>();
        if (view is null)
        {
            UnityEngine.Object.Destroy(clone);
            return;
        }
        try
        {
            view.Show(session, inventoryController, questController, quest, trader);
            view.gameObject.SetActive(false);
            await view.ShowChangeQuestConfirmation();
        }
        catch (Exception exception)
        {
            log.LogError($"QUESTMAP_M07_TABLE_REPLACE_ERROR quest={quest.Id}; {exception}");
            throw;
        }
        finally
        {
            view.Close();
            UnityEngine.Object.Destroy(clone);
        }
    }

    public static async Task AcceptAsync(
        RectTransform parent,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        QuestClass quest,
        string traderId,
        ManualLogSource log)
    {
        var source = FindQuestViewSource();
        var trader = session.Traders.FirstOrDefault(value => string.Equals(value.Id, traderId, StringComparison.Ordinal));
        if (source is null || trader is null) return;
        var clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
        clone.name = "QuestMapTableAcceptHost";
        clone.SetActive(false);
        var view = clone.GetComponent<QuestView>();
        if (view is null)
        {
            UnityEngine.Object.Destroy(clone);
            return;
        }
        try
        {
            view.Show(session, inventoryController, questController, quest, trader);
            view.gameObject.SetActive(false);
            await view.StartQuest(quest);
        }
        catch (Exception exception)
        {
            log.LogError($"QUESTMAP_M07_TABLE_ACCEPT_ERROR quest={quest.Id}; {exception}");
            throw;
        }
        finally
        {
            view.Close();
            UnityEngine.Object.Destroy(clone);
        }
    }

    public static async Task CompleteAsync(
        RectTransform parent,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        QuestClass quest,
        string traderId,
        ManualLogSource log)
    {
        var source = FindQuestViewSource();
        var trader = session.Traders.FirstOrDefault(value => string.Equals(value.Id, traderId, StringComparison.Ordinal));
        if (source is null || trader is null) return;
        var clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
        clone.name = "QuestMapTableCompleteHost";
        clone.SetActive(false);
        var view = clone.GetComponent<QuestView>();
        if (view is null)
        {
            UnityEngine.Object.Destroy(clone);
            return;
        }
        try
        {
            view.Show(session, inventoryController, questController, quest, trader);
            view.gameObject.SetActive(false);
            // Tarkov's native quest-level TURN IN transaction. Objective HAND
            // OVER remains the separate QuestObjectiveView path above.
            await view.FinishQuest(quest);
        }
        catch (Exception exception)
        {
            log.LogError($"QUESTMAP_M07_TABLE_COMPLETE_ERROR quest={quest.Id}; {exception}");
            throw;
        }
        finally
        {
            view.Close();
            UnityEngine.Object.Destroy(clone);
        }
    }

    public static bool IsRaidOnlyTrader(string traderId) =>
        string.Equals(traderId, LightkeeperTraderId, StringComparison.Ordinal)
        || string.Equals(traderId, BtrDriverTraderId, StringComparison.Ordinal);

    private static QuestView? FindQuestViewSource() =>
        Resources.FindObjectsOfTypeAll<QuestView>()
            .FirstOrDefault(view => view != null
                && !view.name.StartsWith("QuestMap", StringComparison.Ordinal)
                && ObjectivesBlockField.GetValue(view) is QuestObjectivesView);

    private static QuestObjectiveView? FindObjectivePrefab()
    {
        if (_cachedObjectivePrefab != null) return _cachedObjectivePrefab;
        var source = FindQuestViewSource();
        var objectives = source is null ? null : ObjectivesBlockField.GetValue(source) as QuestObjectivesView;
        _cachedObjectivePrefab = objectives is null ? null : ObjectivePrefabField.GetValue(objectives) as QuestObjectiveView;
        return _cachedObjectivePrefab;
    }
}

internal sealed class NativeHandoverResolutionRequest
{
    public NativeHandoverResolutionRequest(
        Func<bool> stillValid,
        Func<NativeQuestHandoverAction?> resolve,
        Action<NativeQuestHandoverAction?> completed,
        ManualLogSource log,
        string questId,
        string objectiveId)
    {
        StillValid = stillValid;
        Resolve = resolve;
        Completed = completed;
        Log = log;
        QuestId = questId;
        ObjectiveId = objectiveId;
    }

    public Func<bool> StillValid { get; }
    public Func<NativeQuestHandoverAction?> Resolve { get; }
    public Action<NativeQuestHandoverAction?> Completed { get; }
    public ManualLogSource Log { get; }
    public string QuestId { get; }
    public string ObjectiveId { get; }
}

internal sealed class NativeQuestHandoverResolver : MonoBehaviour
{
    private readonly Queue<NativeHandoverResolutionRequest> _selectedDetails = new();
    private readonly Queue<NativeHandoverResolutionRequest> _tableRows = new();

    public void Enqueue(NativeHandoverResolutionRequest request, bool selectedDetails) =>
        (selectedDetails ? _selectedDetails : _tableRows).Enqueue(request);

    private void Update()
    {
        if (TryResolveNext(_selectedDetails)) return;
        TryResolveNext(_tableRows);
    }

    private bool TryResolveNext(Queue<NativeHandoverResolutionRequest> requests)
    {
        while (requests.Count > 0)
        {
            var request = requests.Dequeue();
            if (!request.StillValid()) continue;

            var startedAt = Stopwatch.GetTimestamp();
            NativeQuestHandoverAction? action = null;
            try
            {
                action = request.Resolve();
            }
            catch (Exception exception)
            {
                request.Log.LogError(
                    $"QUESTMAP_M07_HANDOVER_RESOLVE_ERROR quest={request.QuestId}; objective={request.ObjectiveId}; {exception}");
            }

            if (!request.StillValid()) action?.Dispose();
            else request.Completed(action);
            QuestMapDebugLog.Info(request.Log,
                 "QUESTMAP_M07_HANDOVER_RESOLVE " +
                 $"quest={request.QuestId}; objective={request.ObjectiveId}; eligible={action is not null}; " +
                 $"elapsedMs={(Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency:F2}; " +
                 $"remaining={_selectedDetails.Count + _tableRows.Count}; perFrame=1");
            // QuestObjectiveView.Show performs condition-specific inventory
            // scans. Never resolve a second native objective in this frame.
            return true;
        }

        return false;
    }

    private void OnDestroy()
    {
        _selectedDetails.Clear();
        _tableRows.Clear();
    }
}

internal sealed class NativeQuestHandoverAction : IDisposable
{
    private static readonly Dictionary<string, HashSet<NativeQuestHandoverAction>> RetainedByQuest =
        new(StringComparer.Ordinal);
    private static Transform? _retainedRoot;

    private QuestObjectiveView? _host;
    private readonly QuestClass _quest;
    private bool _retained;

    public NativeQuestHandoverAction(QuestObjectiveView host, QuestClass quest)
    {
        _host = host;
        _quest = quest;
    }

    public async Task Execute()
    {
        if (_host is null) return;
        RetainForTransaction();
        try
        {
            await _host.method_2(_quest);
        }
        catch
        {
            ReleaseAfterTransaction();
            throw;
        }
    }

    public static void ReleaseRetained(string questId)
    {
        if (!RetainedByQuest.Remove(questId, out var actions)) return;
        foreach (var action in actions.ToArray()) action.ReleaseAfterTransaction();
        DestroyRetainedRootIfUnused();
    }

    public static void ReleaseAllRetained()
    {
        foreach (var questId in RetainedByQuest.Keys.ToArray()) ReleaseRetained(questId);
    }

    public void Dispose()
    {
        if (_retained) return;
        DisposeHost();
    }

    private void RetainForTransaction()
    {
        if (_retained) return;
        _retained = true;
        _retainedRoot ??= CreateRetainedRoot(_host!);
        _host!.transform.SetParent(_retainedRoot, false);
        if (!RetainedByQuest.TryGetValue(_quest.Id, out var actions))
        {
            actions = [];
            RetainedByQuest.Add(_quest.Id, actions);
        }
        actions.Add(this);
    }

    private void ReleaseAfterTransaction()
    {
        _retained = false;
        if (RetainedByQuest.TryGetValue(_quest.Id, out var actions))
        {
            actions.Remove(this);
            if (actions.Count == 0) RetainedByQuest.Remove(_quest.Id);
        }
        DisposeHost();
        DestroyRetainedRootIfUnused();
    }

    private void DisposeHost()
    {
        if (_host is null) return;
        _host.Close();
        UnityEngine.Object.Destroy(_host.gameObject);
        _host = null;
    }

    private static Transform CreateRetainedRoot(QuestObjectiveView host)
    {
        var root = new GameObject("QuestMapRetainedNativeActions", typeof(RectTransform)).transform;
        root.SetParent(host.transform.root, false);
        root.gameObject.SetActive(false);
        return root;
    }

    private static void DestroyRetainedRootIfUnused()
    {
        if (RetainedByQuest.Count != 0 || _retainedRoot == null) return;
        UnityEngine.Object.Destroy(_retainedRoot.gameObject);
        _retainedRoot = null;
    }
}
