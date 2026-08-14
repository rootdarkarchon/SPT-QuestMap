using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using CoreDisplayState = SPTQuestMap.Core.Models.QuestDisplayStateKind;
using CoreGraphRules = SPTQuestMap.Core.Rules.QuestGraphRules;

namespace SPTQuestMap.Services;

internal static class QuestProfileRules
{
    internal static QuestBlockerDto[] GetBlockers(
        QuestNodeDto quest,
        int level,
        int prestigeLevel,
        Dictionary<MongoId, TraderInfo> traders,
        Dictionary<string, QuestStatus> profileQuests,
        IReadOnlyCollection<QuestEdgeDto> incomingEdges,
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
            if (requirement.Kind == "PrestigeLevel"
                && !QuestGraphRules.Compare(prestigeLevel, requirement.Value, requirement.Compare))
            {
                blockers.Add(new QuestBlockerDto("PrestigeLevel", null, requirement.Compare, requirement.Value, []));
                continue;
            }

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
        foreach (var edge in incomingEdges)
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
        var coreState = CoreGraphRules.ClassifyDisplayState(status?.ToString(), status.HasValue, quest.Restartable);
        if (coreState == CoreDisplayState.Success) return "Completed";
        if (exclusion is not null) return "Excluded";
        if (coreState == CoreDisplayState.AvailableForFinish) return "ReadyToFinish";
        if (coreState == CoreDisplayState.Started) return "InProgress";
        if (coreState == CoreDisplayState.FailRestartable) return "RestartableFailure";
        if (coreState == CoreDisplayState.Expired) return "Expired";
        if (coreState is CoreDisplayState.Fail or CoreDisplayState.MarkedAsFailed) return "Failed";
        if (blockers.Any(blocker => blocker.Kind == "TraderUnavailable")) return "TraderUnavailable";
        if (coreState == CoreDisplayState.AvailableAfter) return "Pending";
        if (blockers.Any(blocker => blocker.Kind == "PrestigeLevel")) return "PrestigeGated";
        if (blockers.Any(blocker => blocker.Kind == "Level")) return "LevelGated";
        if (blockers.Any(blocker => blocker.Kind is "TraderLoyalty" or "TraderStanding")) return "TraderGated";
        if (blockers.Any(blocker => blocker.Kind == "Prerequisite")) return "PrerequisiteGated";
        if (status == QuestStatusEnum.Locked) return "Locked";
        if (coreState == CoreDisplayState.AvailableForStart || authoritative) return "Available";
        return "Locked";
    }

    internal static bool IsEffectivelyVisible(bool authoritativelyVisible, QuestBlockerDto[] blockers) =>
        authoritativelyVisible && !blockers.Any(blocker => blocker.Kind == "TraderUnavailable");

    internal static bool ShouldShowInDefaultGraph(QuestStateDto state) => state.DisplayState != "TraderUnavailable";

}
