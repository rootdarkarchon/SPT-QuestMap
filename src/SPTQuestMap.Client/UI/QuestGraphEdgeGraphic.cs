using System;
using System.Linq;
using SPTQuestMap.Core.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestGraphEdgeGraphic : MaskableGraphic
{
    private TraderGraphProjection? _projection;

    public void Bind(TraderGraphProjection projection)
    {
        _projection = projection;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (_projection is null) return;

        var outgoing = _projection.Edges
            .GroupBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(edge => EdgeTargetY(edge)).ThenBy(edge => edge.TargetId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var incoming = _projection.Edges
            .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(edge => EdgeSourceY(edge)).ThenBy(edge => edge.SourceId, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        foreach (var edge in _projection.Edges)
        {
            if (!_projection.NodesById.TryGetValue(edge.SourceId, out var source)
                || !_projection.NodesById.TryGetValue(edge.TargetId, out var target))
            {
                continue;
            }

            var sourceEdges = outgoing[edge.SourceId];
            var targetEdges = incoming[edge.TargetId];
            var sourcePort = Array.IndexOf(sourceEdges, edge) + 1;
            var targetPort = Array.IndexOf(targetEdges, edge) + 1;
            var start = new Vector2(
                (float)(source.X + source.Width),
                (float)-(source.Y + source.Height * sourcePort / (sourceEdges.Length + 1)));
            var end = new Vector2(
                (float)target.X,
                (float)-(target.Y + target.Height * targetPort / (targetEdges.Length + 1)));
            var edgeColor = EdgeColor(edge.RequirementKind);
            AddCurve(vertexHelper, start, end, 3f, edgeColor, sourcePort + targetPort);
        }
    }

    private double EdgeTargetY(QuestGraphEdge edge) =>
        _projection!.NodesById.TryGetValue(edge.TargetId, out var target) ? target.Y : double.MaxValue;

    private double EdgeSourceY(QuestGraphEdge edge) =>
        _projection!.NodesById.TryGetValue(edge.SourceId, out var source) ? source.Y : double.MaxValue;

    private static void AddCurve(
        VertexHelper helper,
        Vector2 start,
        Vector2 end,
        float width,
        Color color,
        int laneSeed)
    {
        const int segments = 20;
        Vector2 control1;
        Vector2 control2;
        if (end.x > start.x + 24f)
        {
            var controlDistance = Mathf.Max(52f, (end.x - start.x) * 0.42f);
            control1 = new Vector2(start.x + controlDistance, start.y);
            control2 = new Vector2(end.x - controlDistance, end.y);
        }
        else
        {
            // Cyclic/backward edges use a stable upper lane so they cannot collapse into a
            // forward edge. Production crossing minimization remains Milestone 5 work.
            var upperLane = Mathf.Max(start.y, end.y) + 52f + laneSeed % 7 * 9f;
            control1 = new Vector2(start.x + 64f, upperLane);
            control2 = new Vector2(end.x - 64f, upperLane);
        }

        var previous = start;
        for (var segment = 1; segment <= segments; segment++)
        {
            var t = segment / (float)segments;
            var point = CubicBezier(start, control1, control2, end, t);
            AddLine(helper, previous, point, width, color);
            previous = point;
        }

        AddArrowHead(helper, CubicBezier(start, control1, control2, end, 0.94f), end, color);
    }

    private static Vector2 CubicBezier(Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, float t)
    {
        var inverse = 1f - t;
        return inverse * inverse * inverse * start
            + 3f * inverse * inverse * t * control1
            + 3f * inverse * t * t * control2
            + t * t * t * end;
    }

    private static void AddArrowHead(VertexHelper helper, Vector2 from, Vector2 tip, Color color)
    {
        var direction = (tip - from).normalized;
        if (direction.sqrMagnitude < 0.01f) return;
        var normal = new Vector2(-direction.y, direction.x);
        var baseCenter = tip - direction * 11f;
        var first = helper.currentVertCount;
        helper.AddVert(tip, color, Vector2.zero);
        helper.AddVert(baseCenter + normal * 5f, color, Vector2.zero);
        helper.AddVert(baseCenter - normal * 5f, color, Vector2.zero);
        helper.AddTriangle(first, first + 1, first + 2);
    }

    private static void AddLine(VertexHelper helper, Vector2 start, Vector2 end, float width, Color color)
    {
        var direction = end - start;
        if (direction.sqrMagnitude < 0.01f) return;
        var normal = new Vector2(-direction.y, direction.x).normalized * (width / 2f);
        var first = helper.currentVertCount;
        helper.AddVert(start - normal, color, Vector2.zero);
        helper.AddVert(start + normal, color, Vector2.zero);
        helper.AddVert(end + normal, color, Vector2.zero);
        helper.AddVert(end - normal, color, Vector2.zero);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first, first + 2, first + 3);
    }

    private static Color EdgeColor(QuestEdgeRequirementKind requirementKind) => requirementKind switch
    {
        QuestEdgeRequirementKind.Success => new Color(0.38f, 0.72f, 0.42f, 0.9f),
        QuestEdgeRequirementKind.Failure => new Color(0.82f, 0.28f, 0.28f, 0.9f),
        QuestEdgeRequirementKind.Started => new Color(0.35f, 0.58f, 0.86f, 0.9f),
        QuestEdgeRequirementKind.AnyOutcome => new Color(0.72f, 0.48f, 0.82f, 0.9f),
        _ => new Color(0.58f, 0.58f, 0.58f, 0.75f),
    };
}
