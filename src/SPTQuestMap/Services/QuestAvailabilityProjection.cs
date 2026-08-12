using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace SPTQuestMap.Services;

/// <summary>
/// Projects the visibility/status portion of SPT 4.0.13 QuestHelper.GetClientQuests without
/// materializing its deep-cloned client quest payload. Keep this decision order source-pinned.
/// </summary>
internal static class QuestAvailabilityProjection
{
    internal static Dictionary<string, QuestStatusEnum?> Build(
        IEnumerable<Quest> databaseQuests,
        IReadOnlyList<QuestStatus> orderedProfileQuests,
        IReadOnlyDictionary<string, QuestStatus> profileQuests,
        string playerSide,
        double playerLevel,
        IReadOnlyCollection<MongoId> profileTraderIds,
        Func<string, MongoId, bool> questIsForOtherSide,
        Func<MongoId, bool> showEventQuestToPlayer,
        Func<double, QuestCondition, bool> levelRequirementPasses,
        Func<QuestCondition, bool> loyaltyRequirementPasses,
        Func<QuestCondition, bool> standingRequirementPasses)
    {
        var result = new Dictionary<string, QuestStatusEnum?>(StringComparer.Ordinal);
        var traderIds = profileTraderIds.ToHashSet();
        var orderedProfileById = BuildOrderedProfileLookup(orderedProfileQuests);

        foreach (var quest in databaseQuests)
        {
            var questId = quest.Id.ToString();

            // SPT always returns accepted quests, regardless of the normal availability gates.
            if (profileQuests.TryGetValue(questId, out var profileQuest))
            {
                result.TryAdd(questId, profileQuest.Status);
                continue;
            }

            if (questIsForOtherSide(playerSide, quest.Id) || !showEventQuestToPlayer(quest.Id)) continue;

            var startConditions = quest.Conditions?.AvailableForStart ?? [];
            if (startConditions.Any(condition =>
                    condition.ConditionType == "Level" && !levelRequirementPasses(playerLevel, condition)))
            {
                continue;
            }

            if (!traderIds.Contains(quest.TraderId)) continue;

            var questRequirements = startConditions.Where(condition => condition.ConditionType == "Quest").ToArray();
            var loyaltyRequirements = startConditions.Where(condition => condition.ConditionType == "TraderLoyalty").ToArray();
            var standingRequirements = startConditions.Where(condition => condition.ConditionType == "TraderStanding").ToArray();

            if (!QuestRequirementsPass(questRequirements, orderedProfileById)) continue;
            if (loyaltyRequirements.Any(condition => !loyaltyRequirementPasses(condition))) continue;
            if (standingRequirements.Any(condition => !standingRequirementPasses(condition))) continue;

            result.TryAdd(questId, QuestStatusEnum.AvailableForStart);
        }

        return result;
    }

    private static bool QuestRequirementsPass(
        IEnumerable<QuestCondition> requirements,
        IReadOnlyDictionary<string, OrderedProfileQuest> profileQuests)
    {
        foreach (var requirement in requirements)
        {
            var prerequisite = QuestTemplateMapper.GetTargets(requirement)
                .Select(target => profileQuests.GetValueOrDefault(target))
                .Where(candidate => candidate is not null)
                .OrderBy(candidate => candidate!.Index)
                .FirstOrDefault();

            if (prerequisite is null || requirement.Status?.Contains(prerequisite.Quest.Status) != true)
            {
                return false;
            }

            // SPT 4.0.13 reports an availableAfter delay here but still exposes the quest.
        }

        return true;
    }

    private static Dictionary<string, OrderedProfileQuest> BuildOrderedProfileLookup(
        IReadOnlyList<QuestStatus> profileQuests)
    {
        var result = new Dictionary<string, OrderedProfileQuest>(StringComparer.Ordinal);
        for (var index = 0; index < profileQuests.Count; index++)
        {
            var quest = profileQuests[index];
            result.TryAdd(quest.QId.ToString(), new OrderedProfileQuest(quest, index));
        }

        return result;
    }

    private sealed record OrderedProfileQuest(QuestStatus Quest, int Index);
}
