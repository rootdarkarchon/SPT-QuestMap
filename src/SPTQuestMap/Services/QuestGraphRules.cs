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
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var outgoing = edges
            .GroupBy(edge => edge.SourceId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var queue = new Queue<string>();
        var queued = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes.Where(node => node.EventSeason is null && incoming.ContainsKey(node.Id)))
        {
            queue.Enqueue(node.Id);
            queued.Add(node.Id);
        }

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            queued.Remove(nodeId);
            if (seasons[nodeId] is not null || !incoming.TryGetValue(nodeId, out var parents)) continue;
            var inherited = parents
                .Select(parent => seasons.GetValueOrDefault(parent))
                .Where(season => season is not null && season != nameof(SeasonalEventType.None))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (inherited.Length != 1) continue;

            seasons[nodeId] = inherited[0];
            if (!outgoing.TryGetValue(nodeId, out var children)) continue;
            foreach (var childId in children)
            {
                if (seasons.GetValueOrDefault(childId) is null && queued.Add(childId)) queue.Enqueue(childId);
            }
        }

        return nodes.Select(node => node with { EventSeason = seasons[node.Id] }).ToList();
    }

    internal static IReadOnlyDictionary<string, RequirementDto[]> PropagateEffectiveRequirements(
        IReadOnlyList<QuestNodeDto> nodes,
        IReadOnlyList<QuestEdgeDto> edges)
    {
        var effective = nodes.ToDictionary(node => node.Id, node => node.DirectRequirements, StringComparer.Ordinal);
        var outgoing = edges
            .Where(edge => effective.ContainsKey(edge.SourceId) && effective.ContainsKey(edge.TargetId))
            .GroupBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.TargetId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var indegree = nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        foreach (var targets in outgoing.Values)
        {
            foreach (var targetId in targets) indegree[targetId]++;
        }

        var queue = new Queue<string>(nodes.Where(node => indegree[node.Id] == 0).Select(node => node.Id));
        var processed = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var sourceId = queue.Dequeue();
            processed.Add(sourceId);
            if (!outgoing.TryGetValue(sourceId, out var targets)) continue;
            var parent = effective[sourceId];
            foreach (var targetId in targets)
            {
                var child = effective[targetId];
                var merged = MergeRequirements(child.Concat(parent));
                if (!merged.SequenceEqual(child)) effective[targetId] = merged;
                indegree[targetId]--;
                if (indegree[targetId] == 0) queue.Enqueue(targetId);
            }
        }

        if (processed.Count == nodes.Count) return effective;

        // Modded data can contain cycles. Kahn's pass has already propagated every acyclic
        // predecessor; converge only the unresolved cyclic region and its descendants.
        var unresolved = nodes.Select(node => node.Id).Where(nodeId => !processed.Contains(nodeId)).ToArray();
        queue = new Queue<string>(unresolved);
        var queued = unresolved.ToHashSet(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var sourceId = queue.Dequeue();
            queued.Remove(sourceId);
            if (!outgoing.TryGetValue(sourceId, out var targets)) continue;
            var parent = effective[sourceId];
            foreach (var targetId in targets)
            {
                var child = effective[targetId];
                var merged = MergeRequirements(child.Concat(parent));
                if (merged.SequenceEqual(child)) continue;
                effective[targetId] = merged;
                if (queued.Add(targetId)) queue.Enqueue(targetId);
            }
        }

        return effective;
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
