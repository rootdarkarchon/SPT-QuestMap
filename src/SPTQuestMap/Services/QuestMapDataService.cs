using System.Security.Cryptography;
using System.Text;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

[Injectable(InjectionType.Singleton)]
public sealed class QuestMapDataService(
    DatabaseService databaseService,
    LocaleService localeService,
    SaveServer saveServer,
    QuestHelper questHelper,
    SeasonalEventService seasonalEventService,
#pragma warning disable CS0618 // SPT 4.0.13 DI provides ConfigServer; direct config injection is a 4.1 behavior.
    ConfigServer configServer,
#pragma warning restore CS0618
    ISptLogger<QuestMapDataService> logger
)
{
    private const string CollectorQuestId = "5c51aac186f77432ea65c552";
    private const string KnockKnockQuestId = "625d7005a4eb80027c4f2e09";
    private const string IntroductionQuestId = "5d2495a886f77425cd51e403";
    private const string RefPveUnlockQuestId = "6834145ebc1f443d7603c8a7";
    private readonly object _topologyLock = new();
    private readonly object _availabilityLock = new();
    private readonly Dictionary<string, QuestTopologyDto> _topologies = new(StringComparer.OrdinalIgnoreCase);
#pragma warning disable CS0618 // SPT 4.0.13 exposes config instances through ConfigServer; direct config DI starts in 4.1.
    private readonly QuestConfig _questConfig = configServer.GetConfig<QuestConfig>();
#pragma warning restore CS0618

    public QuestTopologyDto GetTopology(string? requestedLanguage = null)
    {
        var language = ResolveLanguage(requestedLanguage);
        lock (_topologyLock)
        {
            if (_topologies.TryGetValue(language, out var topology))
            {
                return topology;
            }

            topology = BuildTopology(language);
            _topologies[language] = topology;
            return topology;
        }
    }

    public QuestMapBootstrapDto GetBootstrap(string? requestedLanguage = null)
    {
        var language = ResolveLanguage(requestedLanguage);
        var languages = databaseService
            .GetLocales()
            .Languages
            .Where(pair => databaseService.GetLocales().Global.ContainsKey(pair.Key))
            .Select(pair => new QuestMapLanguageDto(pair.Key, pair.Value))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();

        return new QuestMapBootstrapDto(language, ToBrowserLocale(language), languages, QuestMapUiCatalog.For(language, localeService.GetLocaleDb(language)));
    }

    internal static string ToBrowserLocale(string language) => language.ToLowerInvariant() switch
    {
        "ch" => "zh-CN",
        "cz" => "cs",
        "ge" => "de",
        "jp" => "ja",
        "kr" => "ko",
        "po" => "pt-PT",
        "tu" => "tr",
        _ => language,
    };

    internal string ResolveLanguage(string? requestedLanguage)
    {
        var locales = databaseService.GetLocales().Global;
        var requested = requestedLanguage?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(requested) && locales.ContainsKey(requested))
        {
            return requested;
        }

        var configured = localeService.GetDesiredGameLocale();
        if (locales.ContainsKey(configured)) return configured;
        if (locales.ContainsKey("en")) return "en";
        return locales.Keys.Order(StringComparer.Ordinal).First();
    }

    public ProfileStateDto? GetProfileState(string rawProfileId)
    {
        if (!MongoId.IsValidMongoId(rawProfileId))
        {
            return null;
        }

        var profileId = new MongoId(rawProfileId);
        if (!saveServer.ProfileExists(profileId) || saveServer.IsProfileInvalidOrUnloadable(profileId))
        {
            return null;
        }

        var fullProfile = saveServer.GetProfile(profileId);
        var pmc = fullProfile.CharacterData?.PmcData;
        if (pmc?.Info is null)
        {
            return null;
        }

        // Profile classification is locale-independent. English is used only as the
        // cached metadata instance backing the structural quest and edge records.
        var topology = GetTopology("en");
        var profileQuests = (pmc.Quests ?? []).ToDictionary(quest => quest.QId.ToString(), StringComparer.Ordinal);
        var profileQuestStatuses = profileQuests.ToDictionary(pair => pair.Key, pair => pair.Value.Status.ToString(), StringComparer.Ordinal);
        var lightkeeperPathSatisfied = IsQuestPathSatisfied(
            KnockKnockQuestId,
            topology.LightkeeperPathQuestIds,
            topology.Edges,
            profileQuestStatuses
        );
        Dictionary<string, QuestStatusEnum?> authoritative;
        lock (_availabilityLock)
        {
            authoritative = questHelper
                .GetClientQuests(profileId)
                .ToDictionary(quest => quest.Id.ToString(), quest => quest.SptStatus, StringComparer.Ordinal);
        }

        var noneExcluded = BuildNoneEventExclusionSet(topology);
        var applicable = topology
            .Quests
            .Where(quest => IsApplicable(quest, pmc.Info.Side ?? string.Empty, noneExcluded))
            .Select(quest => quest.Id)
            .ToHashSet(StringComparer.Ordinal);
        var states = new List<QuestStateDto>(applicable.Count);

        foreach (var quest in topology.Quests.Where(quest => applicable.Contains(quest.Id)))
        {
            profileQuests.TryGetValue(quest.Id, out var profileQuest);
            authoritative.TryGetValue(quest.Id, out var authoritativeStatus);
            var exclusion = FindExclusion(quest, profileQuests);
            var blockers = GetBlockers(quest, pmc.Info.Level ?? 0, pmc.TradersInfo, profileQuests, profileQuestStatuses, topology.Edges, lightkeeperPathSatisfied);
            var exactStatus = profileQuest?.Status ?? authoritativeStatus;
            var authoritativelyVisible = IsEffectivelyVisible(authoritative.ContainsKey(quest.Id), blockers);
            var displayState = Classify(quest, exactStatus, authoritativelyVisible, exclusion, blockers);
            var completed = profileQuest?.CompletedConditions?.ToHashSet(StringComparer.Ordinal) ?? [];
            var objectives = quest
                .Objectives
                .Select(objective =>
                {
                    var counter = FindCounter(pmc.TaskConditionCounters, objective.Id, quest.Id);
                    var conditionRecorded = completed.Contains(objective.Id);
                    return new ObjectiveProgressDto(
                        objective.Id,
                        ObjectiveIsComplete(conditionRecorded, counter?.Value, objective.RequiredValue, objective.Compare),
                        counter?.Value,
                        objective.RequiredValue,
                        counter is not null || conditionRecorded
                    );
                })
                .ToArray();
            var progressPercent = displayState == "InProgress" ? CalculateObjectiveProgress(objectives) : null;

            states.Add(
                new QuestStateDto(
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
                )
            );
        }

        var graphApplicable = states
            .Where(ShouldShowInDefaultGraph)
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
        var defaultVisible = BuildDefaultVisible(known, futureBoundary, topology.Edges, graphApplicable);

        var traders = databaseService
            .GetTraders()
            .Select(pair =>
            {
                pmc.TradersInfo.TryGetValue(pair.Key, out var profileTrader);
                return new TraderStateDto(
                    pair.Key.ToString(),
                    TraderIsAvailable(pair.Key, pmc.TradersInfo, profileQuestStatuses, lightkeeperPathSatisfied),
                    profileTrader?.LoyaltyLevel,
                    profileTrader?.Standing,
                    profileTrader?.SalesSum
                );
            })
            .ToArray();

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

    private QuestTopologyDto BuildTopology(string language)
    {
        var locale = localeService.GetLocaleDb(language);
        var dbQuests = databaseService.GetQuests().Values.OrderBy(quest => quest.Id.ToString(), StringComparer.Ordinal).ToArray();
        var items = databaseService.GetItems();
        var traders = databaseService.GetTraders();
        var locationValues = databaseService
            .GetLocations()
            .GetDictionary()
            .Values
            .Where(location => location?.Base?.Id is not null);
        var locationsById = BuildLocationLookup(locationValues, _questConfig.LocationIdMap);
        var edges = new List<QuestEdgeDto>();
        var directRequirements = new Dictionary<string, RequirementDto[]>(StringComparer.Ordinal);
        var nodes = new List<QuestNodeDto>(dbQuests.Length);

        foreach (var quest in dbQuests)
        {
            var questId = quest.Id.ToString();
            var startConditions = quest.Conditions?.AvailableForStart ?? [];
            var requirements = startConditions
                .Where(condition => condition.ConditionType is "Level" or "TraderLoyalty" or "TraderStanding")
                .Select(ToRequirement)
                .Where(requirement => requirement is not null)
                .Cast<RequirementDto>()
                .ToArray();
            directRequirements[questId] = requirements;

            foreach (var condition in startConditions.Where(condition => condition.ConditionType == "Quest"))
            {
                foreach (var target in GetTargets(condition))
                {
                    edges.Add(
                        new QuestEdgeDto(
                            target,
                            questId,
                            (condition.Status ?? []).Select(status => status.ToString()).Order(StringComparer.Ordinal).ToArray(),
                            condition.AvailableAfter ?? 0
                        )
                    );
                }
            }

            foreach (var unsupported in startConditions.Select(condition => condition.ConditionType).Distinct().Where(type => type is not ("Quest" or "Level" or "TraderLoyalty" or "TraderStanding")))
            {
                logger.Warning($"SPT-QuestMap: unsupported start condition '{unsupported}' on quest {questId}; availability remains authoritative but the blocker explanation may be incomplete.");
            }

            var faction = GetQuestFaction(quest.Id);
            var season = GetQuestSeason(quest.Id);
            var location = BuildLocation(quest.Location, locale, locationsById);
            traders.TryGetValue(quest.TraderId, out var trader);
            var objectives = OrderObjectives(quest.Conditions?.AvailableForFinish ?? [], locale).ToArray();
            var rewards = BuildRewards(
                quest.Rewards?.GetValueOrDefault(QuestStatusEnum.Success.ToString()) ?? [],
                locale,
                items,
                traders
            );
            var exclusions = (quest.Conditions?.Fail ?? [])
                .Where(condition => condition.ConditionType == "Quest")
                .SelectMany(condition => GetTargets(condition).Select(target => new QuestExclusionRuleDto(target, (condition.Status ?? []).Select(status => status.ToString()).ToArray())))
                .ToArray();

            nodes.Add(
                new QuestNodeDto(
                    questId,
                    Locale(locale, $"{questId} name", quest.QuestName ?? quest.Name ?? questId),
                    Locale(locale, $"{questId} description", string.Empty),
                    quest.TraderId.ToString(),
                    Locale(locale, $"{quest.TraderId} Nickname", trader?.Base.Nickname ?? trader?.Base.Name ?? quest.TraderId.ToString()),
                    trader?.Base.Avatar,
                    quest.Image,
                    faction,
                    location,
                    season,
                    quest.Restartable,
                    requirements,
                    [],
                    objectives,
                    exclusions,
                    rewards
                )
            );
        }

        nodes = PropagateSeasonalEventTypes(nodes, edges);
        var effective = nodes.ToDictionary(node => node.Id, node => node.DirectRequirements, StringComparer.Ordinal);
        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            foreach (var edge in edges)
            {
                if (!effective.TryGetValue(edge.SourceId, out var parent) || !effective.TryGetValue(edge.TargetId, out var child))
                {
                    continue;
                }

                var merged = MergeRequirements(child.Concat(parent));
                if (!merged.SequenceEqual(child))
                {
                    effective[edge.TargetId] = merged;
                    changed = true;
                }
            }

            if (!changed)
            {
                break;
            }
        }

        nodes = nodes.Select(node => node with { EffectiveRequirements = effective[node.Id] }).ToList();
        var collectorPathQuestIds = BuildPrerequisiteClosure(CollectorQuestId, edges, nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal));
        if (collectorPathQuestIds.Count == 0)
        {
            logger.Warning($"SPT-QuestMap: Collector quest {CollectorQuestId} was not found; Collector-path markers are disabled.");
        }
        var lightkeeperPathQuestIds = BuildPrerequisiteClosure(KnockKnockQuestId, edges, nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal));
        if (lightkeeperPathQuestIds.Count == 0)
        {
            logger.Warning($"SPT-QuestMap: Knock-Knock quest {KnockKnockQuestId} was not found; Lightkeeper-path markers and unlock evaluation are disabled.");
        }
        var canonical = string.Join('\n', nodes.Select(node => node.Id).Concat(edges.OrderBy(edge => edge.SourceId).ThenBy(edge => edge.TargetId).Select(edge => $"{edge.SourceId}>{edge.TargetId}:{string.Join(',', edge.RequiredStatuses)}")));
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
        var traderCatalog = traders
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new QuestTraderDto(
                pair.Key.ToString(),
                Locale(locale, $"{pair.Key} Nickname", pair.Value.Base.Nickname ?? pair.Value.Base.Name ?? pair.Key.ToString()),
                pair.Value.Base.Avatar
            ))
            .ToArray();
        return new QuestTopologyDto(
            version,
            nodes,
            edges,
            traderCatalog,
            collectorPathQuestIds.Order(StringComparer.Ordinal).ToArray(),
            lightkeeperPathQuestIds.Order(StringComparer.Ordinal).ToArray()
        );
    }

    internal static QuestLocationDto BuildLocation(
        string? locationId,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location> locationsById
    )
    {
        if (string.IsNullOrWhiteSpace(locationId) || string.Equals(locationId, "any", StringComparison.OrdinalIgnoreCase))
        {
            return new QuestLocationDto("any", null, true, null);
        }

        locationsById.TryGetValue(locationId, out var location);
        var fallback = location?.Base?.Name;
        var bannerPath = location?.Base?.Banners?.FirstOrDefault()?.Picture?.Path;
        return new QuestLocationDto(
            locationId,
            Locale(locale, $"{locationId} Name", string.IsNullOrWhiteSpace(fallback) ? locationId : fallback),
            false,
            ToFileUrl(bannerPath)
        );
    }

    internal static Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location> BuildLocationLookup(
        IEnumerable<SPTarkov.Server.Core.Models.Eft.Common.Location> locations,
        IReadOnlyDictionary<string, string> locationIdMap
    )
    {
        var byInternalId = locations
            .GroupBy(location => location.Base.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>(byInternalId, StringComparer.OrdinalIgnoreCase);
        foreach (var (internalId, questLocationId) in locationIdMap)
        {
            if (byInternalId.TryGetValue(internalId, out var location)) result[questLocationId] = location;
        }

        return result;
    }

    internal static string? ToFileUrl(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;
        var normalized = assetPath.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("files/", StringComparison.OrdinalIgnoreCase) ? $"/{normalized}" : $"/files/{normalized}";
    }

    private string GetQuestFaction(MongoId questId)
    {
        // These helpers are SPT 4.0.13's public accessors over QuestConfig's
        // usecOnlyQuests/bearOnlyQuests sets. A quest hidden from BEAR is USEC-only.
        if (questHelper.QuestIsForOtherSide("BEAR", questId))
        {
            return "USEC";
        }

        return questHelper.QuestIsForOtherSide("USEC", questId) ? "BEAR" : "Any";
    }

    private string? GetQuestSeason(MongoId questId)
    {
        // IsQuestRelatedToEvent is SPT 4.0.13's public accessor over
        // QuestConfig.EventQuests and preserves every enum value supported by this build.
        foreach (var eventType in Enum.GetValues<SeasonalEventType>())
        {
            if (seasonalEventService.IsQuestRelatedToEvent(questId, eventType))
            {
                return eventType.ToString();
            }
        }

        return null;
    }

    private bool IsApplicable(QuestNodeDto quest, string side, HashSet<string> noneExcluded)
    {
        if (noneExcluded.Contains(quest.Id))
        {
            return false;
        }

        if (quest.Faction != "Any" && !string.Equals(quest.Faction, side, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (quest.EventSeason == nameof(SeasonalEventType.Christmas) && !seasonalEventService.ChristmasEventEnabled()) return false;
        if (quest.EventSeason == nameof(SeasonalEventType.Halloween) && !seasonalEventService.HalloweenEventEnabled()) return false;

        var id = new MongoId(quest.Id);
        return questHelper.ShowEventQuestToPlayer(id);
    }

    internal static List<QuestNodeDto> PropagateSeasonalEventTypes(IReadOnlyList<QuestNodeDto> nodes, IReadOnlyList<QuestEdgeDto> edges)
    {
        var seasons = nodes.ToDictionary(node => node.Id, node => node.EventSeason, StringComparer.Ordinal);
        var incoming = edges.GroupBy(edge => edge.TargetId).ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).ToArray(), StringComparer.Ordinal);
        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            foreach (var node in nodes.Where(node => seasons[node.Id] is null))
            {
                if (!incoming.TryGetValue(node.Id, out var parents)) continue;
                var inherited = parents.Select(parent => seasons.GetValueOrDefault(parent)).Where(season => season is not null && season != nameof(SeasonalEventType.None)).Distinct(StringComparer.Ordinal).ToArray();
                if (inherited.Length != 1) continue;
                seasons[node.Id] = inherited[0];
                changed = true;
            }

            if (!changed) break;
        }

        return nodes.Select(node => node with { EventSeason = seasons[node.Id] }).ToList();
    }

    internal static HashSet<string> BuildNoneEventExclusionSet(QuestTopologyDto topology)
    {
        var excluded = topology.Quests.Where(quest => quest.EventSeason == nameof(SeasonalEventType.None)).Select(quest => quest.Id).ToHashSet(StringComparer.Ordinal);
        var incoming = topology.Edges.GroupBy(edge => edge.TargetId).ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).ToArray(), StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (target, parents) in incoming)
            {
                if (!excluded.Contains(target) && parents.Length > 0 && parents.All(excluded.Contains))
                {
                    excluded.Add(target);
                    changed = true;
                }
            }
        }

        return excluded;
    }

    internal static QuestBlockerDto[] GetBlockers(
        QuestNodeDto quest,
        int level,
        Dictionary<MongoId, TraderInfo> traders,
        Dictionary<string, QuestStatus> profileQuests,
        IReadOnlyDictionary<string, string> profileQuestStatuses,
        IReadOnlyCollection<QuestEdgeDto> edges,
        bool lightkeeperPathSatisfied
    )
    {
        var blockers = new List<QuestBlockerDto>();
        if (MongoId.IsValidMongoId(quest.TraderId))
        {
            var questTraderId = new MongoId(quest.TraderId);
            if (!TraderIsAvailable(questTraderId, traders, profileQuestStatuses, lightkeeperPathSatisfied))
            {
                blockers.Add(new QuestBlockerDto("TraderUnavailable", quest.TraderId, null, null, []));
            }
        }

        // Descendants inherit the gates of their prerequisite chain. The detail pane
        // already presents these effective requirements, so classification must use
        // the same set rather than only the conditions declared on this quest.
        foreach (var requirement in quest.EffectiveRequirements)
        {
            if (requirement.Kind == "Level" && !Compare(level, requirement.Value, requirement.Compare))
            {
                blockers.Add(new QuestBlockerDto("Level", null, requirement.Compare, requirement.Value, []));
                continue;
            }

            if (requirement.TraderId is null || !MongoId.IsValidMongoId(requirement.TraderId))
            {
                continue;
            }

            var traderId = new MongoId(requirement.TraderId);
            if (!TraderIsAvailable(traderId, traders, profileQuestStatuses, lightkeeperPathSatisfied))
            {
                blockers.Add(new QuestBlockerDto("TraderUnavailable", requirement.TraderId, requirement.Compare, requirement.Value, []));
                continue;
            }

            var trader = traders[traderId];
            var actual = requirement.Kind == "TraderLoyalty" ? trader.LoyaltyLevel : trader.Standing;
            if (!actual.HasValue || !Compare(actual.Value, requirement.Value, requirement.Compare))
            {
                blockers.Add(new QuestBlockerDto(requirement.Kind, requirement.TraderId, requirement.Compare, requirement.Value, []));
            }
        }

        // Keep blocker output in the same priority order used by Classify:
        // trader availability, player level, trader requirements, prerequisites.
        foreach (var edge in edges.Where(edge => edge.TargetId == quest.Id))
        {
            if (!profileQuests.TryGetValue(edge.SourceId, out var prerequisite) || !edge.RequiredStatuses.Contains(prerequisite.Status.ToString(), StringComparer.Ordinal))
            {
                blockers.Add(new QuestBlockerDto("Prerequisite", edge.SourceId, null, null, edge.RequiredStatuses));
            }
        }

        return blockers.ToArray();
    }

    internal static bool TraderIsAvailable(MongoId traderId, Dictionary<MongoId, TraderInfo> traders, IReadOnlyDictionary<string, string> questStatuses, bool lightkeeperPathSatisfied)
    {
        if (!traders.TryGetValue(traderId, out var trader) || trader.Unlocked != true || trader.Disabled == true)
        {
            return false;
        }

        if (traderId == Traders.JAEGER)
        {
            return questStatuses.TryGetValue(IntroductionQuestId, out var introductionStatus) && introductionStatus == nameof(QuestStatusEnum.Success);
        }

        if (traderId == Traders.REF)
        {
            return questStatuses.TryGetValue(RefPveUnlockQuestId, out var refUnlockStatus) && refUnlockStatus == nameof(QuestStatusEnum.Success);
        }

        return traderId != Traders.LIGHTHOUSEKEEPER || lightkeeperPathSatisfied;
    }

    internal static string Classify(QuestNodeDto quest, QuestStatusEnum? status, bool authoritative, QuestExclusionDto? exclusion, QuestBlockerDto[] blockers)
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
        conditionRecorded || (current.HasValue && required.HasValue && Compare(current.Value, required.Value, compare ?? ">="));

    private QuestExclusionDto? FindExclusion(QuestNodeDto quest, Dictionary<string, QuestStatus> profileQuests)
    {
        foreach (var rule in quest.ExclusionRules)
        {
            if (profileQuests.TryGetValue(rule.CausedByQuestId, out var cause) && rule.RequiredStatuses.Contains(cause.Status.ToString(), StringComparer.Ordinal))
            {
                return new QuestExclusionDto(rule.CausedByQuestId, cause.Status.ToString(), !quest.Restartable);
            }
        }

        return null;
    }

    private static TaskConditionCounter? FindCounter(Dictionary<MongoId, TaskConditionCounter>? counters, string objectiveId, string questId)
    {
        if (counters is null) return null;
        if (MongoId.IsValidMongoId(objectiveId) && counters.TryGetValue(new MongoId(objectiveId), out var direct)) return direct;
        return counters.Values.FirstOrDefault(counter => counter.Id?.ToString() == objectiveId && counter.SourceId?.ToString() == questId);
    }

    internal static IEnumerable<ObjectiveDefinitionDto> OrderObjectives(IEnumerable<QuestCondition> conditions, Dictionary<string, string> locale)
    {
        var source = conditions.Select((condition, sourceIndex) => new ObjectiveOrderItem(condition, sourceIndex)).ToArray();
        var byId = source.ToDictionary(item => item.Condition.Id.ToString(), StringComparer.Ordinal);
        var dependencies = source.ToDictionary(
            item => item.Condition.Id.ToString(),
            item => new HashSet<string>(
                (string.IsNullOrWhiteSpace(item.Condition.ParentId) ? [] : new[] { item.Condition.ParentId })
                    .Concat(item.Condition.VisibilityConditions?.Select(condition => condition.Target).Where(target => !string.IsNullOrWhiteSpace(target)).Cast<string>() ?? [] )
                    .Where(byId.ContainsKey),
                StringComparer.Ordinal
            ),
            StringComparer.Ordinal
        );
        var ordered = new List<ObjectiveOrderItem>(source.Length);
        var remaining = source.ToDictionary(item => item.Condition.Id.ToString(), StringComparer.Ordinal);
        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(item => dependencies[item.Condition.Id.ToString()].All(dependency => !remaining.ContainsKey(dependency)))
                .OrderBy(item => item.Condition.Index ?? int.MaxValue)
                .ThenBy(item => item.SourceIndex)
                .ToArray();
            if (ready.Length == 0)
            {
                ready = remaining.Values.OrderBy(item => item.SourceIndex).ToArray();
            }

            foreach (var item in ready)
            {
                ordered.Add(item);
                remaining.Remove(item.Condition.Id.ToString());
            }
        }

        return ordered.Select(item => new ObjectiveDefinitionDto(
                item.Condition.Id.ToString(),
                Locale(locale, item.Condition.Id.ToString(), item.Condition.ConditionType),
                item.Condition.ConditionType,
                item.Condition.Index,
                item.Condition.ParentId,
                item.Condition.Value,
                item.Condition.CompareMethod,
                item.Condition.VisibilityConditions?.Select(condition => condition.Target).Where(target => !string.IsNullOrEmpty(target)).Cast<string>().ToArray() ?? []
            ));
    }

    internal static QuestRewardDto[] BuildRewards(
        IEnumerable<Reward> rewards,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<MongoId, TemplateItem> items,
        IReadOnlyDictionary<MongoId, Trader> traders
    )
    {
        return rewards
            .OrderBy(reward => reward.Index ?? int.MaxValue)
            .Select(reward =>
            {
                var target = reward.Target;
                var traderName = ResolveTraderName(target, reward.TraderId, locale, traders);
                var rewardItems = (reward.Items ?? [])
                    .Where(item => string.IsNullOrWhiteSpace(item.ParentId))
                    .GroupBy(item => item.Template)
                    .Select(group =>
                    {
                        items.TryGetValue(group.Key, out var template);
                        var templateId = group.Key.ToString();
                        return new QuestRewardItemDto(
                            templateId,
                            Locale(locale, $"{templateId} Name", template?.Name ?? templateId),
                            group.Sum(item => item.Upd?.StackObjectsCount ?? 1)
                        );
                    })
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                var targetName = string.IsNullOrWhiteSpace(target)
                    ? null
                    : Locale(locale, $"{target} name", Locale(locale, target, traderName ?? target));

                return new QuestRewardDto(
                    reward.Id.ToString(),
                    reward.Type?.ToString() ?? "Unknown",
                    target,
                    targetName,
                    reward.Value,
                    reward.LoyaltyLevel,
                    traderName,
                    reward.Unknown == true,
                    reward.IsHidden == true,
                    rewardItems
                );
            })
            .ToArray();
    }

    private static string? ResolveTraderName(
        string? target,
        object? traderId,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<MongoId, Trader> traders
    )
    {
        var id = traderId?.ToString();
        if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(target) && MongoId.IsValidMongoId(target)) id = target;
        if (string.IsNullOrWhiteSpace(id) || !MongoId.IsValidMongoId(id)) return null;
        var mongoId = new MongoId(id);
        if (!traders.TryGetValue(mongoId, out var trader)) return null;
        return Locale(locale, $"{id} Nickname", trader.Base.Nickname ?? trader.Base.Name ?? id);
    }

    private static RequirementDto? ToRequirement(QuestCondition condition)
    {
        if (!condition.Value.HasValue) return null;
        var traderId = condition.ConditionType == "Level" ? null : GetTargets(condition).FirstOrDefault();
        return new RequirementDto(condition.ConditionType, traderId, condition.CompareMethod ?? ">=", condition.Value.Value);
    }

    internal static RequirementDto[] MergeRequirements(IEnumerable<RequirementDto> requirements)
    {
        return requirements
            .GroupBy(requirement => $"{requirement.Kind}|{requirement.TraderId}|{Direction(requirement.Compare)}", StringComparer.Ordinal)
            .Select(group => Direction(group.First().Compare) == "upper" ? group.OrderBy(requirement => requirement.Value).First() : group.OrderByDescending(requirement => requirement.Value).First())
            .OrderBy(requirement => requirement.Kind, StringComparer.Ordinal)
            .ThenBy(requirement => requirement.TraderId, StringComparer.Ordinal)
            .ThenBy(requirement => requirement.Compare, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Direction(string compare) => compare is "<" or "<=" ? "upper" : compare is ">" or ">=" ? "lower" : compare;

    internal static bool Compare(double actual, double required, string compare) => compare switch
    {
        ">=" => actual >= required,
        ">" => actual > required,
        "<=" => actual <= required,
        "<" => actual < required,
        "=" or "==" => Math.Abs(actual - required) < 0.000001,
        _ => false,
    };

    private static IEnumerable<string> GetTargets(QuestCondition condition)
    {
        if (condition.Target is null) return [];
        if (condition.Target.IsList) return condition.Target.List ?? [];
        return condition.Target.Item is null ? [] : [condition.Target.Item];
    }

    private static string Locale(Dictionary<string, string> locale, string key, string fallback) => locale.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    internal static HashSet<string> BuildDefaultVisible(
        HashSet<string> known,
        HashSet<string> futureBoundary,
        IReadOnlyCollection<QuestEdgeDto> edges,
        HashSet<string> applicable
    )
    {
        var applicableEdges = edges
            .Where(edge => applicable.Contains(edge.SourceId) && applicable.Contains(edge.TargetId))
            .ToArray();
        var frontierCandidates = applicableEdges
            .Where(edge => futureBoundary.Contains(edge.SourceId) && !known.Contains(edge.TargetId))
            .Select(edge => edge.TargetId)
            .Distinct(StringComparer.Ordinal);
        var visible = new HashSet<string>(known, StringComparer.Ordinal);

        foreach (var candidate in frontierCandidates)
        {
            visible.Add(candidate);
        }

        return visible;
    }

    internal static HashSet<string> BuildPrerequisiteClosure(string questId, IReadOnlyCollection<QuestEdgeDto> edges, HashSet<string> questIds)
    {
        if (!questIds.Contains(questId))
        {
            return [];
        }

        var incoming = edges
            .Where(edge => questIds.Contains(edge.SourceId) && questIds.Contains(edge.TargetId))
            .GroupBy(edge => edge.TargetId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var result = new HashSet<string>([questId], StringComparer.Ordinal);
        var queue = new Queue<string>([questId]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var prerequisite in incoming.GetValueOrDefault(current, []))
            {
                if (result.Add(prerequisite))
                {
                    queue.Enqueue(prerequisite);
                }
            }
        }

        return result;
    }

    internal static double? CalculateObjectiveProgress(IReadOnlyCollection<ObjectiveProgressDto> objectives)
    {
        if (objectives.Count == 0)
        {
            return null;
        }

        var completedShare = objectives.Sum(objective =>
        {
            if (objective.Complete)
            {
                return 1d;
            }

            if (objective.Current.HasValue && objective.Required is > 0)
            {
                return Math.Clamp(objective.Current.Value / objective.Required.Value, 0d, 1d);
            }

            return 0d;
        });
        return Math.Round(completedShare / objectives.Count * 100d, 1, MidpointRounding.AwayFromZero);
    }

    internal static bool IsQuestPathSatisfied(
        string finalQuestId,
        IReadOnlyCollection<string> requiredQuestIds,
        IReadOnlyCollection<QuestEdgeDto> edges,
        IReadOnlyDictionary<string, string> exactStatuses
    )
    {
        if (requiredQuestIds.Count == 0
            || !exactStatuses.TryGetValue(finalQuestId, out var finalStatus)
            || finalStatus != QuestStatusEnum.Success.ToString())
        {
            return false;
        }

        var required = requiredQuestIds.ToHashSet(StringComparer.Ordinal);
        return edges
            .Where(edge => required.Contains(edge.SourceId) && required.Contains(edge.TargetId))
            .All(edge => exactStatuses.TryGetValue(edge.SourceId, out var sourceStatus)
                && edge.RequiredStatuses.Contains(sourceStatus, StringComparer.Ordinal));
    }

    internal static string ClassifyEdgeRequirement(IReadOnlyCollection<string> requiredStatuses)
    {
        var hasStarted = requiredStatuses.Contains(nameof(QuestStatusEnum.Started), StringComparer.Ordinal);
        var hasSuccess = requiredStatuses.Contains(nameof(QuestStatusEnum.Success), StringComparer.Ordinal);
        var hasFailure = requiredStatuses.Any(status => status is nameof(QuestStatusEnum.Fail)
            or nameof(QuestStatusEnum.FailRestartable)
            or nameof(QuestStatusEnum.MarkedAsFailed));

        if (hasStarted) return "Started";
        if (hasSuccess && hasFailure) return "AnyOutcome";
        if (hasFailure) return "Failure";
        if (hasSuccess) return "Success";
        return "Other";
    }

    private sealed record ObjectiveOrderItem(QuestCondition Condition, int SourceIndex);
}

public sealed record QuestTopologyDto(string Version, IReadOnlyList<QuestNodeDto> Quests, IReadOnlyList<QuestEdgeDto> Edges, IReadOnlyList<QuestTraderDto> Traders, string[] CollectorPathQuestIds, string[] LightkeeperPathQuestIds);
public sealed record QuestTraderDto(string Id, string Name, string? ImageUrl);
public sealed record QuestNodeDto(string Id, string Name, string Description, string TraderId, string TraderName, string? TraderImageUrl, string ImageUrl, string Faction, QuestLocationDto Location, string? EventSeason, bool Restartable, RequirementDto[] DirectRequirements, RequirementDto[] EffectiveRequirements, ObjectiveDefinitionDto[] Objectives, QuestExclusionRuleDto[] ExclusionRules, QuestRewardDto[] Rewards);
public sealed record QuestLocationDto(string Id, string? Name, bool Any, string? BannerImageUrl);
public sealed record QuestEdgeDto(string SourceId, string TargetId, string[] RequiredStatuses, int AvailableAfterSeconds)
{
    public string RequirementKind => QuestMapDataService.ClassifyEdgeRequirement(RequiredStatuses);
}
public sealed record RequirementDto(string Kind, string? TraderId, string Compare, double Value);
public sealed record ObjectiveDefinitionDto(string Id, string Text, string ConditionType, int? Index, string? ParentId, double? RequiredValue, string? Compare, string[] DependsOn);
public sealed record QuestRewardDto(string Id, string Type, string? TargetId, string? TargetName, double? Value, int? LoyaltyLevel, string? TraderName, bool Unknown, bool Hidden, QuestRewardItemDto[] Items);
public sealed record QuestRewardItemDto(string TemplateId, string Name, double Count);
public sealed record QuestExclusionRuleDto(string CausedByQuestId, string[] RequiredStatuses);
public sealed record ProfileStateDto(string ProfileId, string Nickname, string Side, int Level, long GeneratedAt, bool ChristmasActive, bool HalloweenActive, IReadOnlyList<QuestStateDto> Quests, IReadOnlyList<TraderStateDto> Traders, string[] DefaultVisibleQuestIds, string[] AllApplicableQuestIds);
public sealed record QuestStateDto(string QuestId, string? ExactStatus, string DisplayState, bool AuthoritativelyVisible, bool InProfile, double? AvailableAfter, QuestBlockerDto[] Blockers, QuestExclusionDto? Exclusion, ObjectiveProgressDto[] Objectives, double? ProgressPercent);
public sealed record QuestBlockerDto(string Kind, string? SubjectId, string? Compare, double? RequiredValue, string[] RequiredStatuses);
public sealed record QuestExclusionDto(string CausedByQuestId, string CauseStatus, bool Permanent);
public sealed record ObjectiveProgressDto(string ObjectiveId, bool Complete, double? Current, double? Required, bool ProgressKnown);
public sealed record TraderStateDto(string TraderId, bool Available, int? LoyaltyLevel, double? Standing, double? SalesSum);
public sealed record QuestMapBootstrapDto(string Language, string BrowserLocale, QuestMapLanguageDto[] Languages, IReadOnlyDictionary<string, string> Strings);
public sealed record QuestMapLanguageDto(string Code, string Name);

internal static class QuestMapUiCatalog
{
    private static readonly string[] CoreOverrideKeys =
    [
        "app.title", "app.heading", "profile.label", "showAllFuture", "hideFinished", "search.placeholder",
        "trader.all", "language.label", "fit", "focus", "focus.clear", "selection.clear", "legend.states",
        "details.selectTitle", "details.selectHelp", "zoom.in", "zoom.out", "legend.futureTier",
        "inProgress.title", "showFinished", "levelEligible", "trader.allShort",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> CoreOverrides = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["ch"] = ["SPT 任务地图", "任务地图", "档案", "显示所有后续任务", "隐藏已结束任务", "任务、商人或 ID", "所有商人", "语言", "适应视图", "聚焦任务链", "清除聚焦", "清除选择", "状态", "选择一个任务", "点击节点查看任务条件、分支关系和有序目标。", "放大", "缩小", "后续层级", "进行中的任务", "显示已完成任务", "仅显示等级符合的任务", "全部"],
        ["cz"] = ["SPT mapa úkolů", "Mapa úkolů", "Profil", "Zobrazit všechny budoucí", "Skrýt dokončené", "Úkol, obchodník nebo ID", "Všichni obchodníci", "Jazyk", "Přizpůsobit", "Zaměřit řetězec", "Zrušit zaměření", "Zrušit výběr", "Stavy", "Vyberte úkol", "Kliknutím na uzel zobrazíte podmínky, větvení a seřazené cíle.", "Přiblížit", "Oddálit", "Budoucí úroveň", "Probíhající úkoly", "Zobrazit dokončené", "Zobrazit jen úkoly odpovídající úrovni", "Vše"],
        ["es"] = ["Mapa de misiones SPT", "Mapa de misiones", "Perfil", "Mostrar todo el futuro", "Ocultar finalizadas", "Misión, comerciante o ID", "Todos los comerciantes", "Idioma", "Ajustar", "Centrar cadena", "Quitar enfoque", "Borrar selección", "Estados", "Selecciona una misión", "Haz clic en un nodo para ver requisitos, ramas y objetivos ordenados.", "Acercar", "Alejar", "Nivel futuro", "Misiones en curso", "Mostrar finalizadas", "Mostrar solo misiones aptas para el nivel", "Todos"],
        ["es-mx"] = ["Mapa de misiones SPT", "Mapa de misiones", "Perfil", "Mostrar todo el futuro", "Ocultar finalizadas", "Misión, comerciante o ID", "Todos los comerciantes", "Idioma", "Ajustar", "Centrar cadena", "Quitar enfoque", "Borrar selección", "Estados", "Selecciona una misión", "Haz clic en un nodo para ver requisitos, ramas y objetivos ordenados.", "Acercar", "Alejar", "Nivel futuro", "Misiones en curso", "Mostrar finalizadas", "Mostrar solo misiones aptas para el nivel", "Todos"],
        ["fr"] = ["Carte des quêtes SPT", "Carte des quêtes", "Profil", "Afficher tout le futur", "Masquer les terminées", "Quête, marchand ou ID", "Tous les marchands", "Langue", "Ajuster", "Cibler la chaîne", "Effacer le ciblage", "Effacer la sélection", "États", "Sélectionnez une quête", "Cliquez sur un nœud pour voir les conditions, branches et objectifs ordonnés.", "Zoom avant", "Zoom arrière", "Niveau futur", "Quêtes en cours", "Afficher les terminées", "Afficher uniquement les quêtes adaptées au niveau", "Tous"],
        ["ge"] = ["SPT-Auftragskarte", "Auftragskarte", "Profil", "Alle zukünftigen anzeigen", "Abgeschlossene ausblenden", "Auftrag, Händler oder ID", "Alle Händler", "Sprache", "Einpassen", "Kette fokussieren", "Fokus aufheben", "Auswahl aufheben", "Status", "Auftrag auswählen", "Klicke einen Knoten an, um Bedingungen, Verzweigungen und geordnete Ziele zu sehen.", "Vergrößern", "Verkleinern", "Zukünftige Stufe", "Laufende Aufträge", "Abgeschlossene anzeigen", "Nur stufengerechte Aufträge anzeigen", "Alle"],
        ["hu"] = ["SPT küldetéstérkép", "Küldetéstérkép", "Profil", "Minden jövőbeli mutatása", "Befejezettek elrejtése", "Küldetés, kereskedő vagy ID", "Minden kereskedő", "Nyelv", "Igazítás", "Lánc fókuszálása", "Fókusz törlése", "Kijelölés törlése", "Állapotok", "Válassz küldetést", "Kattints egy csomópontra a feltételek, ágak és rendezett célok megtekintéséhez.", "Nagyítás", "Kicsinyítés", "Jövőbeli szint", "Folyamatban lévő küldetések", "Befejezettek megjelenítése", "Csak szintnek megfelelő küldetések", "Mind"],
        ["it"] = ["Mappa missioni SPT", "Mappa missioni", "Profilo", "Mostra tutto il futuro", "Nascondi completate", "Missione, commerciante o ID", "Tutti i commercianti", "Lingua", "Adatta", "Metti a fuoco la catena", "Rimuovi fuoco", "Cancella selezione", "Stati", "Seleziona una missione", "Fai clic su un nodo per vedere requisiti, rami e obiettivi ordinati.", "Ingrandisci", "Riduci", "Livello futuro", "Missioni in corso", "Mostra completate", "Mostra solo missioni adatte al livello", "Tutti"],
        ["jp"] = ["SPT クエストマップ", "クエストマップ", "プロフィール", "将来のクエストをすべて表示", "完了済みを非表示", "クエスト、トレーダー、ID", "すべてのトレーダー", "言語", "全体表示", "チェーンにフォーカス", "フォーカス解除", "選択解除", "状態", "クエストを選択", "ノードをクリックして条件、分岐、順序付き目標を確認します。", "拡大", "縮小", "将来階層", "進行中のクエスト", "完了済みを表示", "レベル条件を満たすクエストのみ表示", "すべて"],
        ["kr"] = ["SPT 퀘스트 지도", "퀘스트 지도", "프로필", "모든 향후 퀘스트 표시", "완료된 퀘스트 숨기기", "퀘스트, 상인 또는 ID", "모든 상인", "언어", "화면에 맞추기", "연계 퀘스트 집중", "집중 해제", "선택 해제", "상태", "퀘스트 선택", "노드를 클릭하여 조건, 분기 관계 및 정렬된 목표를 확인하세요.", "확대", "축소", "향후 단계", "진행 중인 퀘스트", "완료된 퀘스트 표시", "레벨에 맞는 퀘스트만 표시", "전체"],
        ["pl"] = ["Mapa zadań SPT", "Mapa zadań", "Profil", "Pokaż wszystkie przyszłe", "Ukryj zakończone", "Zadanie, handlarz lub ID", "Wszyscy handlarze", "Język", "Dopasuj", "Skup na łańcuchu", "Wyczyść skupienie", "Wyczyść wybór", "Stany", "Wybierz zadanie", "Kliknij węzeł, aby zobaczyć warunki, rozgałęzienia i uporządkowane cele.", "Powiększ", "Pomniejsz", "Przyszły poziom", "Zadania w toku", "Pokaż ukończone", "Pokaż tylko zadania odpowiednie dla poziomu", "Wszyscy"],
        ["po"] = ["Mapa de missões SPT", "Mapa de missões", "Perfil", "Mostrar todo o futuro", "Ocultar concluídas", "Missão, comerciante ou ID", "Todos os comerciantes", "Idioma", "Ajustar", "Focar cadeia", "Limpar foco", "Limpar seleção", "Estados", "Selecione uma missão", "Clique num nó para ver requisitos, ramificações e objetivos ordenados.", "Aumentar", "Diminuir", "Nível futuro", "Missões em andamento", "Mostrar concluídas", "Mostrar apenas missões adequadas ao nível", "Todos"],
        ["ro"] = ["Harta misiunilor SPT", "Harta misiunilor", "Profil", "Arată toate misiunile viitoare", "Ascunde finalizate", "Misiune, comerciant sau ID", "Toți comercianții", "Limbă", "Potrivește", "Focalizează lanțul", "Șterge focalizarea", "Șterge selecția", "Stări", "Selectează o misiune", "Apasă pe un nod pentru a vedea condițiile, ramurile și obiectivele ordonate.", "Mărește", "Micșorează", "Nivel viitor", "Misiuni în desfășurare", "Arată finalizate", "Arată doar misiunile potrivite nivelului", "Toți"],
        ["ru"] = ["Карта заданий SPT", "Карта заданий", "Профиль", "Показать все будущие", "Скрыть завершённые", "Задание, торговец или ID", "Все торговцы", "Язык", "Вместить", "Фокус на цепочке", "Снять фокус", "Снять выделение", "Состояния", "Выберите задание", "Нажмите на узел, чтобы увидеть условия, ветви и упорядоченные цели.", "Приблизить", "Отдалить", "Будущий уровень", "Выполняемые задания", "Показать завершённые", "Показать только задания подходящего уровня", "Все"],
        ["sk"] = ["Mapa úloh SPT", "Mapa úloh", "Profil", "Zobraziť všetky budúce", "Skryť dokončené", "Úloha, obchodník alebo ID", "Všetci obchodníci", "Jazyk", "Prispôsobiť", "Zamerať reťazec", "Zrušiť zameranie", "Zrušiť výber", "Stavy", "Vyberte úlohu", "Kliknutím na uzol zobrazíte podmienky, vetvy a zoradené ciele.", "Priblížiť", "Oddialiť", "Budúca úroveň", "Prebiehajúce úlohy", "Zobraziť dokončené", "Zobraziť len úlohy vhodné pre úroveň", "Všetci"],
        ["tu"] = ["SPT görev haritası", "Görev haritası", "Profil", "Tüm geleceği göster", "Tamamlananları gizle", "Görev, tüccar veya ID", "Tüm tüccarlar", "Dil", "Sığdır", "Zincire odaklan", "Odağı temizle", "Seçimi temizle", "Durumlar", "Bir görev seç", "Koşulları, dalları ve sıralı hedefleri görmek için bir düğüme tıkla.", "Yakınlaştır", "Uzaklaştır", "Gelecek seviye", "Devam eden görevler", "Tamamlananları göster", "Yalnızca seviyeye uygun görevleri göster", "Tümü"],
    };

    private static readonly string[] ExtendedOverrideKeys =
    [
        "legend.priorGate", "legend.levelGate", "legend.traderRequirement", "legend.failedExcluded",
        "legend.collectorRoute", "legend.lightkeeperRoute", "legend.requiresSuccess", "legend.requiresFailure",
        "legend.requiresStarted", "legend.requiresOutcome",
        "state.PrerequisiteGated", "state.LevelGated", "state.TraderGated", "state.TraderUnavailable",
        "state.InProgress", "state.ReadyToFinish", "state.Excluded", "state.RestartableFailure", "state.Expired",
        "state.Pending", "state.FailRestartable", "state.AvailableAfter", "details.effectiveGates",
        "details.availableAfter", "details.currentBlockers", "details.mutualExclusion", "details.branchAlternatives",
        "details.prerequisites", "details.successors",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> ExtendedOverrides = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["ch"] = ["前置任务要求", "等级要求", "商人要求", "失败 / 排除", "收藏家路线", "Lightkeeper 路线", "需要成功", "需要失败", "需要开始", "任一结果", "前置任务限制", "等级限制", "商人等级限制", "商人不可用", "进行中", "可完成", "已排除", "可重新开始的失败", "已过期", "等待中", "可重新开始的失败", "此后可用", "实际要求", "可用时间", "当前阻碍", "互斥", "分支选项", "前置任务", "直接后续任务"],
        ["cz"] = ["Vyžaduje předchozí úkol", "Požadavek úrovně", "Požadavek obchodníka", "Neúspěšné / vyloučené", "Trasa Sběratele", "Trasa Lightkeepera", "Vyžaduje úspěch", "Vyžaduje neúspěch", "Vyžaduje zahájení", "Libovolný výsledek", "Blokováno předchozím úkolem", "Blokováno úrovní", "Blokováno obchodníkem", "Obchodník nedostupný", "Probíhá", "Připraveno k dokončení", "Vyloučeno", "Opakovatelný neúspěch", "Vypršelo", "Čeká", "Opakovatelný neúspěch", "Dostupné po", "Platné požadavky", "Dostupné po", "Aktuální překážky", "Vzájemné vyloučení", "Alternativní větve", "Předpoklady", "Přímé následné úkoly"],
        ["es"] = ["Requiere misión anterior", "Requisito de nivel", "Requisito de comerciante", "Fallida / excluida", "Ruta de Coleccionista", "Ruta de Lightkeeper", "Requiere éxito", "Requiere fallo", "Requiere inicio", "Cualquier resultado", "Bloqueada por misión previa", "Bloqueada por nivel", "Bloqueada por comerciante", "Comerciante no disponible", "En curso", "Lista para terminar", "Excluida", "Fallo reiniciable", "Caducada", "Pendiente", "Fallo reiniciable", "Disponible después", "Requisitos efectivos", "Disponible después", "Bloqueos actuales", "Exclusión mutua", "Ramas alternativas", "Requisitos previos", "Sucesoras directas"],
        ["es-mx"] = ["Requiere misión anterior", "Requisito de nivel", "Requisito de comerciante", "Fallida / excluida", "Ruta de Coleccionista", "Ruta de Lightkeeper", "Requiere éxito", "Requiere fallo", "Requiere inicio", "Cualquier resultado", "Bloqueada por misión previa", "Bloqueada por nivel", "Bloqueada por comerciante", "Comerciante no disponible", "En curso", "Lista para terminar", "Excluida", "Fallo reiniciable", "Caducada", "Pendiente", "Fallo reiniciable", "Disponible después", "Requisitos efectivos", "Disponible después", "Bloqueos actuales", "Exclusión mutua", "Ramas alternativas", "Requisitos previos", "Sucesoras directas"],
        ["fr"] = ["Quête précédente requise", "Niveau requis", "Exigence du marchand", "Échouée / exclue", "Voie du Collectionneur", "Voie de Lightkeeper", "Nécessite la réussite", "Nécessite l'échec", "Nécessite le démarrage", "N'importe quel résultat", "Bloquée par une quête préalable", "Bloquée par le niveau", "Bloquée par le marchand", "Marchand indisponible", "En cours", "Prête à terminer", "Exclue", "Échec recommençable", "Expirée", "En attente", "Échec recommençable", "Disponible après", "Conditions effectives", "Disponible après", "Blocages actuels", "Exclusion mutuelle", "Branches alternatives", "Prérequis", "Successeurs directs"],
        ["ge"] = ["Vorheriger Auftrag erforderlich", "Stufenanforderung", "Händleranforderung", "Fehlgeschlagen / ausgeschlossen", "Sammler-Pfad", "Lightkeeper-Pfad", "Erfordert Erfolg", "Erfordert Fehlschlag", "Erfordert Start", "Beliebiger Ausgang", "Vorauftrag erforderlich", "Stufe erforderlich", "Händlerstufe erforderlich", "Händler nicht verfügbar", "In Bearbeitung", "Abschlussbereit", "Ausgeschlossen", "Wiederholbarer Fehlschlag", "Abgelaufen", "Ausstehend", "Wiederholbarer Fehlschlag", "Verfügbar nach", "Wirksame Anforderungen", "Verfügbar nach", "Aktuelle Hindernisse", "Gegenseitiger Ausschluss", "Alternative Zweige", "Voraussetzungen", "Direkte Folgeaufträge"],
        ["hu"] = ["Előző küldetés szükséges", "Szintkövetelmény", "Kereskedői követelmény", "Sikertelen / kizárt", "Gyűjtő útvonal", "Lightkeeper útvonal", "Siker szükséges", "Kudarc szükséges", "Kezdés szükséges", "Bármely eredmény", "Előfeltétel blokkolja", "Szint blokkolja", "Kereskedő blokkolja", "Kereskedő nem elérhető", "Folyamatban", "Befejezhető", "Kizárt", "Újrakezdhető kudarc", "Lejárt", "Függőben", "Újrakezdhető kudarc", "Elérhető ezután", "Tényleges követelmények", "Elérhető ezután", "Jelenlegi akadályok", "Kölcsönös kizárás", "Alternatív ágak", "Előfeltételek", "Közvetlen utódok"],
        ["it"] = ["Missione precedente richiesta", "Requisito di livello", "Requisito del commerciante", "Fallita / esclusa", "Percorso Collezionista", "Percorso Lightkeeper", "Richiede successo", "Richiede fallimento", "Richiede avvio", "Qualsiasi esito", "Bloccata da missione precedente", "Bloccata dal livello", "Bloccata dal commerciante", "Commerciante non disponibile", "In corso", "Pronta da completare", "Esclusa", "Fallimento riavviabile", "Scaduta", "In attesa", "Fallimento riavviabile", "Disponibile dopo", "Requisiti effettivi", "Disponibile dopo", "Blocchi attuali", "Esclusione reciproca", "Rami alternativi", "Prerequisiti", "Successori diretti"],
        ["jp"] = ["前提クエスト", "レベル要件", "トレーダー要件", "失敗 / 除外", "コレクタールート", "Lightkeeper ルート", "成功が必要", "失敗が必要", "開始が必要", "いずれかの結果", "前提クエストでロック", "レベルでロック", "トレーダーでロック", "トレーダー利用不可", "進行中", "完了可能", "除外済み", "再開可能な失敗", "期限切れ", "保留中", "再開可能な失敗", "指定時刻後に利用可能", "有効な要件", "利用可能になる時刻", "現在の障害", "相互排他", "代替分岐", "前提条件", "直接の後続クエスト"],
        ["kr"] = ["선행 퀘스트 필요", "레벨 요구 사항", "상인 요구 사항", "실패 / 제외", "수집가 경로", "Lightkeeper 경로", "성공 필요", "실패 필요", "시작 필요", "어느 결과든", "선행 퀘스트로 잠김", "레벨로 잠김", "상인으로 잠김", "상인 이용 불가", "진행 중", "완료 가능", "제외됨", "재시작 가능한 실패", "만료됨", "대기 중", "재시작 가능한 실패", "이후 이용 가능", "실제 요구 사항", "이후 이용 가능", "현재 방해 요소", "상호 배제", "대체 분기", "선행 조건", "직접 후속 퀘스트"],
        ["pl"] = ["Wymagane poprzednie zadanie", "Wymagany poziom", "Wymaganie handlarza", "Nieudane / wykluczone", "Ścieżka Kolekcjonera", "Ścieżka Lightkeepera", "Wymaga sukcesu", "Wymaga porażki", "Wymaga rozpoczęcia", "Dowolny wynik", "Zablokowane przez poprzednie zadanie", "Zablokowane przez poziom", "Zablokowane przez handlarza", "Handlarz niedostępny", "W toku", "Gotowe do ukończenia", "Wykluczone", "Porażka z możliwością restartu", "Wygasło", "Oczekuje", "Porażka z możliwością restartu", "Dostępne po", "Obowiązujące wymagania", "Dostępne po", "Bieżące blokady", "Wzajemne wykluczenie", "Alternatywne gałęzie", "Wymagania wstępne", "Bezpośrednie następniki"],
        ["po"] = ["Missão anterior necessária", "Requisito de nível", "Requisito do comerciante", "Falhada / excluída", "Rota do Colecionador", "Rota do Lightkeeper", "Requer sucesso", "Requer falha", "Requer início", "Qualquer resultado", "Bloqueada por missão anterior", "Bloqueada por nível", "Bloqueada por comerciante", "Comerciante indisponível", "Em progresso", "Pronta para concluir", "Excluída", "Falha reiniciável", "Expirada", "Pendente", "Falha reiniciável", "Disponível depois", "Requisitos efetivos", "Disponível depois", "Bloqueios atuais", "Exclusão mútua", "Ramificações alternativas", "Pré-requisitos", "Sucessoras diretas"],
        ["ro"] = ["Misiune anterioară necesară", "Cerință de nivel", "Cerință comerciant", "Eșuată / exclusă", "Ruta Colecționarului", "Ruta Lightkeeper", "Necesită succes", "Necesită eșec", "Necesită începere", "Orice rezultat", "Blocată de misiunea anterioară", "Blocată de nivel", "Blocată de comerciant", "Comerciant indisponibil", "În desfășurare", "Gata de finalizat", "Exclusă", "Eșec repornibil", "Expirată", "În așteptare", "Eșec repornibil", "Disponibilă după", "Cerințe efective", "Disponibilă după", "Blocaje actuale", "Excludere reciprocă", "Ramuri alternative", "Cerințe preliminare", "Succesoare directe"],
        ["ru"] = ["Требуется предыдущее задание", "Требование уровня", "Требование торговца", "Провалено / исключено", "Путь Коллекционера", "Путь Lightkeeper", "Требуется успех", "Требуется провал", "Требуется начало", "Любой исход", "Заблокировано предыдущим заданием", "Заблокировано уровнем", "Заблокировано торговцем", "Торговец недоступен", "Выполняется", "Готово к завершению", "Исключено", "Перезапускаемый провал", "Истекло", "Ожидает", "Перезапускаемый провал", "Доступно после", "Действующие требования", "Доступно после", "Текущие препятствия", "Взаимоисключение", "Альтернативные ветви", "Предварительные условия", "Прямые последующие задания"],
        ["sk"] = ["Vyžaduje predchádzajúcu úlohu", "Požiadavka úrovne", "Požiadavka obchodníka", "Neúspešné / vylúčené", "Trasa Zberateľa", "Trasa Lightkeepera", "Vyžaduje úspech", "Vyžaduje neúspech", "Vyžaduje začatie", "Ľubovoľný výsledok", "Blokované predchádzajúcou úlohou", "Blokované úrovňou", "Blokované obchodníkom", "Obchodník nedostupný", "Prebieha", "Pripravené na dokončenie", "Vylúčené", "Opakovateľný neúspech", "Vypršalo", "Čaká", "Opakovateľný neúspech", "Dostupné po", "Platné požiadavky", "Dostupné po", "Aktuálne prekážky", "Vzájomné vylúčenie", "Alternatívne vetvy", "Predpoklady", "Priame následné úlohy"],
        ["tu"] = ["Önceki görev gerekli", "Seviye gereksinimi", "Tüccar gereksinimi", "Başarısız / hariç", "Koleksiyoncu rotası", "Lightkeeper rotası", "Başarı gerekli", "Başarısızlık gerekli", "Başlatma gerekli", "Herhangi bir sonuç", "Önceki görev nedeniyle kilitli", "Seviye nedeniyle kilitli", "Tüccar nedeniyle kilitli", "Tüccar kullanılamıyor", "Devam ediyor", "Bitirmeye hazır", "Hariç tutuldu", "Yeniden başlatılabilir başarısızlık", "Süresi doldu", "Beklemede", "Yeniden başlatılabilir başarısızlık", "Şundan sonra kullanılabilir", "Geçerli gereksinimler", "Şundan sonra kullanılabilir", "Mevcut engeller", "Karşılıklı dışlama", "Alternatif dallar", "Ön koşullar", "Doğrudan ardıllar"],
    };

    private static readonly IReadOnlyDictionary<string, string> SptGlobalKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["profile.level"] = "ArenaBattlePass/LevelContainer",
        ["refresh"] = "Arena/CustomGames/toggle/Refresh",
        ["search.label"] = "Search",
        ["trader.label"] = "Merchant",
        ["state.Locked"] = "APC/Locked",
        ["state.Available"] = "ClothingItem/Purchase",
        ["state.InProgress"] = "QuestStatusStarted",
        ["state.ReadyToFinish"] = "QuestStatusAvailableForFinish",
        ["state.Completed"] = "QuestStatusSuccess",
        ["state.Failed"] = "EEventState/SummonFailed",
        ["state.AvailableForStart"] = "QuestStatusAvailableForStart",
        ["state.Started"] = "QuestStatusStarted",
        ["state.AvailableForFinish"] = "QuestStatusAvailableForFinish",
        ["state.Success"] = "EEventState/SummonSuccess",
        ["state.Fail"] = "EEventState/SummonFailed",
        ["state.MarkedAsFailed"] = "Queststatusmarkedasfailed",
        ["route.collector"] = "5c51aac186f77432ea65c552 name",
        ["route.lightkeeper"] = "638f541a29ffd1183d187f57 Nickname",
        ["details.trader"] = "Merchant",
        ["details.location"] = "Arena/CustomGames/toggle/map",
        ["details.expand"] = "expand",
        ["details.collapse"] = "collapse",
        ["details.objectives"] = "Achievements/Tooltip/Conditions",
        ["details.rewards"] = "Achievements/Tooltip/Task rewards",
        ["reward.unknown"] = "Unknown reward",
        ["reward.experience"] = "GAINED EXPERIENCE",
        ["reward.skill"] = "arena/career/tabs/skills",
        ["reward.traderStanding"] = "arena/popup/quests/standing",
        ["reward.unlock"] = "Button/UNLOCK",
        ["reward.production"] = "PRODUCTION:",
        ["reward.achievement"] = "Achievements",
        ["reward.pockets"] = "Pockets",
        ["reward.customization"] = "CLOTHING UNLOCK",
        ["location.any"] = "QuestCondition/SurviveOnLocation/Any",
        ["trader.unlocked"] = "APC/Unlocked",
        ["trader.unavailable"] = "Arena/Widgets/refill container cooldown",
        ["gates.loyaltyShort"] = "LL",
        ["common.none"] = "BotAmount/NoBots",
        ["common.trader"] = "Merchant",
    };

    private static readonly IReadOnlyDictionary<string, string> GermanOverrides = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["controls.aria"] = "QuestMap-Steuerelemente",
        ["profile.label"] = "Profil",
        ["profile.aria"] = "Ausgewähltes Profil",
        ["profile.unnamed"] = "Unbenanntes Profil",
        ["profile.unknownSide"] = "Unbekannt",
        ["profile.level"] = "Stufe",
        ["refresh"] = "Aktualisieren",
        ["showAllFuture"] = "Alle zukünftigen anzeigen",
        ["hideFinished"] = "Abgeschlossene ausblenden",
        ["showFinished"] = "Abgeschlossene anzeigen",
        ["levelEligible"] = "Nur stufengerechte Aufträge anzeigen",
        ["inProgress.title"] = "Laufende Aufträge",
        ["trader.allShort"] = "Alle",
        ["search.label"] = "Suchen",
        ["search.placeholder"] = "Auftrag, Händler oder ID",
        ["trader.label"] = "Händler",
        ["trader.all"] = "Alle Händler",
        ["language.label"] = "Sprache",
        ["language.aria"] = "QuestMap-Sprache",
        ["graph.aria"] = "Interaktiver Auftrags-Abhängigkeitsgraph",
        ["graph.canvasAria"] = "Auftragsgraph. Mit dem Mausrad zoomen und den Hintergrund zum Verschieben ziehen.",
        ["graph.actionsAria"] = "Ansichtssteuerung",
        ["zoom.in"] = "Vergrößern",
        ["zoom.out"] = "Verkleinern",
        ["fit"] = "Einpassen",
        ["focus"] = "Kette fokussieren",
        ["focus.clear"] = "Fokus aufheben",
        ["selection.clear"] = "Auswahl aufheben",
        ["loading.topology"] = "Auftragsstruktur wird geladen…",
        ["loading.profile"] = "Auftragsstatus des Profils wird geladen…",
        ["loading.refresh"] = "Serverstruktur und Profilstatus werden aktualisiert…",
        ["loading.initial"] = "Auftragsstruktur und Profile werden geladen…",
        ["profiles.none"] = "Derzeit sind keine gültigen Profile geladen.",
        ["legend.aria"] = "Legende der Auftragsstatus",
        ["legend.states"] = "Status",
        ["legend.priorGate"] = "Vorheriger Auftrag erforderlich",
        ["legend.levelGate"] = "Stufenanforderung",
        ["legend.traderRequirement"] = "Händleranforderung",
        ["legend.futureTier"] = "Zukünftige Stufe",
        ["legend.failedExcluded"] = "Fehlgeschlagen / ausgeschlossen",
        ["legend.collectorRoute"] = "Sammler-Pfad",
        ["legend.lightkeeperRoute"] = "Lightkeeper-Pfad",
        ["legend.requiresSuccess"] = "Erfordert Erfolg",
        ["legend.requiresFailure"] = "Erfordert Fehlschlag",
        ["legend.requiresStarted"] = "Erfordert Start",
        ["legend.requiresOutcome"] = "Beliebiger Ausgang",
        ["state.Locked"] = "Gesperrt",
        ["state.PrerequisiteGated"] = "Vorauftrag erforderlich",
        ["state.LevelGated"] = "Stufe erforderlich",
        ["state.TraderGated"] = "Händlerstufe erforderlich",
        ["state.TraderUnavailable"] = "Händler nicht verfügbar",
        ["state.Available"] = "Verfügbar",
        ["state.InProgress"] = "In Bearbeitung",
        ["state.ReadyToFinish"] = "Abschlussbereit",
        ["state.Completed"] = "Abgeschlossen",
        ["state.Failed"] = "Fehlgeschlagen",
        ["state.Excluded"] = "Ausgeschlossen",
        ["state.RestartableFailure"] = "Wiederholbarer Fehlschlag",
        ["state.Expired"] = "Abgelaufen",
        ["state.Pending"] = "Ausstehend",
        ["state.AvailableForStart"] = "Zum Start verfügbar",
        ["state.Started"] = "Gestartet",
        ["state.AvailableForFinish"] = "Zum Abschluss verfügbar",
        ["state.Success"] = "Erfolgreich",
        ["state.Fail"] = "Fehlgeschlagen",
        ["state.FailRestartable"] = "Wiederholbarer Fehlschlag",
        ["state.MarkedAsFailed"] = "Als fehlgeschlagen markiert",
        ["state.AvailableAfter"] = "Verfügbar nach",
        ["route.collector"] = "Sammler",
        ["route.lightkeeper"] = "Lightkeeper",
        ["details.aria"] = "Details des ausgewählten Auftrags",
        ["details.selectTitle"] = "Auftrag auswählen",
        ["details.selectHelp"] = "Klicke einen Knoten an, um Bedingungen, Verzweigungen und geordnete Ziele zu sehen.",
        ["details.trader"] = "Händler",
        ["details.location"] = "Ort",
        ["details.expand"] = "Erweitern",
        ["details.collapse"] = "Einklappen",
        ["details.effectiveGates"] = "Wirksame Anforderungen",
        ["details.availableAfter"] = "Verfügbar nach",
        ["details.currentBlockers"] = "Aktuelle Hindernisse",
        ["details.mutualExclusion"] = "Gegenseitiger Ausschluss",
        ["details.branchAlternatives"] = "Alternative Zweige",
        ["details.objectives"] = "Ziele",
        ["details.rewards"] = "Belohnungen",
        ["details.prerequisites"] = "Voraussetzungen",
        ["details.successors"] = "Direkte Folgeaufträge",
        ["chip.faction"] = "Fraktion: {value}",
        ["chip.map"] = "Karte: {value}",
        ["chip.sptStatus"] = "SPT: {value}",
        ["location.any"] = "Beliebiger Ort",
        ["faction.any"] = "Beliebige Fraktion",
        ["season.Christmas"] = "Weihnachten",
        ["season.Halloween"] = "Halloween",
        ["season.None"] = "Kein Ereignis",
        ["season.NewYears"] = "Neujahr",
        ["season.Promo"] = "Werbeaktion",
        ["season.AprilFools"] = "Aprilscherz",
        ["trader.unlocked"] = "freigeschaltet",
        ["trader.unavailable"] = "nicht verfügbar",
        ["trader.noState"] = "{trader} · kein Händlerstatus im Profil",
        ["trader.summary"] = "{trader} · {availability} · LL{loyalty} · Ruf {standing}",
        ["gates.none"] = "Keine übernommene Stufen-, Loyalitäts- oder Rufanforderung.",
        ["gates.pmcLevel"] = "PMC-Stufe {compare} {value}",
        ["gates.reputation"] = "{trader} {kind} {compare} {value}",
        ["gates.loyaltyShort"] = "LL",
        ["gates.reputationShort"] = "Ruf",
        ["objectives.none"] = "Die Auftragsvorlage enthält keine Zieldefinitionen.",
        ["objectives.required"] = "Benötigt: {value}",
        ["objectives.progressRecorded"] = "Fortschritt erfasst",
        ["reward.unknown"] = "Unbekannte Belohnung",
        ["reward.experience"] = "Erfahrung",
        ["reward.skill"] = "Fertigkeit",
        ["reward.traderStanding"] = "Händlerruf",
        ["reward.unlock"] = "Freischaltung",
        ["reward.production"] = "Herstellung:",
        ["reward.achievement"] = "Erfolg",
        ["reward.pockets"] = "Taschen",
        ["reward.customization"] = "Kleidung freigeschaltet",
        ["blocker.prerequisite"] = "{quest} muss den Status {statuses} haben",
        ["blocker.traderUnavailable"] = "{trader} ist nicht verfügbar",
        ["common.or"] = " oder ",
        ["common.none"] = "Keine",
        ["common.unknownQuest"] = "Unbekannter Auftrag",
        ["common.trader"] = "Händler",
        ["exclusion.prefix"] = "Ausgeschlossen durch ",
        ["exclusion.permanent"] = "dauerhaft",
        ["time.seconds"] = "{value} Sekunden",
        ["metrics.summary"] = "{visible}/{applicable} Aufträge · {edges} Kanten · Anordnung {layout} ms · Status {overlay} ms · Darstellung {render} ms",
        ["metrics.overview"] = " · Übersichtsmodus",
        ["error.profileNotFound"] = "Das ausgewählte Profil ist nicht geladen oder ungültig.",
    };

    internal static IReadOnlyDictionary<string, string> For(string language, IReadOnlyDictionary<string, string> sptLocale)
    {
        var result = English.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (catalogKey, localeKey) in SptGlobalKeys)
            {
                if (sptLocale.TryGetValue(localeKey, out var localized) && !string.IsNullOrWhiteSpace(localized)) result[catalogKey] = localized;
            }
        }

        if (CoreOverrides.TryGetValue(language, out var overrides))
        {
            if (overrides.Length != CoreOverrideKeys.Length) throw new InvalidOperationException($"QuestMap locale '{language}' has {overrides.Length} core strings; expected {CoreOverrideKeys.Length}.");
            for (var index = 0; index < CoreOverrideKeys.Length; index++) result[CoreOverrideKeys[index]] = overrides[index];
        }

        if (ExtendedOverrides.TryGetValue(language, out var extendedOverrides))
        {
            if (extendedOverrides.Length != ExtendedOverrideKeys.Length) throw new InvalidOperationException($"QuestMap locale '{language}' has {extendedOverrides.Length} extended strings; expected {ExtendedOverrideKeys.Length}.");
            for (var index = 0; index < ExtendedOverrideKeys.Length; index++) result[ExtendedOverrideKeys[index]] = extendedOverrides[index];
        }

        if (string.Equals(language, "ge", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (key, value) in GermanOverrides) result[key] = value;
        }

        // Product names are identifiers, not localizable UI prose.
        result["app.title"] = "SPT QuestMap";
        result["app.heading"] = "QuestMap";

        return result;
    }

    internal static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app.title"] = "SPT Quest Map",
        ["app.heading"] = "Quest Map",
        ["controls.aria"] = "Quest map controls",
        ["profile.label"] = "Profile",
        ["profile.aria"] = "Selected profile",
        ["profile.unnamed"] = "Unnamed profile",
        ["profile.unknownSide"] = "Unknown",
        ["profile.level"] = "level",
        ["refresh"] = "Refresh",
        ["showAllFuture"] = "Show All Future Quests",
        ["hideFinished"] = "Hide finished",
        ["showFinished"] = "Show Finished",
        ["levelEligible"] = "Show only level eligible quests",
        ["inProgress.title"] = "Quests In Progress",
        ["trader.allShort"] = "All",
        ["search.label"] = "Search",
        ["search.placeholder"] = "Quest, trader, or ID",
        ["trader.label"] = "Trader",
        ["trader.all"] = "All traders",
        ["language.label"] = "Language",
        ["language.aria"] = "Quest map language",
        ["graph.aria"] = "Interactive quest dependency graph",
        ["graph.canvasAria"] = "Quest graph. Use the mouse wheel to zoom and drag the background to pan.",
        ["graph.actionsAria"] = "Viewport controls",
        ["zoom.in"] = "Zoom in",
        ["zoom.out"] = "Zoom out",
        ["fit"] = "Fit",
        ["focus"] = "Focus chain",
        ["focus.clear"] = "Clear focus",
        ["selection.clear"] = "Clear selection",
        ["loading.topology"] = "Loading quest topology…",
        ["loading.profile"] = "Loading profile quest state…",
        ["loading.refresh"] = "Refreshing server topology and profile state…",
        ["loading.initial"] = "Loading quest topology and profiles…",
        ["profiles.none"] = "No valid profiles are currently loaded.",
        ["legend.aria"] = "Quest status legend",
        ["legend.states"] = "States",
        ["legend.priorGate"] = "Prior quest gate",
        ["legend.levelGate"] = "Level gate",
        ["legend.traderRequirement"] = "Trader requirement",
        ["legend.futureTier"] = "Future tier",
        ["legend.failedExcluded"] = "Failed / excluded",
        ["legend.collectorRoute"] = "Collector route",
        ["legend.lightkeeperRoute"] = "Lightkeeper route",
        ["legend.requiresSuccess"] = "Requires success",
        ["legend.requiresFailure"] = "Requires failure",
        ["legend.requiresStarted"] = "Requires started",
        ["legend.requiresOutcome"] = "Either outcome",
        ["state.Locked"] = "Locked",
        ["state.PrerequisiteGated"] = "Prerequisite gated",
        ["state.LevelGated"] = "Level gated",
        ["state.TraderGated"] = "Trader gated",
        ["state.TraderUnavailable"] = "Trader unavailable",
        ["state.Available"] = "Available",
        ["state.InProgress"] = "In progress",
        ["state.ReadyToFinish"] = "Ready to finish",
        ["state.Completed"] = "Completed",
        ["state.Failed"] = "Failed",
        ["state.Excluded"] = "Excluded",
        ["state.RestartableFailure"] = "Restartable failure",
        ["state.Expired"] = "Expired",
        ["state.Pending"] = "Pending",
        ["state.AvailableForStart"] = "Available for start",
        ["state.Started"] = "Started",
        ["state.AvailableForFinish"] = "Available for finish",
        ["state.Success"] = "Success",
        ["state.Fail"] = "Failed",
        ["state.FailRestartable"] = "Restartable failure",
        ["state.MarkedAsFailed"] = "Marked as failed",
        ["state.AvailableAfter"] = "Available after",
        ["route.collector"] = "Collector",
        ["route.lightkeeper"] = "Lightkeeper",
        ["badge.event"] = "E",
        ["badge.branch"] = "B",
        ["details.aria"] = "Selected quest details",
        ["details.selectTitle"] = "Select a quest",
        ["details.selectHelp"] = "Click a node to inspect its gates, branch relationships, and ordered objectives.",
        ["details.trader"] = "Trader",
        ["details.location"] = "Location",
        ["details.expand"] = "Expand",
        ["details.collapse"] = "Collapse",
        ["details.effectiveGates"] = "Effective gates",
        ["details.availableAfter"] = "Available after",
        ["details.currentBlockers"] = "Current blockers",
        ["details.mutualExclusion"] = "Mutual exclusion",
        ["details.branchAlternatives"] = "Branch alternatives",
        ["details.objectives"] = "Objectives",
        ["details.rewards"] = "Rewards",
        ["details.prerequisites"] = "Prerequisites",
        ["details.successors"] = "Direct successors",
        ["chip.faction"] = "Faction: {value}",
        ["chip.map"] = "Map: {value}",
        ["chip.sptStatus"] = "SPT: {value}",
        ["location.any"] = "Any location",
        ["faction.any"] = "Any faction",
        ["season.Christmas"] = "Christmas",
        ["season.Halloween"] = "Halloween",
        ["season.None"] = "No event",
        ["season.NewYears"] = "New Year",
        ["season.Promo"] = "Promotion",
        ["season.AprilFools"] = "April Fools",
        ["trader.unlocked"] = "unlocked",
        ["trader.unavailable"] = "unavailable",
        ["trader.noState"] = "{trader} · no profile trader state",
        ["trader.summary"] = "{trader} · {availability} · LL{loyalty} · rep {standing}",
        ["gates.none"] = "No inherited level, loyalty, or standing gate.",
        ["gates.pmcLevel"] = "PMC level {compare} {value}",
        ["gates.reputation"] = "{trader} {kind} {compare} {value}",
        ["gates.loyaltyShort"] = "LL",
        ["gates.reputationShort"] = "rep",
        ["objectives.none"] = "No objective definitions were supplied by the quest template.",
        ["objectives.required"] = "Required: {value}",
        ["objectives.progressRecorded"] = "Progress recorded",
        ["reward.unknown"] = "Unknown reward",
        ["reward.experience"] = "Experience",
        ["reward.skill"] = "Skill",
        ["reward.traderStanding"] = "Trader standing",
        ["reward.unlock"] = "Unlock",
        ["reward.production"] = "Production:",
        ["reward.achievement"] = "Achievement",
        ["reward.pockets"] = "Pockets",
        ["reward.customization"] = "Customization unlock",
        ["blocker.prerequisite"] = "{quest} must be {statuses}",
        ["blocker.traderUnavailable"] = "{trader} is unavailable",
        ["common.or"] = " or ",
        ["common.none"] = "None",
        ["common.unknownQuest"] = "Unknown quest",
        ["common.trader"] = "Trader",
        ["exclusion.prefix"] = "Excluded by ",
        ["exclusion.permanent"] = "permanent",
        ["time.seconds"] = "{value} seconds",
        ["metrics.summary"] = "{visible}/{applicable} quests · {edges} edges · layout {layout} ms · overlay {overlay} ms · render {render} ms",
        ["metrics.overview"] = " · overview mode",
        ["error.profileNotFound"] = "The selected profile is not loaded or is invalid.",
    };
}
