using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

internal sealed class QuestTopologyBuilder(
    DatabaseService databaseService,
    LocaleService localeService,
    QuestHelper questHelper,
    SeasonalEventService seasonalEventService,
    QuestMapTopologyPreload preload,
    QuestSummaryCatalog summaryCatalog,
    QuestMetaInfoCatalog metaInfoCatalog,
    QuestZoneMapCatalog zoneMapCatalog,
    ISptLogger<QuestMapDataService> logger
)
{
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, QuestTopologyDto> _cache = new(StringComparer.OrdinalIgnoreCase);

    internal QuestTopologyDto Get(string language)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(language, out var topology)) return topology;
            topology = Build(language);
            _cache[language] = topology;
            return topology;
        }
    }

    private QuestTopologyDto Build(string language)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var stageStopwatch = Stopwatch.StartNew();
        var locale = localeService.GetLocaleDb(language);
        var ui = QuestMapUiCatalog.For(language, locale);
        var dbQuests = databaseService.GetQuests().Values.OrderBy(quest => quest.Id.ToString(), StringComparer.Ordinal).ToArray();
        var items = preload.Items;
        var traders = databaseService.GetTraders();
        var locationValues = preload.Locations;
        var locationsById = QuestTemplateMapper.BuildLocationLookup(locationValues);
        var canonicalMapIdsByAlias = QuestTemplateMapper.BuildCanonicalMapIdLookup(locationsById);
        var questItemSpawnMapIds = preload.QuestItemSpawnMapIds;
        var mapAliases = QuestTemplateMapper.BuildMapAliases(locationValues);
        var edges = new List<QuestEdgeDto>();
        var nodes = new List<QuestNodeDto>(dbQuests.Length);
        var snapshotMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        var slowestQuestId = string.Empty;
        var slowestQuestMilliseconds = 0d;
        var questStopwatch = new Stopwatch();

        foreach (var quest in dbQuests)
        {
            questStopwatch.Restart();
            var questId = quest.Id.ToString();
            var metaInfo = metaInfoCatalog.Get(questId, locale);
            var startConditions = quest.Conditions?.AvailableForStart ?? [];
            var requirements = startConditions
                .Where(condition => condition.ConditionType is "Level" or "PrestigeLevel" or "TraderLoyalty" or "TraderStanding")
                .Select(QuestTemplateMapper.ToRequirement)
                .Where(requirement => requirement is not null)
                .Cast<RequirementDto>()
                .ToArray();
            var unknownConditions = startConditions
                .Where(condition => !IsSupportedStartConditionType(condition.ConditionType))
                .Select(condition => new UnknownConditionDto(
                    "AvailableForStart",
                    condition.ConditionType,
                    condition.Id.ToString()))
                .OrderBy(condition => condition.ConditionType, StringComparer.Ordinal)
                .ThenBy(condition => condition.ConditionId, StringComparer.Ordinal)
                .ToArray();

            foreach (var condition in startConditions.Where(condition => condition.ConditionType == "Quest"))
            {
                foreach (var target in QuestTemplateMapper.GetTargets(condition))
                {
                    edges.Add(new QuestEdgeDto(
                        target,
                        questId,
                        (condition.Status ?? []).Select(status => status.ToString()).Order(StringComparer.Ordinal).ToArray(),
                        condition.AvailableAfter ?? 0
                    ));
                }
            }

            foreach (var unsupported in unknownConditions.Select(condition => condition.ConditionType).Distinct(StringComparer.Ordinal))
            {
                logger.Warning($"SPT-QuestMap: unsupported start condition '{unsupported}' on quest {questId}; availability remains authoritative but the blocker explanation may be incomplete.");
            }

            traders.TryGetValue(quest.TraderId, out var trader);
            var exclusions = (quest.Conditions?.Fail ?? [])
                .Where(condition => condition.ConditionType == "Quest")
                .SelectMany(condition => QuestTemplateMapper.GetTargets(condition)
                    .Select(target => new QuestExclusionRuleDto(
                        target,
                        (condition.Status ?? []).Select(status => status.ToString()).ToArray()
                    )))
                .ToArray();

            var nativeLocation = QuestTemplateMapper.BuildLocation(quest.Location, locale, locationsById);
            var finishConditions = quest.Conditions?.AvailableForFinish ?? [];
            var conditionsById = finishConditions
                .GroupBy(condition => condition.Id.ToString(), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var objectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
                nativeLocation,
                QuestTemplateMapper.ResolveObjectiveMaps(
                    QuestTemplateMapper.OrderObjectives(
                        finishConditions,
                        locale,
                        duplicateId => logger.Warning($"SPT-QuestMap: duplicate objective condition ID '{duplicateId}' on quest {questId}; keeping its first definition.")),
                    zoneMapCatalog,
                    conditionsById,
                    questItemSpawnMapIds,
                    canonicalMapIdsByAlias),
                ui,
                locale,
                locationsById);
            var actualMaps = QuestTemplateMapper.BuildActualMaps(objectives);

            nodes.Add(new QuestNodeDto(
                questId,
                QuestTemplateMapper.Localize(locale, $"{questId} name", quest.QuestName ?? quest.Name ?? questId),
                QuestTemplateMapper.Localize(locale, $"{questId} description", string.Empty),
                quest.TraderId.ToString(),
                QuestTemplateMapper.Localize(locale, $"{quest.TraderId} Nickname", trader?.Base.Nickname ?? trader?.Base.Name ?? quest.TraderId.ToString()),
                trader?.Base.Avatar,
                quest.Image,
                GetQuestFaction(quest.Id),
                nativeLocation,
                GetQuestSeason(quest.Id),
                quest.Restartable,
                requirements,
                [],
                objectives,
                exclusions,
                QuestTemplateMapper.BuildRewards(
                    quest.Rewards?.GetValueOrDefault(QuestStatusEnum.Success.ToString()) ?? [],
                    locale,
                    items,
                    traders
                )
            )
            {
                TaskLocation = QuestTemplateMapper.BuildTaskLocation(nativeLocation, objectives, ui),
                ActualMaps = actualMaps.Maps,
                ActualMapsComplete = actualMaps.Complete,
                UnknownConditions = unknownConditions,
                Summary = summaryCatalog.Get(questId, language),
                WikiUrl = metaInfo?.WikiUrl,
                RelevantItems = metaInfo?.RelevantItems ?? [],
            });
            questStopwatch.Stop();
            if (questStopwatch.Elapsed.TotalMilliseconds > slowestQuestMilliseconds)
            {
                slowestQuestId = questId;
                slowestQuestMilliseconds = questStopwatch.Elapsed.TotalMilliseconds;
            }
        }

        var nodesMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        nodes = QuestGraphRules.PropagateSeasonalEventTypes(nodes, edges);
        var seasonalMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        var effective = QuestGraphRules.PropagateEffectiveRequirements(nodes, edges);
        nodes = nodes.Select(node => node with { EffectiveRequirements = effective[node.Id] }).ToList();
        var requirementsMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        var questIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var collectorPathQuestIds = QuestGraphRules.BuildPrerequisiteClosure(QuestMapQuestIds.Collector, edges, questIds);
        if (collectorPathQuestIds.Count == 0)
        {
            logger.Warning($"SPT-QuestMap: Collector quest {QuestMapQuestIds.Collector} was not found; Collector-path markers are disabled.");
        }

        var lightkeeperPathQuestIds = QuestGraphRules.BuildPrerequisiteClosure(QuestMapQuestIds.KnockKnock, edges, questIds);
        if (lightkeeperPathQuestIds.Count == 0)
        {
            logger.Warning($"SPT-QuestMap: Knock-Knock quest {QuestMapQuestIds.KnockKnock} was not found; Lightkeeper-path markers and unlock evaluation are disabled.");
        }

        var canonical = string.Join('\n', nodes.Select(node => $"{node.Id}:{node.TaskLocation.Id}:{node.ActualMapsComplete}:{string.Join(',', node.ActualMaps.Select(map => map.Id))}:{string.Join(';', node.Objectives.Select(objective => $"{objective.Id}={objective.InRaidRelevant}:{string.Join(',', objective.TaskLocations.Select(map => map.Id))}:{string.Join(',', objective.MapIds)}!{string.Join(',', objective.UnresolvedZoneIds)}"))}").Concat(edges
            .OrderBy(edge => edge.SourceId)
            .ThenBy(edge => edge.TargetId)
            .Select(edge => $"{edge.SourceId}>{edge.TargetId}:{string.Join(',', edge.RequiredStatuses)}"))
            .Concat(mapAliases.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"map:{pair.Key}={string.Join(',', pair.Value)}")));
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
        var traderCatalog = traders
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new QuestTraderDto(
                pair.Key.ToString(),
                QuestTemplateMapper.Localize(locale, $"{pair.Key} Nickname", pair.Value.Base.Nickname ?? pair.Value.Base.Name ?? pair.Key.ToString()),
                pair.Value.Base.Avatar
            ))
            .ToArray();

        var finalizeMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        totalStopwatch.Stop();
        logger.Info(
            "QUESTMAP_M08_SERVER_TOPOLOGY_BUILD " +
            $"language={language}; quests={nodes.Count}; edges={edges.Count}; snapshotMs={snapshotMilliseconds:F2}; " +
            $"nodesMs={nodesMilliseconds:F2}; seasonalMs={seasonalMilliseconds:F2}; " +
            $"requirementsMs={requirementsMilliseconds:F2}; finalizeMs={finalizeMilliseconds:F2}; " +
            $"slowestQuestId={slowestQuestId}; slowestQuestMs={slowestQuestMilliseconds:F2}; " +
            $"totalMs={totalStopwatch.Elapsed.TotalMilliseconds:F2}");

        return new QuestTopologyDto(
            version,
            nodes,
            edges,
            traderCatalog,
            collectorPathQuestIds.Order(StringComparer.Ordinal).ToArray(),
            lightkeeperPathQuestIds.Order(StringComparer.Ordinal).ToArray()
        ) { MapAliases = mapAliases };
    }

    internal static bool IsSupportedStartConditionType(string? conditionType) => conditionType is
        "Quest" or "Level" or "PrestigeLevel" or "TraderLoyalty" or "TraderStanding" or "Block";

    private string GetQuestFaction(MongoId questId)
    {
        if (questHelper.QuestIsForOtherSide("BEAR", questId)) return "USEC";
        return questHelper.QuestIsForOtherSide("USEC", questId) ? "BEAR" : "Any";
    }

    private string? GetQuestSeason(MongoId questId)
    {
        foreach (var eventType in Enum.GetValues<SeasonalEventType>())
        {
            if (seasonalEventService.IsQuestRelatedToEvent(questId, eventType)) return eventType.ToString();
        }

        return null;
    }
}
