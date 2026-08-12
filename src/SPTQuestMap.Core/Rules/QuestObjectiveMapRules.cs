using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestObjectiveMapRules
{
    public static bool IsActualMapPlaceholder(QuestLocation location) =>
        location.Any
        || location.Id.Contains("transit", StringComparison.OrdinalIgnoreCase)
        || location.Id.Contains("marathon", StringComparison.OrdinalIgnoreCase)
        || (location.Name?.Contains("transition", StringComparison.OrdinalIgnoreCase) ?? false);

    public static bool UsesActualMaps(QuestGraphNode quest) =>
        IsActualMapPlaceholder(quest.Location)
        && quest.ActualMapsComplete
        && quest.ActualMaps.Count > 0;

    public static string NativeFilterId(QuestGraphNode quest) =>
        quest.Location.Any ? "any" : quest.Location.Id;

    public static bool MatchesTableLocationFilter(
        QuestGraphNode quest,
        IReadOnlyCollection<string> selectedLocationIds)
    {
        var selected = selectedLocationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!IsActualMapPlaceholder(quest.Location)) return selected.Contains(quest.Location.Id);
        var mappedTaskMatches = quest.Objectives.Any(objective => objective.MapIds.Any(selected.Contains));
        if (UsesActualMaps(quest)) return mappedTaskMatches;
        return selected.Contains(NativeFilterId(quest)) || mappedTaskMatches;
    }

    public static IReadOnlyList<QuestObjectiveDefinition> FilterTableObjectives(
        QuestGraphNode quest,
        IReadOnlyCollection<string>? selectedLocationIds)
    {
        if (selectedLocationIds is null) return quest.Objectives;
        var selected = selectedLocationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!IsActualMapPlaceholder(quest.Location)) return quest.Objectives;
        var nativeSelected = selected.Contains(NativeFilterId(quest));
        return quest.Objectives
            .Where(objective => objective.MapIds.Count == 0
                ? quest.Location.Any || nativeSelected
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
