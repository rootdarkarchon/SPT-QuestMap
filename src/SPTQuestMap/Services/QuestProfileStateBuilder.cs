using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Services;

internal sealed class QuestProfileStateBuilder(
    DatabaseService databaseService,
    LocaleService localeService,
    SaveServer saveServer,
    QuestHelper questHelper,
    SeasonalEventService seasonalEventService,
    QuestZoneMapCatalog zoneMapCatalog,
    ISptLogger<QuestMapDataService> logger
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
        var profileQuests = BuildProfileQuestLookup(
            pmc.Quests ?? [],
            (questId, kept, ignored) =>
            {
                var node = topology.Quests.FirstOrDefault(quest => quest.Id == questId);
                var description = node is null
                    ? $"unknown quest {questId}"
                    : $"'{node.Name}' ({questId}) from trader '{node.TraderName}' ({node.TraderId})";
                logger.Warning($"SPT-QuestMap: profile {profileId} contains duplicate status rows for {description}; keeping first status {kept.Status} and ignoring later status {ignored.Status} to match SPT 4.0.13.");
            }
        );
        var profileQuestStatuses = profileQuests.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Status.ToString(),
            StringComparer.Ordinal
        );
        var traderAvailability = new TraderAvailabilityEvaluator(pmc.TradersInfo, profileQuestStatuses);
        Dictionary<string, QuestStatusEnum?> authoritative;
        lock (_availabilityLock)
        {
            authoritative = new Dictionary<string, QuestStatusEnum?>(StringComparer.Ordinal);
            foreach (var quest in questHelper.GetClientQuests(profileId))
            {
                var questId = quest.Id.ToString();
                if (!authoritative.TryAdd(questId, quest.SptStatus))
                {
                    logger.Warning($"SPT-QuestMap: SPT returned quest {questId} more than once for profile {profileId}; keeping the first authoritative result.");
                }
            }
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
                pmc.Info.PrestigeLevel ?? 0,
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
                    IsObjectiveComplete(objective, exactStatus, conditionRecorded, counter),
                    QuestProgressRules.CapCurrent(counter?.Value, objective.RequiredValue),
                    objective.RequiredValue,
                    counter is not null || conditionRecorded
                );
            }).ToArray();
            var progressPercent = displayState == "InProgress"
                ? QuestProgressRules.CalculateObjectiveProgressPercent(
                    objectives,
                    objective => objective.Complete,
                    objective => objective.Current,
                    objective => objective.Required)
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
        var repeatableGroups = EnsureUniqueRepeatableQuestIds(
            BuildRepeatableQuestGroups(pmc, profileQuests, language, generatedAt),
            topology.Quests.Select(quest => quest.Id),
            (entry, collision) => logger.Warning($"SPT-QuestMap: repeatable quest '{entry.Node.Name}' ({entry.Node.Id}) from trader '{entry.Node.TraderName}' ({entry.Node.TraderId}) collides with {collision}; keeping the earlier canonical entry.")
        );

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
            RepeatableQuestGroups = repeatableGroups,
        };
    }

    internal static Dictionary<string, QuestStatus> BuildProfileQuestLookup(
        IEnumerable<QuestStatus> quests,
        Action<string, QuestStatus, QuestStatus>? onDuplicate = null
    )
    {
        var result = new Dictionary<string, QuestStatus>(StringComparer.Ordinal);
        foreach (var quest in quests)
        {
            var questId = quest.QId.ToString();
            if (result.TryAdd(questId, quest)) continue;
            onDuplicate?.Invoke(questId, result[questId], quest);
        }

        return result;
    }

    internal static RepeatableQuestGroupDto[] EnsureUniqueRepeatableQuestIds(
        IEnumerable<RepeatableQuestGroupDto> groups,
        IEnumerable<string> reservedQuestIds,
        Action<RepeatableQuestEntryDto, string>? onCollision = null
    )
    {
        var reserved = reservedQuestIds.ToHashSet(StringComparer.Ordinal);
        var occupied = new HashSet<string>(reserved, StringComparer.Ordinal);
        var result = new List<RepeatableQuestGroupDto>();
        foreach (var group in groups)
        {
            var unique = new List<RepeatableQuestEntryDto>();
            foreach (var entry in group.Quests)
            {
                if (occupied.Add(entry.Node.Id))
                {
                    unique.Add(entry);
                    continue;
                }

                onCollision?.Invoke(
                    entry,
                    reserved.Contains(entry.Node.Id) ? "the static quest topology" : "an earlier Daily/Weekly entry"
                );
            }

            if (unique.Count > 0) result.Add(group with { Quests = unique });
        }

        return result.ToArray();
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
        var locationsById = QuestTemplateMapper.BuildLocationLookup(locationValues);

        return (pmc.RepeatableQuests ?? [])
            .Where(group => RepeatableQuestRules.ShouldIncludeGroup(group.Name))
            .Select(group =>
            {
                var scav = RepeatableQuestRules.IsScavGroup(group.Name);
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
                        expired,
                        scav
                    ))
                    .OrderBy(entry => QuestTraderOrder.Rank(entry.Node.TraderId))
                    .ThenBy(entry => entry.Node.TraderName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Node.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.Node.Id, StringComparer.Ordinal)
                    .ToArray();
                return new RepeatableQuestGroupDto(RepeatableQuestRules.DisplayKind(group.Name), endTime, entries)
                {
                    Scav = scav,
                };
            })
            .Where(group => group.Quests.Count > 0)
            .OrderBy(group => group.Scav ? 1 : group.Kind == "Daily" ? 0 : 2)
            .ToArray();
    }

    private RepeatableQuestEntryDto BuildRepeatableQuestEntry(
        RepeatableQuest quest,
        QuestStatus? profileQuest,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<MongoId, Trader> traders,
        IReadOnlyDictionary<MongoId, TemplateItem> items,
        IReadOnlyDictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location> locationsById,
        Dictionary<MongoId, TaskConditionCounter>? counters,
        bool expired,
        bool scav
    )
    {
        traders.TryGetValue(quest.TraderId, out var trader);
        var questId = quest.Id.ToString();
        var traderId = quest.TraderId.ToString();
        var traderName = QuestTemplateMapper.Localize(locale, $"{traderId} Nickname", trader?.Base.Nickname ?? trader?.Base.Name ?? traderId);
        var location = QuestTemplateMapper.BuildLocation(quest.Location, locale, locationsById);
        var typeName = QuestTemplateMapper.Localize(locale, $"DailyQuestName/{quest.Type}", quest.Type.ToString());
        var finishConditions = quest.Conditions?.AvailableForFinish ?? [];
        var objectives = QuestTemplateMapper.ResolveObjectiveMaps(QuestTemplateMapper.OrderObjectives(
                finishConditions,
                locale,
                duplicateId => logger.Warning($"SPT-QuestMap: duplicate objective condition ID '{duplicateId}' on repeatable quest {questId}; keeping its first definition.")
            ), zoneMapCatalog)
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
        var actualMaps = QuestTemplateMapper.BuildActualMaps(location, objectives, locale, locationsById);
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
        )
        {
            ActualMaps = actualMaps.Maps,
            ActualMapsComplete = actualMaps.Complete,
            ScavRepeatable = scav,
        };

        var exactStatus = profileQuest?.Status ?? RepeatableQuestRules.ExactStatus(quest.QuestStatus?.Status);
        var displayState = RepeatableQuestRules.Classify(exactStatus, expired);
        var completed = profileQuest?.CompletedConditions?.ToHashSet(StringComparer.Ordinal) ?? [];
        var objectiveProgress = objectives.Select(objective =>
        {
            var counter = FindCounter(counters, objective.Id, questId);
            var conditionRecorded = completed.Contains(objective.Id);
            return new ObjectiveProgressDto(
                objective.Id,
                IsObjectiveComplete(objective, exactStatus, conditionRecorded, counter),
                QuestProgressRules.CapCurrent(counter?.Value, objective.RequiredValue),
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
            displayState == "InProgress"
                ? QuestProgressRules.CalculateObjectiveProgressPercent(
                    objectiveProgress,
                    objective => objective.Complete,
                    objective => objective.Current,
                    objective => objective.Required)
                : null
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

    internal static bool IsObjectiveComplete(
        ObjectiveDefinitionDto objective,
        QuestStatusEnum? exactStatus,
        bool conditionRecorded,
        TaskConditionCounter? counter) =>
        QuestProgressRules.IsComplete(
            conditionRecorded,
            counter?.Value,
            objective.RequiredValue,
            objective.Compare,
            exactStatus == QuestStatusEnum.Started
            && objective.OneSessionOnly
            && !objective.DoNotResetIfCounterCompleted
            && counter is not null);

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
