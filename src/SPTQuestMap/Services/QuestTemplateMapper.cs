using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace SPTQuestMap.Services;

internal static class QuestTemplateMapper
{
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

    internal static Dictionary<string, Location> BuildLocationLookup(
        IEnumerable<Location> locations,
        IReadOnlyDictionary<string, string> locationIdMap
    )
    {
        var byInternalId = locations
            .GroupBy(location => location.Base.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, Location>(byInternalId, StringComparer.OrdinalIgnoreCase);
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

        return ordered.Select(item => new ObjectiveDefinitionDto(
            item.Condition.Id.ToString(),
            Localize(locale, item.Condition.Id.ToString(), item.Condition.ConditionType),
            item.Condition.ConditionType,
            item.Condition.Index,
            item.Condition.ParentId,
            item.Condition.Value,
            item.Condition.CompareMethod,
            item.Condition.VisibilityConditions?.Select(condition => condition.Target).Where(target => !string.IsNullOrEmpty(target)).Cast<string>().ToArray() ?? [],
            GetObjectiveZoneIds(item.Condition)
        ));
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
