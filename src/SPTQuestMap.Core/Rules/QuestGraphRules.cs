using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestGraphRules
{
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.Ordinal)
    {
        "Started",
        "AvailableForFinish",
        "MarkedAsFailed",
    };

    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.Ordinal)
    {
        "Success",
        "Fail",
    };

    public static QuestEdgeRequirementKind ClassifyEdgeRequirement(IEnumerable<string> requiredStatuses)
    {
        var statuses = requiredStatuses.ToHashSet(StringComparer.Ordinal);
        var hasStarted = statuses.Contains("Started");
        var hasSuccess = statuses.Contains("Success");
        var hasFailure = statuses.Contains("Fail")
            || statuses.Contains("FailRestartable")
            || statuses.Contains("MarkedAsFailed");

        if (hasStarted) return QuestEdgeRequirementKind.Started;
        if (hasSuccess && hasFailure) return QuestEdgeRequirementKind.AnyOutcome;
        if (hasFailure) return QuestEdgeRequirementKind.Failure;
        if (hasSuccess) return QuestEdgeRequirementKind.Success;
        return QuestEdgeRequirementKind.Unknown;
    }

    public static IReadOnlyCollection<string> BuildPrerequisiteClosure(QuestGraphTopology topology, string questId)
    {
        if (!topology.NodesById.ContainsKey(questId)) return new HashSet<string>(StringComparer.Ordinal);

        var result = new HashSet<string>(StringComparer.Ordinal) { questId };
        var queue = new Queue<string>();
        queue.Enqueue(questId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!topology.IncomingEdgesByTarget.TryGetValue(current, out var incoming)) continue;
            foreach (var edge in incoming)
            {
                if (topology.NodesById.ContainsKey(edge.SourceId) && result.Add(edge.SourceId))
                {
                    queue.Enqueue(edge.SourceId);
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<string> GetDirectSuccessors(QuestGraphTopology topology, string questId) =>
        topology.OutgoingEdgesBySource.TryGetValue(questId, out var outgoing)
            ? outgoing.Select(edge => edge.TargetId).Where(topology.NodesById.ContainsKey).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : [];

    public static IReadOnlyList<QuestGraphNode> FilterByTrader(IEnumerable<QuestGraphNode> nodes, string? traderId) =>
        StableOrder(string.IsNullOrWhiteSpace(traderId) ? nodes : nodes.Where(node => node.TraderId == traderId));

    public static IReadOnlyList<QuestGraphNode> FilterByFaction(IEnumerable<QuestGraphNode> nodes, string faction) =>
        StableOrder(nodes.Where(node => node.Faction == "Any" || node.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase)));

    public static IReadOnlyList<QuestGraphNode> FilterByEvent(
        QuestGraphTopology topology,
        IReadOnlyCollection<string> activeEvents)
    {
        var excluded = topology.Nodes
            .Where(node => node.EventSeason == "None"
                || (node.EventSeason is not null && !activeEvents.Contains(node.EventSeason)))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in topology.Nodes)
            {
                if (excluded.Contains(node.Id)
                    || !topology.IncomingEdgesByTarget.TryGetValue(node.Id, out var incoming))
                {
                    continue;
                }

                var existingParents = incoming.Where(edge => topology.NodesById.ContainsKey(edge.SourceId)).ToArray();
                if (existingParents.Length > 0 && existingParents.All(edge => excluded.Contains(edge.SourceId)))
                {
                    excluded.Add(node.Id);
                    changed = true;
                }
            }
        }

        return StableOrder(topology.Nodes.Where(node => !excluded.Contains(node.Id)));
    }

    public static IReadOnlyCollection<string> BuildDefaultFrontier(
        QuestGraphTopology topology,
        IReadOnlyCollection<string> knownQuestIds,
        IReadOnlyCollection<string> boundaryQuestIds,
        IReadOnlyCollection<string> applicableQuestIds)
    {
        var result = new HashSet<string>(knownQuestIds.Where(applicableQuestIds.Contains), StringComparer.Ordinal);
        foreach (var sourceId in boundaryQuestIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!applicableQuestIds.Contains(sourceId)
                || !topology.OutgoingEdgesBySource.TryGetValue(sourceId, out var outgoing))
            {
                continue;
            }

            foreach (var edge in outgoing)
            {
                if (applicableQuestIds.Contains(edge.TargetId) && !knownQuestIds.Contains(edge.TargetId))
                {
                    result.Add(edge.TargetId);
                }
            }
        }

        return result;
    }

    public static IReadOnlyList<QuestGraphNode> FilterActive(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay) =>
        StableOrder(topology.Nodes.Where(node => overlay.QuestsById.TryGetValue(node.Id, out var state)
            && state.ExactStatus is not null
            && ActiveStatuses.Contains(state.ExactStatus)));

    public static IReadOnlyList<QuestGraphNode> FilterFinishedAndFailed(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        bool hideFinished) =>
        StableOrder(!hideFinished
            ? topology.Nodes
            : topology.Nodes.Where(node => !overlay.QuestsById.TryGetValue(node.Id, out var state)
                || state.ExactStatus is null
                || !FinishedStatuses.Contains(state.ExactStatus)));

    public static IReadOnlyList<QuestGraphNode> StableOrder(IEnumerable<QuestGraphNode> nodes) =>
        nodes.OrderBy(node => node.TraderName, StringComparer.Ordinal)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
}
