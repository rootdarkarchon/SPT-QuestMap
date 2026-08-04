using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

internal sealed class QuestProfileStateBuilder(
    DatabaseService databaseService,
    SaveServer saveServer,
    QuestHelper questHelper,
    SeasonalEventService seasonalEventService
)
{
    private readonly object _availabilityLock = new();

    internal ProfileStateDto? Build(string rawProfileId, QuestTopologyDto topology)
    {
        if (!MongoId.IsValidMongoId(rawProfileId)) return null;

        var profileId = new MongoId(rawProfileId);
        if (!saveServer.ProfileExists(profileId) || saveServer.IsProfileInvalidOrUnloadable(profileId)) return null;

        var pmc = saveServer.GetProfile(profileId).CharacterData?.PmcData;
        if (pmc?.Info is null) return null;

        var profileQuests = (pmc.Quests ?? []).ToDictionary(quest => quest.QId.ToString(), StringComparer.Ordinal);
        var profileQuestStatuses = profileQuests.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Status.ToString(),
            StringComparer.Ordinal
        );
        var traderAvailability = new TraderAvailabilityEvaluator(pmc.TradersInfo, profileQuestStatuses);
        Dictionary<string, QuestStatusEnum?> authoritative;
        lock (_availabilityLock)
        {
            authoritative = questHelper
                .GetClientQuests(profileId)
                .ToDictionary(quest => quest.Id.ToString(), quest => quest.SptStatus, StringComparer.Ordinal);
        }

        var noneExcluded = QuestGraphRules.BuildNoneEventExclusionSet(topology);
        var applicable = topology.Quests
            .Where(quest => IsApplicable(quest, pmc.Info.Side ?? string.Empty, noneExcluded))
            .Select(quest => quest.Id)
            .ToHashSet(StringComparer.Ordinal);
        var states = new List<QuestStateDto>(applicable.Count);

        foreach (var quest in topology.Quests.Where(quest => applicable.Contains(quest.Id)))
        {
            profileQuests.TryGetValue(quest.Id, out var profileQuest);
            authoritative.TryGetValue(quest.Id, out var authoritativeStatus);
            var exclusion = FindExclusion(quest, profileQuests);
            var blockers = QuestProfileRules.GetBlockers(
                quest,
                pmc.Info.Level ?? 0,
                pmc.TradersInfo,
                profileQuests,
                topology.Edges,
                traderAvailability
            );
            var exactStatus = profileQuest?.Status ?? authoritativeStatus;
            var authoritativelyVisible = QuestProfileRules.IsEffectivelyVisible(authoritative.ContainsKey(quest.Id), blockers);
            var displayState = QuestProfileRules.Classify(quest, exactStatus, authoritativelyVisible, exclusion, blockers);
            var completed = profileQuest?.CompletedConditions?.ToHashSet(StringComparer.Ordinal) ?? [];
            var objectives = quest.Objectives.Select(objective =>
            {
                var counter = FindCounter(pmc.TaskConditionCounters, objective.Id, quest.Id);
                var conditionRecorded = completed.Contains(objective.Id);
                return new ObjectiveProgressDto(
                    objective.Id,
                    QuestProfileRules.ObjectiveIsComplete(conditionRecorded, counter?.Value, objective.RequiredValue, objective.Compare),
                    counter?.Value,
                    objective.RequiredValue,
                    counter is not null || conditionRecorded
                );
            }).ToArray();
            var progressPercent = displayState == "InProgress"
                ? QuestProfileRules.CalculateObjectiveProgress(objectives)
                : null;

            states.Add(new QuestStateDto(
                quest.Id,
                exactStatus?.ToString(),
                displayState,
                authoritativelyVisible,
                profileQuest is not null,
                profileQuest?.AvailableAfter,
                blockers,
                exclusion,
                objectives,
                progressPercent
            ));
        }

        var graphApplicable = states
            .Where(QuestProfileRules.ShouldShowInDefaultGraph)
            .Select(state => state.QuestId)
            .ToHashSet(StringComparer.Ordinal);
        var known = states
            .Where(state => graphApplicable.Contains(state.QuestId))
            .Where(state => state.InProfile || state.AuthoritativelyVisible)
            .Select(state => state.QuestId)
            .ToHashSet(StringComparer.Ordinal);
        var futureBoundary = states
            .Where(state => state.DisplayState is "Available" or "InProgress" or "ReadyToFinish" or "Completed")
            .Select(state => state.QuestId)
            .ToHashSet(StringComparer.Ordinal);
        var defaultVisible = QuestGraphRules.BuildDefaultVisible(known, futureBoundary, topology.Edges, graphApplicable);

        var traders = databaseService.GetTraders().Select(pair =>
        {
            pmc.TradersInfo.TryGetValue(pair.Key, out var profileTrader);
            return new TraderStateDto(
                pair.Key.ToString(),
                traderAvailability.IsAvailable(pair.Key),
                profileTrader?.LoyaltyLevel,
                profileTrader?.Standing,
                profileTrader?.SalesSum
            );
        }).ToArray();

        return new ProfileStateDto(
            profileId.ToString(),
            pmc.Info.Nickname ?? "Unnamed profile",
            pmc.Info.Side ?? "Unknown",
            pmc.Info.Level ?? 0,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            seasonalEventService.ChristmasEventEnabled(),
            seasonalEventService.HalloweenEventEnabled(),
            states,
            traders,
            defaultVisible.Order(StringComparer.Ordinal).ToArray(),
            applicable.Order(StringComparer.Ordinal).ToArray()
        );
    }

    private bool IsApplicable(QuestNodeDto quest, string side, HashSet<string> noneExcluded)
    {
        if (noneExcluded.Contains(quest.Id)) return false;
        if (quest.Faction != "Any" && !string.Equals(quest.Faction, side, StringComparison.OrdinalIgnoreCase)) return false;
        if (quest.EventSeason == nameof(SeasonalEventType.Christmas) && !seasonalEventService.ChristmasEventEnabled()) return false;
        if (quest.EventSeason == nameof(SeasonalEventType.Halloween) && !seasonalEventService.HalloweenEventEnabled()) return false;
        return questHelper.ShowEventQuestToPlayer(new MongoId(quest.Id));
    }

    private static QuestExclusionDto? FindExclusion(QuestNodeDto quest, Dictionary<string, QuestStatus> profileQuests)
    {
        foreach (var rule in quest.ExclusionRules)
        {
            if (profileQuests.TryGetValue(rule.CausedByQuestId, out var cause)
                && rule.RequiredStatuses.Contains(cause.Status.ToString(), StringComparer.Ordinal))
            {
                return new QuestExclusionDto(rule.CausedByQuestId, cause.Status.ToString(), !quest.Restartable);
            }
        }

        return null;
    }

    private static TaskConditionCounter? FindCounter(
        Dictionary<MongoId, TaskConditionCounter>? counters,
        string objectiveId,
        string questId
    )
    {
        if (counters is null) return null;
        if (MongoId.IsValidMongoId(objectiveId)
            && counters.TryGetValue(new MongoId(objectiveId), out var direct))
        {
            return direct;
        }

        return counters.Values.FirstOrDefault(counter =>
            counter.Id?.ToString() == objectiveId && counter.SourceId?.ToString() == questId);
    }
}
