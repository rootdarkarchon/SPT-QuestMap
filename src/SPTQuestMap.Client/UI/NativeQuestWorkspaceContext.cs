using System;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Core.Models;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

/// <summary>
/// Owns the exact-version EFT session binding and native quest transaction
/// rules shared by QuestMap's global and trader workspaces.
/// </summary>
internal sealed class NativeQuestWorkspaceContext
{
    private readonly QuestMapClientConfiguration _configuration;

    public NativeQuestWorkspaceContext(
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        ManualLogSource log,
        QuestMapClientConfiguration configuration)
    {
        Session = session;
        InventoryController = inventoryController;
        QuestController = questController;
        Log = log;
        _configuration = configuration;
        DebugLogging = configuration.EnableDebugLogging.Value;
    }

    public ISession Session { get; }

    public InventoryController InventoryController { get; private set; }

    public AbstractQuestControllerClass QuestController { get; }

    public ManualLogSource Log { get; }

    public bool DebugLogging { get; }

    public bool ShowHiddenRewards() => _configuration.ShowHiddenQuestRewards.Value;

    public bool DefaultDetailsToSummary() => _configuration.DefaultQuestDetailsToSummary.Value;

    public bool TaskSkippingEnabled() => _configuration.EnableTaskSkipping.Value;

    public KeyboardShortcut TaskSkipModifier() => _configuration.TaskSkipModifier.Value;

    public bool MutationsAllowed() => !InRaidQuestContext.TryCapture(out _);

    public void RebindInventoryController(InventoryController inventoryController) =>
        InventoryController = inventoryController;

    public QuestClass? FindLiveQuest(string questId) => QuestController.Quests.LastOrDefault(
        quest => string.Equals(quest.Id, questId, StringComparison.Ordinal));

    public NativeQuestHandoverAction? TryCreateHandover(
        RectTransform parent,
        string questId,
        string objectiveId)
    {
        if (!MutationsAllowed()) return null;
        var quest = FindLiveQuest(questId);
        if (quest?.QuestStatus != EQuestStatus.Started) return null;
        return NativeQuestTableActions.TryCreateHandover(
            parent,
            quest,
            objectiveId,
            QuestController,
            InventoryController);
    }

    public bool CanAccept(QuestGraphTopology topology, string questId)
    {
        if (!MutationsAllowed() || !topology.NodesById.TryGetValue(questId, out var node)
            || NativeQuestTableActions.IsRaidOnlyTrader(node.TraderId)) return false;
        return FindLiveQuest(questId)?.QuestStatus == EQuestStatus.AvailableForStart;
    }

    public async Task AcceptAsync(RectTransform parent, QuestGraphTopology topology, string questId)
    {
        if (!CanAccept(topology, questId)) return;
        await NativeQuestTableActions.AcceptAsync(
            parent,
            Session,
            InventoryController,
            QuestController,
            FindLiveQuest(questId)!,
            topology.NodesById[questId].TraderId,
            Log);
    }

    public bool CanComplete(QuestGraphTopology topology, string questId)
    {
        if (!MutationsAllowed() || !topology.NodesById.TryGetValue(questId, out var node)
            || NativeQuestTableActions.IsRaidOnlyTrader(node.TraderId)) return false;
        return FindLiveQuest(questId)?.QuestStatus == EQuestStatus.AvailableForFinish;
    }

    public async Task CompleteAsync(RectTransform parent, QuestGraphTopology topology, string questId)
    {
        if (!CanComplete(topology, questId)) return;
        await NativeQuestTableActions.CompleteAsync(
            parent,
            Session,
            InventoryController,
            QuestController,
            FindLiveQuest(questId)!,
            topology.NodesById[questId].TraderId,
            Log);
    }

    public bool CanReplace(QuestGraphTopology topology, string questId)
    {
        if (!MutationsAllowed() || !topology.NodesById.TryGetValue(questId, out var node)
            || node.RepeatableKind is not ("Daily" or "Weekly")) return false;
        return FindLiveQuest(questId)?.IsChangeAllowed == true;
    }

    public async Task ReplaceAsync(RectTransform parent, QuestGraphTopology topology, string questId)
    {
        if (!CanReplace(topology, questId)) return;
        await NativeQuestTableActions.ReplaceAsync(
            parent,
            Session,
            InventoryController,
            QuestController,
            FindLiveQuest(questId)!,
            topology.NodesById[questId].TraderId,
            Log);
    }
}
