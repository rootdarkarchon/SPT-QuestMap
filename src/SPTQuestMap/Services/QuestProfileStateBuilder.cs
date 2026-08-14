using System.Diagnostics;
using System.Globalization;
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
    QuestMapTopologyPreload preload,
    QuestZoneMapCatalog zoneMapCatalog,
    ISptLogger<QuestMapDataService> logger
)
{
    internal ProfileStateDto? Build(string rawProfileId, QuestTopologyDto topology, string language)
    {
        if (!MongoId.IsValidMongoId(rawProfileId)) return null;

        var profileId = new MongoId(rawProfileId);
        if (!saveServer.ProfileExists(profileId) || saveServer.IsProfileInvalidOrUnloadable(profileId)) return null;

        var pmc = saveServer.GetProfile(profileId).CharacterData?.PmcData;
        if (pmc?.Info is null) return null;

        var totalStopwatch = Stopwatch.StartNew();
        var stageStopwatch = Stopwatch.StartNew();
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
        var authoritative = QuestAvailabilityProjection.Build(
            databaseService.GetQuests().Values,
            pmc.Quests ?? [],
            profileQuests,
            pmc.Info.Side ?? string.Empty,
            pmc.Info.Level ?? 0,
            pmc.TradersInfo.Keys,
            questHelper.QuestIsForOtherSide,
            questHelper.ShowEventQuestToPlayer,
            questHelper.DoesPlayerLevelFulfilCondition,
            condition => questHelper.TraderLoyaltyLevelRequirementCheck(condition, pmc),
            condition => questHelper.TraderStandingRequirementCheck(condition, pmc)
        );
        var availabilityMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();

        var noneExcluded = QuestGraphRules.BuildNoneEventExclusionSet(topology);
        var incomingEdges = topology.Edges
            .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<QuestEdgeDto>)group.ToArray(), StringComparer.Ordinal);
        var applicable = topology.Quests
            .Where(quest => IsApplicable(quest, pmc.Info.Side ?? string.Empty, noneExcluded))
            .Select(quest => quest.Id)
            .ToHashSet(StringComparer.Ordinal);
        var states = new List<QuestStateDto>(applicable.Count);
        var applicabilityMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();

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
                incomingEdges.GetValueOrDefault(quest.Id) ?? [],
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
                )
                {
                    ContributesToProgress = objective.ContributesToProgress,
                };
            }).ToArray();
            var progressOwners = objectives.Where(objective => objective.ContributesToProgress).ToArray();
            var progressPercent = displayState == "InProgress"
                ? QuestProgressRules.CalculateObjectiveProgressPercent(
                    progressOwners,
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
        var statesMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();

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
        var frontierMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();

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

        var finalizeMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        totalStopwatch.Stop();
        logger.Info(
            "QUESTMAP_M08_SERVER_PROFILE_BUILD " +
            $"quests={states.Count}; available={authoritative.Count}; availabilityMs={availabilityMilliseconds:F2}; " +
            $"applicabilityMs={applicabilityMilliseconds:F2}; statesMs={statesMilliseconds:F2}; " +
            $"frontierMs={frontierMilliseconds:F2}; finalizeMs={finalizeMilliseconds:F2}; " +
            $"totalMs={totalStopwatch.Elapsed.TotalMilliseconds:F2}");

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
        var ui = QuestMapUiCatalog.For(language, locale);
        var traders = databaseService.GetTraders();
        var items = preload.Items;
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
                        scav,
                        ui
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
        bool scav,
        IReadOnlyDictionary<string, string> ui
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
            .Select(objective => !objective.ContributesToProgress
                ? objective
                : objective with
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
            TaskLocation = QuestTemplateMapper.BuildTaskLocation(location, objectives, ui),
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
            )
            {
                ContributesToProgress = objective.ContributesToProgress,
            };
        }).ToArray();
        var repeatableProgressOwners = objectiveProgress.Where(objective => objective.ContributesToProgress).ToArray();
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
                    repeatableProgressOwners,
                    objective => objective.Complete,
                    objective => objective.Current,
                    objective => objective.Required)
                : null
        );
        return new RepeatableQuestEntryDto(node, state);
    }

    internal static string RepeatableObjectiveText(
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
        var kill = counterConditions.FirstOrDefault(counter => counter.ConditionType == "Kills");
        if (kill is not null) return RepeatableEliminationText(condition, kill, typeName, locale, items);

        if (counterConditions.Any(counter => counter.ConditionType == "ExitStatus"))
            return RepeatableExplorationText(counterConditions, location, locale);

        if (!location.Any && !string.IsNullOrWhiteSpace(location.Name)) return $"{typeName}: {location.Name}";
        return typeName;
    }

    private static string RepeatableExplorationText(
        IReadOnlyCollection<QuestConditionCounterCondition> counterConditions,
        QuestLocationDto location,
        Dictionary<string, string> locale)
    {
        var hasSpecificLocation = counterConditions.Any(counter =>
            counter.ConditionType == "Location" && QuestTemplateMapper.GetTargets(counter).Any());
        var locationText = QuestTemplateMapper.Localize(
            locale,
            hasSpecificLocation || !location.Any
                ? "QuestCondition/SurviveOnLocation/Location"
                : "QuestCondition/SurviveOnLocation/Any",
            hasSpecificLocation || !location.Any ? "the location" : "any location");

        var exit = counterConditions.FirstOrDefault(counter =>
            counter.ConditionType == "ExitName" && !string.IsNullOrWhiteSpace(counter.ExitName));
        var exitText = string.Empty;
        if (exit?.ExitName is { } exitName)
        {
            var localizedExitName = QuestTemplateMapper.Localize(locale, exitName, exitName);
            exitText = FormatLocale(
                QuestTemplateMapper.Localize(
                    locale,
                    "QuestCondition/SurviveOnLocation/ExitName",
                    " by extracting through the \"{0}\""),
                localizedExitName);
        }

        return ReplaceTokens(
            QuestTemplateMapper.Localize(
                locale,
                "QuestCondition/SurviveOnLocation",
                "Survive on {location}{exitName}"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["location"] = locationText,
                ["exitName"] = exitText,
            }).Trim();
    }

    private static string RepeatableEliminationText(
        QuestCondition objective,
        QuestConditionCounterCondition kill,
        string typeName,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<MongoId, TemplateItem> items)
    {
        var roles = (kill.SavageRole ?? [])
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => QuestTemplateMapper.Localize(
                locale,
                $"QuestCondition/Elimination/Kill/BotRole/{role}",
                role))
            .ToArray();
        var botRole = roles.Length == 0
            ? string.Empty
            : FormatLocale(
                QuestTemplateMapper.Localize(locale, "QuestCondition/Elimination/Kill/BotRole", "the target: {0}"),
                string.Join(", ", roles));

        var target = roles.Length > 0
            ? string.Empty
            : string.Join(", ", QuestTemplateMapper.GetTargets(kill)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value =>
                {
                    var localeTarget = string.Equals(value, "AnyPmc", StringComparison.OrdinalIgnoreCase)
                        ? "AnyPMC"
                        : value;
                    return QuestTemplateMapper.Localize(
                        locale,
                        $"QuestCondition/Elimination/Kill/Target/{localeTarget}",
                        value);
                }));

        var bodyParts = (kill.BodyPart ?? [])
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => QuestTemplateMapper.Localize(
                locale,
                $"QuestCondition/Elimination/Kill/BodyPart/{part}",
                part))
            .ToArray();
        var bodyPart = bodyParts.Length == 0
            ? string.Empty
            : FormatLocale(
                QuestTemplateMapper.Localize(locale, "QuestCondition/Elimination/Kill/BodyPart", " with a {0} shot"),
                string.Join(", ", bodyParts));

        var distance = kill.Distance is null
            ? string.Empty
            : FormatLocale(
                QuestTemplateMapper.Localize(locale, "QuestCondition/Elimination/Kill/Distance", " from a distance of{0} {1}m"),
                kill.Distance.CompareMethod ?? string.Empty,
                kill.Distance.Value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        var weaponNames = (kill.Weapon ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id =>
            {
                var fallback = id;
                if (MongoId.IsValidMongoId(id) && items.TryGetValue(new MongoId(id), out var item))
                    fallback = item.Name ?? id;
                return QuestTemplateMapper.Localize(locale, $"{id} Name", fallback);
            })
            .ToArray();
        var weapon = weaponNames.Length == 0
            ? string.Empty
            : FormatLocale(
                QuestTemplateMapper.Localize(locale, "QuestCondition/Elimination/Kill/Weapon", " while using {0}"),
                string.Join(", ", weaponNames));
        var oneSession = objective.OneSessionOnly == true
            ? QuestTemplateMapper.Localize(locale, "QuestCondition/Elimination/Kill/OneSession", " in a single raid")
            : string.Empty;

        var killTemplate = QuestTemplateMapper.Localize(
            locale,
            "QuestCondition/Elimination/Kill",
            " {target}{botrole}{bodypart}{distance}{weapon}{weapontype}{onesession}");
        var killText = ReplaceTokens(killTemplate, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target"] = target,
            ["botrole"] = botRole,
            ["bodypart"] = bodyPart,
            ["distance"] = distance,
            ["weapon"] = weapon,
            ["weapontype"] = string.Empty,
            ["onesession"] = oneSession,
        });

        if (!locale.TryGetValue("QuestCondition/Elimination", out var eliminationTemplate)
            || string.IsNullOrWhiteSpace(eliminationTemplate))
            return $"{typeName}: {killText.Trim()}";
        return ReplaceTokens(eliminationTemplate, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kill"] = killText,
            ["zone"] = string.Empty,
            ["enemyPreset"] = string.Empty,
            ["playerPreset"] = string.Empty,
            ["resetOnSessionEnd"] = string.Empty,
        }).Trim();
    }

    private static string ReplaceTokens(string template, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values) template = template.Replace($"{{{pair.Key}}}", pair.Value, StringComparison.Ordinal);
        return template;
    }

    private static string FormatLocale(string template, params object[] values)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, values);
        }
        catch (FormatException)
        {
            for (var index = 0; index < values.Length; index++)
                template = template.Replace($"{{{index}}}", values[index]?.ToString() ?? string.Empty, StringComparison.Ordinal);
            return template;
        }
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
