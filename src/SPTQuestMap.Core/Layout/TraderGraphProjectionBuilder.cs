using System.Collections.ObjectModel;
using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Layout;

public static class TraderGraphProjectionBuilder
{
    public static TraderGraphProjection Build(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        string traderId)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        if (layout is null) throw new ArgumentNullException(nameof(layout));
        if (string.IsNullOrWhiteSpace(traderId)) throw new ArgumentException("Trader ID is required.", nameof(traderId));
        if (!string.Equals(topology.Version, layout.TopologyVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("The topology and layout versions do not match.", nameof(layout));
        }

        var nodes = topology.Nodes
            .Where(node => string.Equals(node.TraderId, traderId, StringComparison.Ordinal))
            .Where(node => layout.NodesById.ContainsKey(node.Id))
            .OrderBy(node => layout.NodesById[node.Id].Rank)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        var visibleIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var rankMap = nodes
            .Select(node => layout.NodesById[node.Id].Rank)
            .Distinct()
            .OrderBy(rank => rank)
            .Select((rank, compactRank) => (rank, compactRank))
            .ToDictionary(pair => pair.rank, pair => pair.compactRank);
        var positions = new Dictionary<string, QuestNodePosition>(StringComparer.Ordinal);

        foreach (var rankGroup in nodes.GroupBy(node => layout.NodesById[node.Id].Rank).OrderBy(group => group.Key))
        {
            var row = 0;
            foreach (var node in rankGroup.OrderBy(node => node.Name, StringComparer.Ordinal).ThenBy(node => node.Id, StringComparer.Ordinal))
            {
                var compactRank = rankMap[rankGroup.Key];
                positions[node.Id] = new QuestNodePosition(
                    node.Id,
                    rankGroup.Key,
                    compactRank * (DeterministicGraphLayout.NodeWidth + DeterministicGraphLayout.LayerGap),
                    row * (DeterministicGraphLayout.NodeHeight + DeterministicGraphLayout.RowGap),
                    DeterministicGraphLayout.NodeWidth,
                    DeterministicGraphLayout.NodeHeight);
                row++;
            }
        }

        var edges = topology.Edges
            .Where(edge => visibleIds.Contains(edge.SourceId) && visibleIds.Contains(edge.TargetId))
            .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToArray();
        var width = positions.Count == 0 ? 0 : positions.Values.Max(position => position.X + position.Width);
        var height = positions.Count == 0 ? 0 : positions.Values.Max(position => position.Y + position.Height);

        return new TraderGraphProjection(
            traderId,
            nodes,
            edges,
            new ReadOnlyDictionary<string, QuestNodePosition>(positions),
            width,
            height);
    }
}
