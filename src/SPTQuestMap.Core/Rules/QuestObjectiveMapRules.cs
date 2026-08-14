using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestObjectiveMapRules
{
    public const string NoLocationFilterId = "no-location";
    public const string AnyFilterId = "any";
    public const string TransitionFilterId = "transition";
    public const string NoLocationBannerUrl = "/files/banners/norvinskzone.png";
    public const string AnyBannerUrl = "/files/banners/67e4047d22d6081b78031ddb.jpg";
    public const string TransitionBannerUrl = "/files/banners/banner_tarkov.png";

    public static IReadOnlyCollection<string> ExpandMapIds(
        IReadOnlyCollection<string> mapIds,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> mapAliases)
    {
        var expanded = mapIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var aliases in mapAliases.Values)
        {
            if (aliases.Any(expanded.Contains)) expanded.UnionWith(aliases);
        }
        return expanded;
    }

    public static bool IsActualMapPlaceholder(QuestLocation location) =>
        location.Any
        || IsTransitionLocation(location.Id, location.Name, location.Any);

    public static bool IsTransitionLocation(
        string locationId,
        string? locationName,
        bool any) =>
        !any
        && (locationId.Contains("transit", StringComparison.OrdinalIgnoreCase)
            || locationId.Contains("marathon", StringComparison.OrdinalIgnoreCase)
            || (locationName?.Contains("transition", StringComparison.OrdinalIgnoreCase) ?? false));

    public static bool ShouldPrependTransitionLabel(
        string locationId,
        string? locationName,
        bool any,
        int derivedMapCount) =>
        derivedMapCount > 1
        && IsTransitionLocation(locationId, locationName, any);

    public static bool UsesActualMaps(QuestGraphNode quest) =>
        (quest.TaskLocation.Id.Equals(AnyFilterId, StringComparison.OrdinalIgnoreCase)
            || quest.TaskLocation.Id.Equals(TransitionFilterId, StringComparison.OrdinalIgnoreCase))
        && quest.ActualMapsComplete
        && quest.ActualMaps.Count > 0;

    public static string NativeFilterId(QuestGraphNode quest) => quest.TaskLocation.Id;

    public static bool MatchesTableLocationFilter(
        QuestGraphNode quest,
        IReadOnlyCollection<string> selectedLocationIds)
    {
        var selected = selectedLocationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (quest.TaskLocation.Id.Equals(NoLocationFilterId, StringComparison.OrdinalIgnoreCase))
            return selected.Contains(NoLocationFilterId);
        var nativeSelected = selected.Contains(NativeFilterId(quest));
        if (!UsesActualMaps(quest)) return nativeSelected;
        var mappedTaskMatches = quest.Objectives.Any(objective => objective.MapIds.Any(selected.Contains));
        return nativeSelected || mappedTaskMatches;
    }

    public static IReadOnlyList<QuestObjectiveDefinition> FilterTableObjectives(
        QuestGraphNode quest,
        IReadOnlyCollection<string>? selectedLocationIds)
    {
        if (selectedLocationIds is null) return quest.Objectives;
        var selected = selectedLocationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (quest.TaskLocation.Id.Equals(NoLocationFilterId, StringComparison.OrdinalIgnoreCase))
            return selected.Contains(NoLocationFilterId) ? quest.Objectives : [];
        var nativeSelected = selected.Contains(NativeFilterId(quest));
        if (nativeSelected) return quest.Objectives;
        if (!UsesActualMaps(quest)) return [];
        return quest.Objectives
            .Where(objective => objective.MapIds.Count == 0
                ? quest.Location.Any
                : objective.MapIds.Any(selected.Contains))
            .ToArray();
    }

    public static IReadOnlyList<QuestObjectiveDefinition> TableObjectivesExcludedByLocationFilter(
        QuestGraphNode quest,
        IReadOnlyCollection<string>? selectedLocationIds)
    {
        if (selectedLocationIds is null || !UsesActualMaps(quest)) return [];
        var includedIds = FilterTableObjectives(quest, selectedLocationIds)
            .Select(objective => objective.Id)
            .ToHashSet(StringComparer.Ordinal);
        return quest.Objectives
            .Where(objective => !includedIds.Contains(objective.Id))
            .ToArray();
    }

    public static bool IsMapRelated(
        QuestGraphNode quest,
        IReadOnlyCollection<string> currentMapIds)
    {
        var maps = currentMapIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!IsActualMapPlaceholder(quest.Location)) return maps.Contains(quest.Location.Id);
        return quest.Objectives.Any(objective => objective.MapIds.Any(maps.Contains));
    }

    public static bool IsObjectiveActiveInRaid(
        QuestGraphNode quest,
        QuestObjectiveDefinition objective,
        IReadOnlyCollection<string> currentMapIds,
        bool smartTracking)
    {
        if (smartTracking && !QuestAutoTrackingRules.IsInRaidObjectiveType(objective.ConditionType)) return false;
        var maps = currentMapIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (objective.MapIds.Count > 0) return objective.MapIds.Any(maps.Contains);
        if (quest.Location.Any) return true;
        if (IsActualMapPlaceholder(quest.Location)) return false;
        return maps.Contains(quest.Location.Id);
    }
}
