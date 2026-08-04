using SPTarkov.Server.Core.Models.Enums;

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
        var excluded = topology.Quests
            .Where(quest => quest.EventSeason == nameof(SeasonalEventType.None))
            .Select(quest => quest.Id)
            .ToHashSet(StringComparer.Ordinal);
        var incoming = topology.Edges
            .GroupBy(edge => edge.TargetId)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).ToArray(), StringComparer.Ordinal);

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

    internal static bool Compare(double actual, double required, string compare) => compare switch
    {
        ">=" => actual >= required,
        ">" => actual > required,
        "<=" => actual <= required,
        "<" => actual < required,
        "=" or "==" => Math.Abs(actual - required) < 0.000001,
        _ => false,
    };

    internal static HashSet<string> BuildDefaultVisible(
        HashSet<string> known,
        HashSet<string> futureBoundary,
        IReadOnlyCollection<QuestEdgeDto> edges,
        HashSet<string> applicable
    )
    {
        var frontierCandidates = edges
            .Where(edge => applicable.Contains(edge.SourceId) && applicable.Contains(edge.TargetId))
            .Where(edge => futureBoundary.Contains(edge.SourceId) && !known.Contains(edge.TargetId))
            .Select(edge => edge.TargetId)
            .Distinct(StringComparer.Ordinal);
        var visible = new HashSet<string>(known, StringComparer.Ordinal);
        visible.UnionWith(frontierCandidates);
        return visible;
    }

    internal static HashSet<string> BuildPrerequisiteClosure(string questId, IReadOnlyCollection<QuestEdgeDto> edges, HashSet<string> questIds)
    {
        if (!questIds.Contains(questId)) return [];

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
                if (result.Add(prerequisite)) queue.Enqueue(prerequisite);
            }
        }

        return result;
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

    private static string Direction(string compare) => compare is "<" or "<=" ? "upper" : compare is ">" or ">=" ? "lower" : compare;
}
