using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Extensions;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Services;

internal static class QuestTemplateMapper
{
    private const string ArenaInternalLocationId = "develop";
    private const string ArenaMongoLocationId = "56db0b3bd2720bb0678b4567";

    internal static QuestLocationDto BuildLocation(
        string? locationId,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<string, Location> locationsById
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
            Localize(locale, $"{locationId} Name", string.IsNullOrWhiteSpace(fallback) ? locationId : fallback),
            false,
            ToFileUrl(bannerPath)
        );
    }

    internal static Dictionary<string, Location> BuildLocationLookup(IEnumerable<Location> locations)
    {
        var result = new Dictionary<string, Location>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in locations)
        {
            foreach (var id in GetLocationIdentityIds(location)) result[id] = location;
        }

        return result;
    }

    internal static QuestMapReferenceDto[] BuildMapReferences(
        IEnumerable<string> mapIds,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<string, Location> locationsById
    ) => mapIds
        .Distinct(StringComparer.Ordinal)
        .Where(mapId => IsApplicableTaskMapId(mapId, locationsById))
        .Select(mapId =>
        {
            locationsById.TryGetValue(mapId, out var location);
            var fallback = location?.Base?.Name;
            var localeId = location?.Base?.IdField.ToString();
            var name = string.IsNullOrWhiteSpace(localeId)
                ? (string.IsNullOrWhiteSpace(fallback) ? mapId : fallback)
                : Localize(locale, $"{localeId} Name", string.IsNullOrWhiteSpace(fallback) ? mapId : fallback);
            return new QuestMapReferenceDto(
                mapId,
                name,
                ToFileUrl(location?.Base?.Banners?.FirstOrDefault()?.Picture?.Path));
        })
        .OrderBy(map => map.Name, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(map => map.Id, StringComparer.Ordinal)
        .ToArray();

    internal static ObjectiveDefinitionDto[] ResolveObjectiveMaps(
        IEnumerable<ObjectiveDefinitionDto> objectives,
        QuestZoneMapCatalog zoneMapCatalog,
        IReadOnlyDictionary<string, QuestCondition>? sourceConditionsById = null,
        IReadOnlyDictionary<string, string[]>? questItemSpawnMapIds = null,
        IReadOnlyDictionary<string, string>? canonicalMapIdsByAlias = null
    ) => objectives
        .Select(objective =>
        {
            var resolution = zoneMapCatalog.Resolve(objective.ZoneIds ?? []);
            var mapIds = resolution.MapIds.ToHashSet(StringComparer.Ordinal);
            if (sourceConditionsById?.TryGetValue(objective.Id, out var sourceCondition) == true)
            {
                foreach (var mapId in GetExplicitObjectiveMapIds(sourceCondition, canonicalMapIdsByAlias))
                {
                    mapIds.Add(CanonicalizeVariantMapId(mapId));
                }

                if (string.Equals(sourceCondition.ConditionType, "FindItem", StringComparison.Ordinal)
                    && questItemSpawnMapIds is not null)
                {
                    foreach (var targetId in GetTargets(sourceCondition))
                    {
                        if (!questItemSpawnMapIds.TryGetValue(targetId, out var spawnMapIds)) continue;
                        mapIds.UnionWith(spawnMapIds);
                    }
                }
            }

            return objective with
            {
                MapIds = mapIds.Order(StringComparer.Ordinal).ToArray(),
                UnresolvedZoneIds = resolution.UnresolvedZoneIds,
            };
        })
        .ToArray();

    internal static IReadOnlyDictionary<string, string[]> BuildQuestItemSpawnMapLookup(
        IEnumerable<Location> locations,
        IReadOnlyDictionary<MongoId, TemplateItem> items)
    {
        var questItemIds = items
            .Where(pair => pair.Value.IsQuestItem())
            .Select(pair => pair.Key.ToString())
            .ToHashSet(StringComparer.Ordinal);
        var mapIdsByItem = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var location in locations.Where(IsApplicableTaskMapLocation))
        {
            var mapId = location.Base?.Id;
            if (string.IsNullOrWhiteSpace(mapId)) continue;
            foreach (var spawnPoint in location.LooseLoot?.Value?.SpawnpointsForced ?? [])
            {
                foreach (var item in spawnPoint.Template?.Items ?? [])
                {
                    var itemId = item.Template.ToString();
                    if (!questItemIds.Contains(itemId)) continue;
                    if (!mapIdsByItem.TryGetValue(itemId, out var mapIds))
                    {
                        mapIds = new HashSet<string>(StringComparer.Ordinal);
                        mapIdsByItem[itemId] = mapIds;
                    }

                    mapIds.Add(CanonicalizeVariantMapId(mapId));
                }
            }
        }

        return mapIdsByItem.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    internal static ObjectiveDefinitionDto[] ClassifyObjectiveTaskLocations(
        QuestLocationDto nativeLocation,
        IEnumerable<ObjectiveDefinitionDto> objectives,
        IReadOnlyDictionary<string, string> ui,
        Dictionary<string, string> locale,
        IReadOnlyDictionary<string, Location> locationsById
    ) => objectives
        .Select(objective =>
        {
            var inRaidRelevant = QuestAutoTrackingRules.IsInRaidObjectiveType(objective.ConditionType);
            QuestMapReferenceDto[] taskLocations;
            if (!inRaidRelevant)
            {
                taskLocations = [BuildNoLocationReference(ui)];
            }
            else
            {
                var concreteMaps = BuildMapReferences(objective.MapIds, locale, locationsById);
                if (IsArenaLocation(nativeLocation.Id, nativeLocation.Name)
                    || (objective.MapIds.Length > 0 && concreteMaps.Length == 0))
                {
                    inRaidRelevant = false;
                    taskLocations = [];
                }
                else if (QuestObjectiveMapRules.IsTransitionLocation(
                        nativeLocation.Id, nativeLocation.Name, nativeLocation.Any))
                {
                    taskLocations = [BuildTransitionReference(nativeLocation), .. concreteMaps];
                }
                else if (concreteMaps.Length > 0)
                {
                    taskLocations = concreteMaps;
                }
                else if (nativeLocation.Any)
                {
                    taskLocations = [BuildAnyLocationReference(ui)];
                }
                else
                {
                    taskLocations =
                    [
                        new QuestMapReferenceDto(
                            nativeLocation.Id,
                            nativeLocation.Name ?? nativeLocation.Id,
                            nativeLocation.BannerImageUrl),
                    ];
                }
            }

            return objective with
            {
                InRaidRelevant = inRaidRelevant,
                TaskLocations = taskLocations
                    .DistinctBy(map => map.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };
        })
        .ToArray();

    internal static (QuestMapReferenceDto[] Maps, bool Complete) BuildActualMaps(
        IReadOnlyCollection<ObjectiveDefinitionDto> objectives
    )
    {
        var maps = objectives
            .SelectMany(objective => objective.TaskLocations)
            .Where(map => !IsSpecialTaskLocation(map.Id))
            .DistinctBy(map => map.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(map => map.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(map => map.Id, StringComparer.Ordinal)
            .ToArray();
        var inRaidObjectives = objectives.Where(objective => objective.InRaidRelevant).ToArray();
        var complete = inRaidObjectives.Length > 0
            && inRaidObjectives.All(objective => objective.TaskLocations.Length > 0
                && objective.UnresolvedZoneIds.Length == 0);
        return (maps, complete);
    }

    internal static QuestMapReferenceDto BuildTaskLocation(
        QuestLocationDto nativeLocation,
        IReadOnlyCollection<ObjectiveDefinitionDto> objectives,
        IReadOnlyDictionary<string, string> ui)
    {
        if (QuestObjectiveMapRules.IsTransitionLocation(
                nativeLocation.Id, nativeLocation.Name, nativeLocation.Any)
            && objectives.Any(objective => objective.InRaidRelevant))
        {
            return BuildTransitionReference(nativeLocation);
        }

        var concreteMap = objectives
            .SelectMany(objective => objective.TaskLocations)
            .Where(map => !IsSpecialTaskLocation(map.Id))
            .OrderBy(map => map.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(map => map.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (concreteMap is not null) return concreteMap;

        if (objectives.Any(objective => objective.TaskLocations.Any(map => map.Id.Equals(
                QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase))))
        {
            return BuildAnyLocationReference(ui);
        }

        return BuildNoLocationReference(ui);
    }

    private static bool IsSpecialTaskLocation(string id) =>
        id.Equals(QuestObjectiveMapRules.NoLocationFilterId, StringComparison.OrdinalIgnoreCase)
        || id.Equals(QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase)
        || id.Equals(QuestObjectiveMapRules.TransitionFilterId, StringComparison.OrdinalIgnoreCase);

    private static QuestMapReferenceDto BuildNoLocationReference(IReadOnlyDictionary<string, string> ui) => new(
        QuestObjectiveMapRules.NoLocationFilterId,
        ui.GetValueOrDefault("location.none") ?? "Out of Raid",
        QuestObjectiveMapRules.NoLocationBannerUrl);

    private static QuestMapReferenceDto BuildAnyLocationReference(IReadOnlyDictionary<string, string> ui) => new(
        QuestObjectiveMapRules.AnyFilterId,
        ui.GetValueOrDefault("location.any") ?? "Any location",
        QuestObjectiveMapRules.AnyBannerUrl);

    private static QuestMapReferenceDto BuildTransitionReference(QuestLocationDto nativeLocation) => new(
        QuestObjectiveMapRules.TransitionFilterId,
        nativeLocation.Name ?? "Transition",
        QuestObjectiveMapRules.TransitionBannerUrl);

    internal static string? ToFileUrl(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;
        var normalized = assetPath.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("files/", StringComparison.OrdinalIgnoreCase) ? $"/{normalized}" : $"/files/{normalized}";
    }

    internal static IEnumerable<ObjectiveDefinitionDto> OrderObjectives(
        IEnumerable<QuestCondition> conditions,
        Dictionary<string, string> locale,
        Action<string>? onDuplicateId = null
    )
    {
        // Modded traders sometimes ship the same objective condition ID more than once.
        // SPT profile progress is keyed by that ID, so later copies cannot be represented
        // independently; keep the first definition and preserve its stable source order.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var source = conditions
            .Select((condition, sourceIndex) => new ObjectiveOrderItem(condition, sourceIndex))
            .Where(item =>
            {
                var id = item.Condition.Id.ToString();
                if (seenIds.Add(id)) return true;
                onDuplicateId?.Invoke(id);
                return false;
            })
            .ToArray();
        var byId = source.ToDictionary(item => item.Condition.Id.ToString(), StringComparer.Ordinal);
        var dependencies = source.ToDictionary(
            item => item.Condition.Id.ToString(),
            item => new HashSet<string>(
                (string.IsNullOrWhiteSpace(item.Condition.ParentId) ? [] : new[] { item.Condition.ParentId })
                    .Concat(item.Condition.VisibilityConditions?.Select(condition => condition.Target).Where(target => !string.IsNullOrWhiteSpace(target)).Cast<string>() ?? [])
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
            if (ready.Length == 0) ready = remaining.Values.OrderBy(item => item.SourceIndex).ToArray();

            foreach (var item in ready)
            {
                ordered.Add(item);
                remaining.Remove(item.Condition.Id.ToString());
            }
        }

        var result = new List<ObjectiveDefinitionDto>(ordered.Count);
        foreach (var item in ordered)
        {
            var condition = item.Condition;
            var conditionId = condition.Id.ToString();
            result.Add(new ObjectiveDefinitionDto(
                conditionId,
                Localize(locale, conditionId, condition.ConditionType),
                condition.ConditionType,
                condition.Index,
                condition.ParentId,
                condition.Value,
                condition.CompareMethod,
                condition.VisibilityConditions?.Select(value => value.Target).Where(target => !string.IsNullOrEmpty(target)).Cast<string>().ToArray() ?? [],
                GetObjectiveZoneIds(condition),
                condition.OneSessionOnly == true,
                condition.DoNotResetIfCounterCompleted == true));

            var counterConditions = condition.Counter?.Conditions ?? [];
            if (!counterConditions.Any(IsWttSalvageCondition)) continue;

            for (var childIndex = 0; childIndex < counterConditions.Count; childIndex++)
            {
                var child = counterConditions[childIndex];
                var childId = child.Id?.ToString();
                if (string.IsNullOrWhiteSpace(childId)) childId = $"{conditionId}:nested:{childIndex}";
                if (!seenIds.Add(childId))
                {
                    onDuplicateId?.Invoke(childId);
                    continue;
                }

                result.Add(new ObjectiveDefinitionDto(
                    childId,
                    Localize(locale, childId, HumanizeConditionType(child.ConditionType)),
                    child.ConditionType,
                    null,
                    conditionId,
                    ToNullableDouble(child.Value),
                    child.CompareMethod,
                    [],
                    GetCounterConditionZoneIds(child))
                {
                    ContributesToProgress = false,
                });
            }
        }

        return result;
    }

    private static bool IsWttSalvageCondition(QuestConditionCounterCondition condition) =>
        string.Equals(condition.ConditionType, "Salvage", StringComparison.Ordinal);

    private static string[] GetCounterConditionZoneIds(QuestConditionCounterCondition condition)
    {
        var zoneIds = new HashSet<string>(StringComparer.Ordinal);
        AddZones(zoneIds, condition.Zones ?? []);
        if (condition.ConditionType == "VisitPlace") AddZones(zoneIds, GetTargets(condition.Target));
        return zoneIds.OrderBy(zoneId => zoneId, StringComparer.Ordinal).ToArray();
    }

    private static double? ToNullableDouble(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case double doubleValue:
                return doubleValue;
            case float floatValue:
                return floatValue;
            case decimal decimalValue:
                return (double)decimalValue;
            case byte or sbyte or short or ushort or int or uint or long or ulong:
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            case JsonElement { ValueKind: JsonValueKind.Number } jsonNumber when jsonNumber.TryGetDouble(out var jsonNumberValue):
                return jsonNumberValue;
            case JsonElement { ValueKind: JsonValueKind.String } json:
                return double.TryParse(json.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var jsonStringValue)
                    ? jsonStringValue
                    : null;
            case string text:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var stringValue)
                    ? stringValue
                    : null;
            default:
                return null;
        }
    }

    private static string HumanizeConditionType(string? conditionType)
    {
        if (string.IsNullOrWhiteSpace(conditionType)) return "Objective";
        var result = new StringBuilder(conditionType.Length + 8);
        for (var index = 0; index < conditionType.Length; index++)
        {
            var value = conditionType[index];
            if (index > 0 && char.IsUpper(value) && char.IsLower(conditionType[index - 1])) result.Append(' ');
            result.Append(index == 0 ? value : char.ToLowerInvariant(value));
        }
        return result.ToString();
    }

    internal static string[] GetObjectiveZoneIds(QuestCondition condition)
    {
        var zoneIds = new HashSet<string>(StringComparer.Ordinal);
        AddZone(zoneIds, condition.ZoneId);
        if (condition.ConditionType == "VisitPlace") AddZones(zoneIds, GetTargets(condition.Target));

        foreach (var child in condition.Counter?.Conditions ?? [])
        {
            AddZones(zoneIds, child.Zones ?? []);
            if (child.ConditionType == "VisitPlace") AddZones(zoneIds, GetTargets(child.Target));
        }

        return zoneIds.OrderBy(zoneId => zoneId, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<string> GetExplicitObjectiveMapIds(
        QuestCondition condition,
        IReadOnlyDictionary<string, string>? canonicalMapIdsByAlias)
    {
        if (canonicalMapIdsByAlias is null) yield break;
        var candidates = string.Equals(condition.ConditionType, "Location", StringComparison.Ordinal)
            ? GetTargets(condition)
            : (condition.Counter?.Conditions ?? [])
                .Where(child => string.Equals(child.ConditionType, "Location", StringComparison.Ordinal))
                .SelectMany(child => GetTargets(child.Target));
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (canonicalMapIdsByAlias.TryGetValue(candidate, out var canonicalMapId))
            {
                yield return CanonicalizeVariantMapId(canonicalMapId);
            }
        }
    }

    internal static string CanonicalizeVariantMapId(string mapId)
    {
        if (string.Equals(mapId, "factory4_night", StringComparison.OrdinalIgnoreCase)) return "factory4_day";
        if (string.Equals(mapId, "Sandbox_high", StringComparison.OrdinalIgnoreCase)) return "Sandbox";
        return mapId;
    }

    internal static IReadOnlyDictionary<string, string> BuildCanonicalMapIdLookup(
        IReadOnlyDictionary<string, Location> locationsById) => locationsById
        .Where(pair => IsApplicableTaskMapLocation(pair.Value))
        .ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Base.Id,
            StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, string[]> BuildMapAliases(
        IEnumerable<Location> locations)
    {
        var aliases = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in locations.Where(location =>
                     !string.IsNullOrWhiteSpace(location.Base?.Id)
                     && IsApplicableTaskMapLocation(location)))
        {
            var internalId = location.Base.Id;
            var canonical = CanonicalizeVariantMapId(internalId);
            if (!aliases.TryGetValue(canonical, out var values))
            {
                values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                aliases[canonical] = values;
            }
            values.Add(canonical);
            values.UnionWith(GetLocationIdentityIds(location));
        }
        return aliases.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsApplicableTaskMapId(
        string mapId,
        IReadOnlyDictionary<string, Location> locationsById)
    {
        if (IsArenaLocation(mapId, null)) return false;
        return !locationsById.TryGetValue(mapId, out var location)
            || IsApplicableTaskMapLocation(location);
    }

    private static bool IsApplicableTaskMapLocation(Location location) =>
        !IsArenaLocation(location.Base?.Id, location.Base?.Name)
        && !IsArenaLocation(location.Base?.IdField.ToString(), location.Base?.Name);

    private static bool IsArenaLocation(string? id, string? name) =>
        string.Equals(id, ArenaInternalLocationId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(id, ArenaMongoLocationId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Arena", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetLocationIdentityIds(Location location)
    {
        if (!string.IsNullOrWhiteSpace(location.Base?.Id)) yield return location.Base.Id;
        var mongoId = location.Base?.IdField.ToString();
        if (!string.IsNullOrWhiteSpace(mongoId)) yield return mongoId;
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
                            Localize(locale, $"{templateId} Name", template?.Name ?? templateId),
                            group.Sum(item => item.Upd?.StackObjectsCount ?? 1)
                        );
                    })
                    .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
                var targetName = string.IsNullOrWhiteSpace(target)
                    ? null
                    : Localize(locale, $"{target} name", Localize(locale, target, traderName ?? target));

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

    internal static RequirementDto? ToRequirement(QuestCondition condition)
    {
        if (!condition.Value.HasValue) return null;
        var traderId = condition.ConditionType is "Level" or "PrestigeLevel"
            ? null
            : GetTargets(condition).FirstOrDefault();
        return new RequirementDto(condition.ConditionType, traderId, condition.CompareMethod ?? ">=", condition.Value.Value);
    }

    internal static IEnumerable<string> GetTargets(QuestCondition condition)
        => GetTargets(condition.Target);

    internal static IEnumerable<string> GetTargets(QuestConditionCounterCondition condition)
        => GetTargets(condition.Target);

    private static IEnumerable<string> GetTargets(SPTarkov.Server.Core.Utils.Json.ListOrT<string>? target)
    {
        if (target is null) return [];
        if (target.IsList) return target.List ?? [];
        return target.Item is null ? [] : [target.Item];
    }

    private static void AddZones(HashSet<string> destination, IEnumerable<string> zoneIds)
    {
        foreach (var zoneId in zoneIds) AddZone(destination, zoneId);
    }

    private static void AddZone(HashSet<string> destination, string? zoneId)
    {
        if (!string.IsNullOrWhiteSpace(zoneId)) destination.Add(zoneId);
    }

    internal static string Localize(Dictionary<string, string> locale, string key, string fallback) =>
        locale.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

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
        return Localize(locale, $"{id} Nickname", trader.Base.Nickname ?? trader.Base.Name ?? id);
    }

    private sealed record ObjectiveOrderItem(QuestCondition Condition, int SourceIndex);
}
