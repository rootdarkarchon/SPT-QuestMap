using System;
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
        var source = FindQuestViewSource();
        var objectives = source is null ? null : ObjectivesBlockField.GetValue(source) as QuestObjectivesView;
        var prefab = objectives is null ? null : ObjectivePrefabField.GetValue(objectives) as QuestObjectiveView;
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
}

internal sealed class NativeQuestHandoverAction : IDisposable
{
    private QuestObjectiveView? _host;
    private readonly QuestClass _quest;

    public NativeQuestHandoverAction(QuestObjectiveView host, QuestClass quest)
    {
        _host = host;
        _quest = quest;
    }

    public Task Execute() => _host?.method_2(_quest) ?? Task.CompletedTask;

    public void Dispose()
    {
        if (_host is null) return;
        _host.Close();
        UnityEngine.Object.Destroy(_host.gameObject);
        _host = null;
    }
}
