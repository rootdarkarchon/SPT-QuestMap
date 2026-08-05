using System.Collections.ObjectModel;
using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Layout;

public static class DeterministicGraphLayout
{
    public const double NodeWidth = 320;
    public const double NodeHeight = 112;
    public const double LayerGap = 96;
    public const double RowGap = 28;

    public static QuestGraphLayout Build(QuestGraphTopology topology)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));

        var nodeIds = topology.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var indegree = nodeIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in topology.Edges)
        {
            if (indegree.ContainsKey(edge.SourceId) && indegree.ContainsKey(edge.TargetId)) indegree[edge.TargetId]++;
        }

        var ranks = nodeIds.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var ready = new SortedSet<string>(nodeIds.Where(id => indegree[id] == 0), StringComparer.Ordinal);
        var processed = new HashSet<string>(StringComparer.Ordinal);
        while (ready.Count > 0)
        {
            var current = ready.Min!;
            ready.Remove(current);
            processed.Add(current);
            if (!topology.OutgoingEdgesBySource.TryGetValue(current, out var outgoing)) continue;

            foreach (var edge in outgoing.Where(edge => indegree.ContainsKey(edge.TargetId)))
            {
                ranks[edge.TargetId] = Math.Max(ranks[edge.TargetId], ranks[current] + 1);
                indegree[edge.TargetId]--;
                if (indegree[edge.TargetId] == 0) ready.Add(edge.TargetId);
            }
        }

        // Deterministic cycle fallback: retain any rank inherited from the acyclic prefix,
        // then place still-cyclic nodes in stable ID order without recursively chasing the cycle.
        foreach (var nodeId in nodeIds.Where(id => !processed.Contains(id)))
        {
            if (!topology.IncomingEdgesByTarget.TryGetValue(nodeId, out var incoming)) continue;
            var externalParentRanks = incoming
                .Where(edge => processed.Contains(edge.SourceId))
                .Select(edge => ranks[edge.SourceId] + 1)
                .ToArray();
            if (externalParentRanks.Length > 0) ranks[nodeId] = Math.Max(ranks[nodeId], externalParentRanks.Max());
        }

        var positions = new Dictionary<string, QuestNodePosition>(StringComparer.Ordinal);
        foreach (var rankGroup in nodeIds.GroupBy(id => ranks[id]).OrderBy(group => group.Key))
        {
            var row = 0;
            foreach (var nodeId in rankGroup.OrderBy(id => topology.NodesById[id].TraderName, StringComparer.Ordinal)
                         .ThenBy(id => topology.NodesById[id].Name, StringComparer.Ordinal)
                         .ThenBy(id => id, StringComparer.Ordinal))
            {
                positions[nodeId] = new QuestNodePosition(
                    nodeId,
                    rankGroup.Key,
                    rankGroup.Key * (NodeWidth + LayerGap),
                    row * (NodeHeight + RowGap),
                    NodeWidth,
                    NodeHeight);
                row++;
            }
        }

        var width = positions.Count == 0 ? 0 : positions.Values.Max(position => position.X + position.Width);
        var height = positions.Count == 0 ? 0 : positions.Values.Max(position => position.Y + position.Height);
        return new QuestGraphLayout(
            topology.Version,
            new ReadOnlyDictionary<string, QuestNodePosition>(positions),
            width,
            height);
    }
}
