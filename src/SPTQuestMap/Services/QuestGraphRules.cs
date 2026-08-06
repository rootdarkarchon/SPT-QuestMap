using SPTarkov.Server.Core.Models.Enums;
using CoreGraphRules = SPTQuestMap.Core.Rules.QuestGraphRules;
using QuestDependency = SPTQuestMap.Core.Models.QuestDependency;

namespace SPTQuestMap.Services;

internal static class QuestGraphRules
{
    internal static List<QuestNodeDto> PropagateSeasonalEventTypes(IReadOnlyList<QuestNodeDto> nodes, IReadOnlyList<QuestEdgeDto> edges)
    {
        var seasons = nodes.ToDictionary(node => node.Id, node => node.EventSeason, StringComparer.Ordinal);
        var incoming = edges
            .GroupBy(edge => edge.TargetId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).ToArray(), StringComparer.Ordinal);

        for (var pass = 0; pass < nodes.Count; pass++)
        {
            var changed = false;
            foreach (var node in nodes.Where(node => seasons[node.Id] is null))
            {
                if (!incoming.TryGetValue(node.Id, out var parents)) continue;
                var inherited = parents
                    .Select(parent => seasons.GetValueOrDefault(parent))
                    .Where(season => season is not null && season != nameof(SeasonalEventType.None))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
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
        var initiallyExcluded = topology.Quests
            .Where(quest => quest.EventSeason == nameof(SeasonalEventType.None))
            .Select(quest => quest.Id);
        return CoreGraphRules.BuildDescendantExclusionSet(
                topology.Quests.Select(quest => quest.Id),
                initiallyExcluded,
                Dependencies(topology.Edges))
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static RequirementDto[] MergeRequirements(IEnumerable<RequirementDto> requirements)
    {
        return requirements
            .GroupBy(requirement => $"{requirement.Kind}|{requirement.TraderId}|{Direction(requirement.Compare)}", StringComparer.Ordinal)
            .Select(group => Direction(group.First().Compare) == "upper"
                ? group.OrderBy(requirement => requirement.Value).First()
                : group.OrderByDescending(requirement => requirement.Value).First())
            .OrderBy(requirement => requirement.Kind, StringComparer.Ordinal)
            .ThenBy(requirement => requirement.TraderId, StringComparer.Ordinal)
            .ThenBy(requirement => requirement.Compare, StringComparer.Ordinal)
            .ToArray();
    }

    internal static bool Compare(double actual, double required, string compare) =>
        CoreGraphRules.Compare(actual, required, compare);

    internal static HashSet<string> BuildDefaultVisible(
        HashSet<string> known,
        HashSet<string> futureBoundary,
        IReadOnlyCollection<QuestEdgeDto> edges,
        HashSet<string> applicable
    )
    {
        return CoreGraphRules.BuildDefaultFrontier(known, futureBoundary, applicable, Dependencies(edges))
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static HashSet<string> BuildPrerequisiteClosure(string questId, IReadOnlyCollection<QuestEdgeDto> edges, HashSet<string> questIds)
    {
        return CoreGraphRules.BuildPrerequisiteClosure(questId, questIds, Dependencies(edges))
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static string ClassifyEdgeRequirement(IReadOnlyCollection<string> requiredStatuses)
    {
        var kind = CoreGraphRules.ClassifyEdgeRequirement(requiredStatuses);
        return kind == SPTQuestMap.Core.Models.QuestEdgeRequirementKind.Unknown ? "Other" : kind.ToString();
    }

    private static IEnumerable<QuestDependency> Dependencies(IEnumerable<QuestEdgeDto> edges) =>
        edges.Select(edge => new QuestDependency(edge.SourceId, edge.TargetId));

    private static string Direction(string compare) => compare is "<" or "<=" ? "upper" : compare is ">" or ">=" ? "lower" : compare;
}
