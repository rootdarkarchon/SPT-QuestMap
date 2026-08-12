using System.Security.Cryptography;
using System.Text;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

internal sealed class QuestTopologyBuilder(
    DatabaseService databaseService,
    LocaleService localeService,
    QuestHelper questHelper,
    SeasonalEventService seasonalEventService,
    QuestConfig questConfig,
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
        var locale = localeService.GetLocaleDb(language);
        var dbQuests = databaseService.GetQuests().Values.OrderBy(quest => quest.Id.ToString(), StringComparer.Ordinal).ToArray();
        var items = databaseService.GetItems();
        var traders = databaseService.GetTraders();
        var locationValues = databaseService
            .GetLocations()
            .GetDictionary()
            .Values
            .Where(location => location?.Base?.Id is not null)
            .ToArray();
        var locationsById = QuestTemplateMapper.BuildLocationLookup(locationValues, questConfig.LocationIdMap);
        var canonicalMapIdsByAlias = locationsById.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Base.Id,
            StringComparer.OrdinalIgnoreCase);
        var questItemSpawnMapIds = QuestTemplateMapper.BuildQuestItemSpawnMapLookup(locationValues, items);
        var edges = new List<QuestEdgeDto>();
        var nodes = new List<QuestNodeDto>(dbQuests.Length);

        foreach (var quest in dbQuests)
        {
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
                .Where(condition => condition.ConditionType is not ("Quest" or "Level" or "PrestigeLevel" or "TraderLoyalty" or "TraderStanding"))
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
            var objectives = QuestTemplateMapper.ResolveObjectiveMaps(
                QuestTemplateMapper.OrderObjectives(
                    finishConditions,
                    locale,
                    duplicateId => logger.Warning($"SPT-QuestMap: duplicate objective condition ID '{duplicateId}' on quest {questId}; keeping its first definition.")),
                zoneMapCatalog,
                conditionsById,
                questItemSpawnMapIds,
                canonicalMapIdsByAlias);
            var actualMaps = QuestTemplateMapper.BuildActualMaps(nativeLocation, objectives, locale, locationsById);

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
                ActualMaps = actualMaps.Maps,
                ActualMapsComplete = actualMaps.Complete,
                UnknownConditions = unknownConditions,
                Summary = summaryCatalog.Get(questId, quest.TraderId.ToString(), language),
                WikiUrl = metaInfo?.WikiUrl,
                RelevantItems = metaInfo?.RelevantItems ?? [],
            });
        }

        nodes = QuestGraphRules.PropagateSeasonalEventTypes(nodes, edges);
        var effective = nodes.ToDictionary(node => node.Id, node => node.DirectRequirements, StringComparer.Ordinal);
        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            foreach (var edge in edges)
            {
                if (!effective.TryGetValue(edge.SourceId, out var parent)
                    || !effective.TryGetValue(edge.TargetId, out var child))
                {
                    continue;
                }

                var merged = QuestGraphRules.MergeRequirements(child.Concat(parent));
                if (!merged.SequenceEqual(child))
                {
                    effective[edge.TargetId] = merged;
                    changed = true;
                }
            }

            if (!changed) break;
        }

        nodes = nodes.Select(node => node with { EffectiveRequirements = effective[node.Id] }).ToList();
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

        var canonical = string.Join('\n', nodes.Select(node => $"{node.Id}:{node.ActualMapsComplete}:{string.Join(',', node.ActualMaps.Select(map => map.Id))}:{string.Join(';', node.Objectives.Select(objective => $"{objective.Id}={string.Join(',', objective.MapIds)}!{string.Join(',', objective.UnresolvedZoneIds)}"))}").Concat(edges
            .OrderBy(edge => edge.SourceId)
            .ThenBy(edge => edge.TargetId)
            .Select(edge => $"{edge.SourceId}>{edge.TargetId}:{string.Join(',', edge.RequiredStatuses)}")));
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16].ToLowerInvariant();
        var traderCatalog = traders
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new QuestTraderDto(
                pair.Key.ToString(),
                QuestTemplateMapper.Localize(locale, $"{pair.Key} Nickname", pair.Value.Base.Nickname ?? pair.Value.Base.Name ?? pair.Key.ToString()),
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
