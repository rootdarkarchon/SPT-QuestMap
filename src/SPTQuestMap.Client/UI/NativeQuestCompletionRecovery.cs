using System;
using System.Linq;
using BepInEx.Logging;
using EFT;
using EFT.Quests;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Client.UI;

/// <summary>
/// Repairs the narrow native state where every effective finish condition is
/// complete but EFT has left the quest in Started. The repair replays one
/// completed condition through EFT's own checker reset and condition controller;
/// it does not edit the profile or task-counter collections directly.
/// </summary>
internal static class NativeQuestCompletionRecovery
{
    public static bool CanRecover(
        AbstractQuestControllerClass questController,
        QuestGraphNode node,
        QuestClass quest)
    {
        if (quest.QuestStatus != EQuestStatus.Started
            || questController is not GClass4005 exactController
            || exactController.GClass4024_0 is null)
            return false;

        try
        {
            if (!AllNecessaryFinishConditionsRecorded(quest)
                || HasResettableIncompleteCounter(node, quest)) return false;
            return TryResolveReplay(node, quest, out _, out _, out _);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryPrepare(
        AbstractQuestControllerClass questController,
        QuestGraphNode node,
        QuestClass quest,
        ManualLogSource log)
    {
        if (quest.QuestStatus == EQuestStatus.AvailableForFinish) return true;
        if (!CanRecover(questController, node, quest)
            || questController is not GClass4005 exactController
            || !TryResolveReplay(node, quest, out var condition, out var checker, out var current))
            return false;

        try
        {
            checker.Reset();
            if (quest.IsConditionDone(condition))
            {
                log.LogWarning(
                    $"QUESTMAP_QUEST_COMPLETION_RECOVERY quest={quest.Id}; completed=False; reason=native reset retained completion");
                return false;
            }

            exactController.GClass4024_0.SetConditionCurrentValue(
                quest,
                EQuestStatus.AvailableForFinish,
                condition,
                current,
                true);
            if (quest.QuestStatus == EQuestStatus.Started)
                exactController.TryNotifyConditionChanged(quest);

            var recovered = quest.QuestStatus == EQuestStatus.AvailableForFinish;
            if (recovered)
            {
                log.LogInfo(
                    $"QUESTMAP_QUEST_COMPLETION_RECOVERY quest={quest.Id}; objective={condition.id}; completed=True");
            }
            else
            {
                log.LogWarning(
                    $"QUESTMAP_QUEST_COMPLETION_RECOVERY quest={quest.Id}; objective={condition.id}; completed=False; status={quest.QuestStatus}");
            }

            return recovered;
        }
        catch (Exception exception)
        {
            log.LogError($"QUESTMAP_QUEST_COMPLETION_RECOVERY quest={quest.Id}; completed=False; {exception}");
            return false;
        }
    }

    private static bool AllNecessaryFinishConditionsRecorded(QuestClass quest)
    {
        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finishConditions))
            return false;
        var necessary = finishConditions.IEnumerable_0.Where(condition => condition.IsNecessary).ToArray();
        return necessary.Length > 0 && necessary.All(quest.IsConditionDone);
    }

    private static bool HasResettableIncompleteCounter(QuestGraphNode node, QuestClass quest)
    {
        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finishConditions))
            return true;

        var conditions = finishConditions.IEnumerable_0
            .GroupBy(condition => condition.id.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var objective in node.Objectives.Where(value =>
                     value.OneSessionOnly && !value.DoNotResetIfCounterCompleted))
        {
            if (!conditions.TryGetValue(objective.Id, out var condition) || !condition.IsNecessary)
                continue;
            if (!quest.ProgressCheckers.TryGetValue(condition, out var checker)) return true;

            if (!QuestProgressRules.IsComplete(
                    quest.IsConditionDone(condition),
                    checker.CurrentValue,
                    objective.RequiredValue ?? condition.value,
                    objective.Compare,
                    counterStateAuthoritative: true))
                return true;
        }

        return false;
    }

    private static bool TryResolveReplay(
        QuestGraphNode node,
        QuestClass quest,
        out Condition condition,
        out ConditionProgressChecker checker,
        out double current)
    {
        condition = null!;
        checker = null!;
        current = 0d;
        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finishConditions))
            return false;

        var definitions = node.Objectives
            .GroupBy(objective => objective.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var candidate in finishConditions.IEnumerable_0.Reverse())
        {
            var id = candidate.id.ToString();
            if (!candidate.IsNecessary
                || !quest.IsConditionDone(candidate)
                || !quest.ProgressCheckers.TryGetValue(candidate, out var candidateChecker)
                || !definitions.TryGetValue(id, out var definition))
                continue;

            var candidateCurrent = candidateChecker.CurrentValue;
            if (!QuestProgressRules.IsComplete(
                    conditionRecorded: false,
                    candidateCurrent,
                    definition.RequiredValue ?? candidate.value,
                    definition.Compare))
                continue;

            condition = candidate;
            checker = candidateChecker;
            current = candidateCurrent;
            return true;
        }

        return false;
    }
}
