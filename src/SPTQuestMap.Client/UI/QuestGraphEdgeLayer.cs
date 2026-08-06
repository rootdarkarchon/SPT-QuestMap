using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestGraphEdgeLayer
{
    private const int RoutesPerBatch = 128;
    private readonly ProductionQuestGraphEdgeGraphic[] _batches;

    private QuestGraphEdgeLayer(ProductionQuestGraphEdgeGraphic[] batches)
    {
        _batches = batches;
    }

    public int BatchCount => _batches.Length;

    public static QuestGraphEdgeLayer Create(
        RectTransform content,
        IQuestGraphProjection projection,
        QuestGraphSelection selection,
        Action<double, int>? onMeshBuilt)
    {
        var routes = QuestEdgeRoutePlanner.Build(projection);
        var batches = new List<ProductionQuestGraphEdgeGraphic>();
        for (var start = 0; start < routes.Count; start += RoutesPerBatch)
        {
            var rect = UnityUiFactory.CreateRect($"BatchedEdges-{batches.Count}", content);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = content.sizeDelta;
            var graphic = rect.gameObject.AddComponent<ProductionQuestGraphEdgeGraphic>();
            graphic.Bind(routes.Skip(start).Take(RoutesPerBatch).ToArray(), selection, onMeshBuilt);
            batches.Add(graphic);
        }

        return new QuestGraphEdgeLayer(batches.ToArray());
    }

    public void SetSelection(QuestGraphSelection selection)
    {
        foreach (var batch in _batches) batch.SetSelection(selection);
    }
}

internal sealed class ProductionQuestGraphEdgeGraphic : MaskableGraphic
{
    private IReadOnlyList<QuestEdgeRoute> _routes = Array.Empty<QuestEdgeRoute>();
    private QuestGraphSelection _selection = QuestGraphSelection.Empty;
    private Action<double, int>? _onMeshBuilt;

    public void Bind(IReadOnlyList<QuestEdgeRoute> routes, QuestGraphSelection selection, Action<double, int>? onMeshBuilt)
    {
        _routes = routes;
        _selection = selection;
        _onMeshBuilt = onMeshBuilt;
        raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetSelection(QuestGraphSelection selection)
    {
        if (ReferenceEquals(_selection, selection)) return;
        _selection = selection;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        var stopwatch = Stopwatch.StartNew();
        helper.Clear();
        foreach (var route in _routes)
        {
            var style = EdgeStyle(route.Edge);
            AddCurve(helper, Point(route.Start), Point(route.Control1), Point(route.Control2), Point(route.End), style.Width, style.Color);
        }
        stopwatch.Stop();
        _onMeshBuilt?.Invoke(stopwatch.Elapsed.TotalMilliseconds, _routes.Count);
    }

    private EdgeRenderStyle EdgeStyle(QuestGraphEdge edge)
    {
        var color = BaseEdgeColor(edge.RequirementKind);
        if (_selection.SelectedQuestId is null) return new EdgeRenderStyle(color, 3f);
        var prerequisite = _selection.PrerequisiteQuestIds.Contains(edge.SourceId)
            && (_selection.PrerequisiteQuestIds.Contains(edge.TargetId)
                || string.Equals(_selection.SelectedQuestId, edge.TargetId, StringComparison.Ordinal));
        var successor = string.Equals(_selection.SelectedQuestId, edge.SourceId, StringComparison.Ordinal)
            && _selection.DirectSuccessorQuestIds.Contains(edge.TargetId);
        if (prerequisite || successor)
        {
            color.a = 1f;
            return new EdgeRenderStyle(color, 5f);
        }
        color.a = 0.18f;
        return new EdgeRenderStyle(color, 2f);
    }

    private static Vector2 Point(QuestGraphPoint point) => new((float)point.X, (float)-point.Y);

    private static void AddCurve(VertexHelper helper, Vector2 start, Vector2 control1, Vector2 control2, Vector2 end, float width, Color color)
    {
        const int segments = 20;
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

    private static Color BaseEdgeColor(QuestEdgeRequirementKind kind) => kind switch
    {
        QuestEdgeRequirementKind.Success => new Color(0.38f, 0.72f, 0.42f, 0.9f),
        QuestEdgeRequirementKind.Failure => new Color(0.82f, 0.28f, 0.28f, 0.9f),
        QuestEdgeRequirementKind.Started => new Color(0.35f, 0.58f, 0.86f, 0.9f),
        QuestEdgeRequirementKind.AnyOutcome => new Color(0.72f, 0.48f, 0.82f, 0.9f),
        _ => new Color(0.58f, 0.58f, 0.58f, 0.75f),
    };

    private readonly struct EdgeRenderStyle
    {
        public EdgeRenderStyle(Color color, float width)
        {
            Color = color;
            Width = width;
        }
        public Color Color { get; }
        public float Width { get; }
    }
}
