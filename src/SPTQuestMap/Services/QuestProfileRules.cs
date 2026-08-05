using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace SPTQuestMap.Services;

internal static class QuestProfileRules
{
    internal static QuestBlockerDto[] GetBlockers(
        QuestNodeDto quest,
        int level,
        Dictionary<MongoId, TraderInfo> traders,
        Dictionary<string, QuestStatus> profileQuests,
        IReadOnlyCollection<QuestEdgeDto> edges,
        TraderAvailabilityEvaluator traderAvailability
    )
    {
        var blockers = new List<QuestBlockerDto>();
        if (MongoId.IsValidMongoId(quest.TraderId)
            && !traderAvailability.IsAvailable(new MongoId(quest.TraderId)))
        {
            blockers.Add(new QuestBlockerDto("TraderUnavailable", quest.TraderId, null, null, []));
        }

        // Classification and the detail pane intentionally use the same inherited gates.
        foreach (var requirement in quest.EffectiveRequirements)
        {
            if (requirement.Kind == "Level" && !QuestGraphRules.Compare(level, requirement.Value, requirement.Compare))
            {
                blockers.Add(new QuestBlockerDto("Level", null, requirement.Compare, requirement.Value, []));
                continue;
            }

            if (requirement.TraderId is null || !MongoId.IsValidMongoId(requirement.TraderId)) continue;

            var traderId = new MongoId(requirement.TraderId);
            if (!traderAvailability.IsAvailable(traderId))
            {
                blockers.Add(new QuestBlockerDto("TraderUnavailable", requirement.TraderId, requirement.Compare, requirement.Value, []));
                continue;
            }

            var trader = traders[traderId];
            var actual = requirement.Kind == "TraderLoyalty" ? trader.LoyaltyLevel : trader.Standing;
            if (!actual.HasValue || !QuestGraphRules.Compare(actual.Value, requirement.Value, requirement.Compare))
            {
                blockers.Add(new QuestBlockerDto(requirement.Kind, requirement.TraderId, requirement.Compare, requirement.Value, []));
            }
        }

        // Keep output in presentation priority: availability, level, trader requirements, prerequisites.
        foreach (var edge in edges.Where(edge => edge.TargetId == quest.Id))
        {
            if (!profileQuests.TryGetValue(edge.SourceId, out var prerequisite)
                || !edge.RequiredStatuses.Contains(prerequisite.Status.ToString(), StringComparer.Ordinal))
            {
                blockers.Add(new QuestBlockerDto("Prerequisite", edge.SourceId, null, null, edge.RequiredStatuses));
            }
        }

        return blockers.ToArray();
    }

    internal static string Classify(
        QuestNodeDto quest,
        QuestStatusEnum? status,
        bool authoritative,
        QuestExclusionDto? exclusion,
        QuestBlockerDto[] blockers
    )
    {
        if (status == QuestStatusEnum.Success) return "Completed";
        if (exclusion is not null) return "Excluded";
        if (status == QuestStatusEnum.AvailableForFinish) return "ReadyToFinish";
        if (status == QuestStatusEnum.Started) return "InProgress";
        if (status == QuestStatusEnum.FailRestartable) return "RestartableFailure";
        if (status == QuestStatusEnum.Expired) return "Expired";
        if (status is QuestStatusEnum.Fail or QuestStatusEnum.MarkedAsFailed) return quest.Restartable ? "RestartableFailure" : "Failed";
        if (blockers.Any(blocker => blocker.Kind == "TraderUnavailable")) return "TraderUnavailable";
        if (status == QuestStatusEnum.AvailableAfter) return "Pending";
        if (blockers.Any(blocker => blocker.Kind == "Level")) return "LevelGated";
        if (blockers.Any(blocker => blocker.Kind is "TraderLoyalty" or "TraderStanding")) return "TraderGated";
        if (blockers.Any(blocker => blocker.Kind == "Prerequisite")) return "PrerequisiteGated";
        if (status == QuestStatusEnum.AvailableForStart || authoritative) return "Available";
        return "Locked";
    }

    internal static bool IsEffectivelyVisible(bool authoritativelyVisible, QuestBlockerDto[] blockers) =>
        authoritativelyVisible && !blockers.Any(blocker => blocker.Kind == "TraderUnavailable");

    internal static bool ShouldShowInDefaultGraph(QuestStateDto state) => state.DisplayState != "TraderUnavailable";

    internal static bool ObjectiveIsComplete(bool conditionRecorded, double? current, double? required, string? compare) =>
        conditionRecorded || (current.HasValue && required.HasValue && QuestGraphRules.Compare(current.Value, required.Value, compare ?? ">="));

    internal static double? CapObjectiveCurrent(double? current, double? required) =>
        current.HasValue && required.HasValue && current.Value > required.Value ? required : current;

    internal static double? CalculateObjectiveProgress(IReadOnlyCollection<ObjectiveProgressDto> objectives)
    {
        if (objectives.Count == 0) return null;

        var completedShare = objectives.Sum(objective =>
        {
            if (objective.Complete) return 1d;
            if (objective.Current.HasValue && objective.Required is > 0)
            {
                return Math.Clamp(objective.Current.Value / objective.Required.Value, 0d, 1d);
            }

            return 0d;
        });
        return Math.Round(completedShare / objectives.Count * 100d, 1, MidpointRounding.AwayFromZero);
    }
}
