using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

internal sealed class QuestProfileStateBuilder(
    DatabaseService databaseService,
    LocaleService localeService,
    SaveServer saveServer,
    QuestHelper questHelper,
    SeasonalEventService seasonalEventService,
    QuestConfig questConfig
)
{
    private readonly object _availabilityLock = new();

    internal ProfileStateDto? Build(string rawProfileId, QuestTopologyDto topology, string language)
    {
        if (!MongoId.IsValidMongoId(rawProfileId)) return null;

        var profileId = new MongoId(rawProfileId);
        if (!saveServer.ProfileExists(profileId) || saveServer.IsProfileInvalidOrUnloadable(profileId)) return null;

        var pmc = saveServer.GetProfile(profileId).CharacterData?.PmcData;
        if (pmc?.Info is null) return null;

        var generatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
                    QuestProfileRules.CapObjectiveCurrent(counter?.Value, objective.RequiredValue),
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
            generatedAt,
            seasonalEventService.ChristmasEventEnabled(),
            seasonalEventService.HalloweenEventEnabled(),
            states,
            traders,
            defaultVisible.Order(StringComparer.Ordinal).ToArray(),
            applicable.Order(StringComparer.Ordinal).ToArray()
        )
        {
            RepeatableQuestGroups = BuildRepeatableQuestGroups(pmc, profileQuests, language, generatedAt),
        };
    }

    private RepeatableQuestGroupDto[] BuildRepeatableQuestGroups(
        PmcData pmc,
        IReadOnlyDictionary<string, QuestStatus> profileQuests,
        string language,
        long generatedAt
    )
    {
        var locale = localeService.GetLocaleDb(language);
        var traders = databaseService.GetTraders();
        var items = databaseService.GetItems();
        var locationValues = databaseService
            .GetLocations()
            .GetDictionary()
            .Values
            .Where(location => location?.Base?.Id is not null);
        var locationsById = QuestTemplateMapper.BuildLocationLookup(locationValues, questConfig.LocationIdMap);

        return (pmc.RepeatableQuests ?? [])
            .Where(group => RepeatableQuestRules.ShouldIncludeGroup(group.Name))
            .Select(group =>
            {
                var endTime = group.EndTime ?? 0;
                var expired = endTime <= generatedAt;
                var entries = (group.ActiveQuests ?? [])
                    .Select(quest => BuildRepeatableQuestEntry(
                        quest,
                        profileQuests.GetValueOrDefault(quest.Id.ToString()),
                        locale,
                        traders,
                        items,
                        locationsById,
                        pmc.TaskConditionCounters,
                        expired
                    ))
                    .OrderBy(entry => QuestMapTraderOrder.Rank(entry.Node.TraderId))
                    .ThenBy(entry => entry.Node.TraderName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Node.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Node.Id, StringComparer.Ordinal)
                    .ToArray();
                return new RepeatableQuestGroupDto(group.Name!, endTime, entries);
            })
            .Where(group => group.Quests.Count > 0)
            .OrderBy(group => group.Kind == "Daily" ? 0 : 1)
            .ToArray();
    }

    private static RepeatableQuestEntryDto BuildRepeatableQuestEntry(
        RepeatableQuest quest,
        QuestStatus? profileQuest,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<MongoId, Trader> traders,
        IReadOnlyDictionary<MongoId, TemplateItem> items,
        IReadOnlyDictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location> locationsById,
        Dictionary<MongoId, TaskConditionCounter>? counters,
        bool expired
    )
    {
        traders.TryGetValue(quest.TraderId, out var trader);
        var questId = quest.Id.ToString();
        var traderId = quest.TraderId.ToString();
        var traderName = QuestTemplateMapper.Localize(locale, $"{traderId} Nickname", trader?.Base.Nickname ?? trader?.Base.Name ?? traderId);
        var location = QuestTemplateMapper.BuildLocation(quest.Location, locale, locationsById);
        var typeName = QuestTemplateMapper.Localize(locale, $"DailyQuestName/{quest.Type}", quest.Type.ToString());
        var finishConditions = quest.Conditions?.AvailableForFinish ?? [];
        var objectives = QuestTemplateMapper.OrderObjectives(finishConditions, locale)
            .Select(objective => objective with
            {
                Text = RepeatableObjectiveText(
                    finishConditions.First(condition => condition.Id.ToString() == objective.Id),
                    typeName,
                    location,
                    locale,
                    items
                ),
            })
            .ToArray();
        var rewards = QuestTemplateMapper.BuildRewards(
            quest.Rewards?.GetValueOrDefault("Success") ?? [],
            locale,
            items,
            traders
        );
        var node = new QuestNodeDto(
            questId,
            typeName,
            QuestTemplateMapper.Localize(locale, quest.Description, quest.Description),
            traderId,
            traderName,
            QuestTemplateMapper.ToFileUrl(trader?.Base.Avatar),
            QuestTemplateMapper.ToFileUrl(quest.Image) ?? string.Empty,
            "Any",
            location,
            null,
            false,
            [],
            [],
            objectives,
            [],
            rewards
        );

        var exactStatus = profileQuest?.Status ?? RepeatableQuestRules.ExactStatus(quest.QuestStatus?.Status);
        var displayState = RepeatableQuestRules.Classify(exactStatus, expired);
        var completed = profileQuest?.CompletedConditions?.ToHashSet(StringComparer.Ordinal) ?? [];
        var objectiveProgress = objectives.Select(objective =>
        {
            var counter = FindCounter(counters, objective.Id, questId);
            var conditionRecorded = completed.Contains(objective.Id);
            return new ObjectiveProgressDto(
                objective.Id,
                QuestProfileRules.ObjectiveIsComplete(conditionRecorded, counter?.Value, objective.RequiredValue, objective.Compare),
                QuestProfileRules.CapObjectiveCurrent(counter?.Value, objective.RequiredValue),
                objective.RequiredValue,
                counter is not null || conditionRecorded
            );
        }).ToArray();
        var state = new QuestStateDto(
            questId,
            exactStatus.ToString(),
            displayState,
            !expired && displayState == "Available",
            profileQuest is not null,
            profileQuest?.AvailableAfter,
            [],
            null,
            objectiveProgress,
            displayState == "InProgress" ? QuestProfileRules.CalculateObjectiveProgress(objectiveProgress) : null
        );
        return new RepeatableQuestEntryDto(node, state);
    }

    private static string RepeatableObjectiveText(
        QuestCondition condition,
        string typeName,
        QuestLocationDto location,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<MongoId, TemplateItem> items
    )
    {
        if (condition.ConditionType is "HandoverItem" or "FindItem")
        {
            var targets = QuestTemplateMapper.GetTargets(condition)
                .Select(target =>
                {
                    if (!MongoId.IsValidMongoId(target)) return target;
                    var itemId = new MongoId(target);
                    items.TryGetValue(itemId, out var item);
                    return QuestTemplateMapper.Localize(locale, $"{target} Name", item?.Name ?? target);
                })
                .ToArray();
            if (targets.Length > 0) return $"{typeName}: {string.Join(", ", targets)}";
        }

        var counterConditions = condition.Counter?.Conditions ?? [];
        var killTarget = counterConditions
            .FirstOrDefault(counter => counter.ConditionType == "Kills")?
            .Target;
        var target = killTarget is null
            ? null
            : killTarget.IsList ? killTarget.List?.FirstOrDefault() : killTarget.Item;
        if (!string.IsNullOrWhiteSpace(target))
        {
            return $"{typeName}: {QuestTemplateMapper.Localize(locale, $"QuestCondition/Elimination/Kill/Target/{target}", target)}";
        }

        if (!location.Any && !string.IsNullOrWhiteSpace(location.Name)) return $"{typeName}: {location.Name}";
        return typeName;
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
