using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Layout;

public static class QuestEdgeRoutePlanner
{
    public static IReadOnlyList<QuestEdgeRoute> Build(IQuestGraphProjection projection)
    {
        if (projection is null) throw new ArgumentNullException(nameof(projection));
        var outgoing = projection.Edges
            .GroupBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(edge => NodeY(projection, edge.TargetId)).ThenBy(edge => edge.TargetId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var incoming = projection.Edges
            .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(edge => NodeY(projection, edge.SourceId)).ThenBy(edge => edge.SourceId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var routes = new List<QuestEdgeRoute>(projection.Edges.Count);
        foreach (var edge in projection.Edges)
        {
            if (!projection.NodesById.TryGetValue(edge.SourceId, out var source)
                || !projection.NodesById.TryGetValue(edge.TargetId, out var target)) continue;
            var sourceEdges = outgoing[edge.SourceId];
            var targetEdges = incoming[edge.TargetId];
            var sourcePort = Array.IndexOf(sourceEdges, edge) + 1;
            var targetPort = Array.IndexOf(targetEdges, edge) + 1;
            var start = new QuestGraphPoint(source.X + source.Width, source.Y + source.Height * sourcePort / (sourceEdges.Length + 1));
            var end = new QuestGraphPoint(target.X, target.Y + target.Height * targetPort / (targetEdges.Length + 1));
            QuestGraphPoint control1;
            QuestGraphPoint control2;
            if (end.X > start.X + 24)
            {
                var distance = Math.Max(52, (end.X - start.X) * 0.42);
                control1 = new QuestGraphPoint(start.X + distance, start.Y);
                control2 = new QuestGraphPoint(end.X - distance, end.Y);
            }
            else
            {
                var upperLane = Math.Min(start.Y, end.Y) - 52 - (sourcePort + targetPort) % 7 * 9;
                control1 = new QuestGraphPoint(start.X + 64, upperLane);
                control2 = new QuestGraphPoint(end.X - 64, upperLane);
            }

            routes.Add(new QuestEdgeRoute(edge, start, control1, control2, end));
        }

        return routes;
    }

    private static double NodeY(IQuestGraphProjection projection, string questId) =>
        projection.NodesById.TryGetValue(questId, out var node) ? node.Y : double.MaxValue;
}
