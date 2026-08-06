using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Logging;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestGraphView : IDisposable
{
    private const float MinimumScale = 0.2f;
    private const float MaximumScale = 1.8f;
    private const double CullPadding = 160;
    private readonly RectTransform _viewport;
    private readonly RectTransform _content;
    private readonly IQuestGraphProjection _projection;
    private readonly QuestGraphTopology _topology;
    private readonly QuestGraphSpatialIndex _spatialIndex;
    private readonly QuestGraphNodePool _nodePool;
    private readonly QuestGraphEdgeLayer _edgeLayer;
    private readonly GraphPanZoomHandler _input;
    private readonly Action<string> _onSelected;
    private readonly Action _onViewportSettled;
    private readonly ManualLogSource _log;
    private readonly bool _debugLogging;
    private readonly Dictionary<string, QuestGraphNodeView> _activeNodes = new(StringComparer.Ordinal);
    private QuestProfileOverlay _overlay;
    private QuestGraphSelection _selection = QuestGraphSelection.Empty;
    private int _maximumActiveNodes;
    private double _edgeMeshMilliseconds;
    private int _edgeMeshBuilds;
    private bool _disposed;

    private QuestGraphView(
        RectTransform root,
        RectTransform viewport,
        RectTransform content,
        IQuestGraphProjection projection,
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        Action<string> onSelected,
        Action onViewportSettled,
        ManualLogSource log,
        bool debugLogging)
    {
        Root = root;
        _viewport = viewport;
        _content = content;
        _projection = projection;
        _topology = topology;
        _overlay = overlay;
        _onSelected = onSelected;
        _onViewportSettled = onViewportSettled;
        _log = log;
        _debugLogging = debugLogging;
        _spatialIndex = new QuestGraphSpatialIndex(projection.NodesById);
        _nodePool = new QuestGraphNodePool(content);
        _edgeLayer = QuestGraphEdgeLayer.Create(content, projection, _selection, OnEdgeMeshBuilt);
        _input = viewport.gameObject.AddComponent<GraphPanZoomHandler>();
        _input.Bind(viewport, content, RefreshViewportCulling, ViewportSettled);
        viewport.gameObject.AddComponent<GraphViewportObserver>().Bind(RefreshViewportCulling);
    }

    public RectTransform Root { get; }
    public int ActiveNodeCount => _activeNodes.Count;
    public int PooledNodeCount => _nodePool.AvailableCount;
    public int CreatedNodeCount => _nodePool.TotalCreated;

    public static QuestGraphView Create(
        RectTransform mountRect,
        string titleText,
        IQuestGraphProjection projection,
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        Action<string> onSelected,
        Action onViewportSettled,
        ManualLogSource log,
        bool debugLogging,
        GraphViewportState? initialViewport = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var root = UnityUiFactory.CreateRect("QuestMapGraph", mountRect.parent);
        UnityUiFactory.CopyRect(mountRect, root);
        root.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.062f, 0.066f, 0.985f);

        var header = UnityUiFactory.CreateRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(12, -44);
        header.offsetMax = new Vector2(-12, 0);
        var title = UnityUiFactory.AddText(header.gameObject, titleText, 19, TextAlignmentOptions.MidlineLeft, new Color(0.88f, 0.84f, 0.72f, 1));
        title.fontStyle = FontStyles.UpperCase;

        var viewport = UnityUiFactory.CreateRect("Viewport", root);
        UnityUiFactory.Stretch(viewport, 8, 8, 48, 8);
        viewport.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.029f, 0.032f, 0.85f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = UnityUiFactory.CreateRect("GraphContent", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(0, 1);
        content.pivot = new Vector2(0, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(Mathf.Max(1, (float)projection.Width), Mathf.Max(1, (float)projection.Height));

        var view = new QuestGraphView(root, viewport, content, projection, topology, overlay, onSelected, onViewportSettled, log, debugLogging);
        view.BuildControls(header);
        if (projection.Nodes.Count == 0) view.BuildEmptyState();
        Canvas.ForceUpdateCanvases();
        if (initialViewport.HasValue) view.RestoreViewportState(initialViewport.Value);
        else view.FitToVisible(false);
        view.RefreshViewportCulling();
        Canvas.ForceUpdateCanvases();
        stopwatch.Stop();
        log.LogInfo(
            "QUESTMAP_M05_RENDER " +
            $"nodes={projection.Nodes.Count}; edges={projection.Edges.Count}; activeNodes={view.ActiveNodeCount}; " +
            $"createdNodes={view.CreatedNodeCount}; edgeBatches={view._edgeLayer.BatchCount}; firstRenderMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
        return view;
    }

    public void RefreshOverlay(QuestProfileOverlay overlay, string? selectedQuestId)
    {
        var stopwatch = Stopwatch.StartNew();
        _overlay = overlay;
        SetSelected(selectedQuestId);
        foreach (var pair in _activeNodes) ApplyNodeState(pair.Key, pair.Value);
        stopwatch.Stop();
        if (_debugLogging)
        {
            _log.LogInfo($"QUESTMAP_M05_OVERLAY activeNodes={_activeNodes.Count}; totalNodes={_projection.Nodes.Count}; overlayMs={stopwatch.Elapsed.TotalMilliseconds:F2}; layoutRebuilt=False");
        }
    }

    public void SetSelected(string? questId)
    {
        if (string.Equals(_selection.SelectedQuestId, questId, StringComparison.Ordinal)) return;
        _selection = QuestGraphRules.BuildSelection(_topology, questId);
        _edgeLayer.SetSelection(_selection);
        foreach (var pair in _activeNodes) pair.Value.ApplySelection(_selection.GetNodeHighlight(pair.Key));
    }

    public GraphViewportState CaptureViewportState() => new(_content.localScale.x, _content.anchoredPosition);

    public void RestoreViewportState(GraphViewportState state)
    {
        var scale = float.IsNaN(state.Scale) || float.IsInfinity(state.Scale)
            ? 1f
            : Mathf.Clamp(state.Scale, MinimumScale, MaximumScale);
        var position = state.AnchoredPosition;
        if (float.IsNaN(position.x) || float.IsInfinity(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.y)) position = Vector2.zero;
        _content.localScale = new Vector3(scale, scale, 1f);
        _content.anchoredPosition = position;
        RefreshViewportCulling();
    }

    public void FitToVisible() => FitToVisible(true);

    public void CenterSelected()
    {
        var selectedId = _selection.SelectedQuestId;
        if (selectedId is null || !_projection.NodesById.TryGetValue(selectedId, out var position)) return;
        var scale = _content.localScale.x;
        var viewportCenter = new Vector2(_viewport.rect.width / 2f, -_viewport.rect.height / 2f);
        var graphCenter = new Vector2((float)(position.X + position.Width / 2), (float)-(position.Y + position.Height / 2));
        _content.anchoredPosition = viewportCenter - graphCenter * scale;
        RefreshViewportCulling();
        ViewportSettled();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_debugLogging)
        {
            _log.LogInfo(
                "QUESTMAP_M05_RENDER_DISPOSE " +
                $"nodes={_projection.Nodes.Count}; maxActiveNodes={_maximumActiveNodes}; createdNodes={CreatedNodeCount}; pooledNodes={PooledNodeCount}; " +
                $"edgeMeshBuilds={_edgeMeshBuilds}; edgeMeshMs={_edgeMeshMilliseconds:F2}");
        }
        foreach (var view in _activeNodes.Values) _nodePool.Release(view);
        _activeNodes.Clear();
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    private void FitToVisible(bool notify)
    {
        if (_projection.Nodes.Count == 0)
        {
            _content.localScale = Vector3.one;
            _content.anchoredPosition = Vector2.zero;
        }
        else
        {
            Canvas.ForceUpdateCanvases();
            var viewportSize = _viewport.rect.size;
            var graphWidth = Mathf.Max(1, (float)_projection.Width);
            var graphHeight = Mathf.Max(1, (float)_projection.Height);
            var scale = Mathf.Clamp(Mathf.Min(viewportSize.x / graphWidth, viewportSize.y / graphHeight) * 0.92f, MinimumScale, 1f);
            _content.localScale = new Vector3(scale, scale, 1);
            _content.anchoredPosition = new Vector2(
                Mathf.Max(12, (viewportSize.x - graphWidth * scale) / 2),
                -Mathf.Max(12, (viewportSize.y - graphHeight * scale) / 2));
        }
        RefreshViewportCulling();
        if (notify) ViewportSettled();
    }

    private void RefreshViewportCulling()
    {
        if (_disposed || _projection.Nodes.Count == 0) return;
        var scale = Mathf.Max(MinimumScale, _content.localScale.x);
        var position = _content.anchoredPosition;
        var bounds = new QuestGraphRect(
            -position.x / scale,
            position.y / scale,
            _viewport.rect.width / scale,
            _viewport.rect.height / scale);
        var visible = _spatialIndex.Query(bounds, CullPadding).ToHashSet(StringComparer.Ordinal);
        foreach (var questId in _activeNodes.Keys.Where(id => !visible.Contains(id)).ToArray())
        {
            _nodePool.Release(_activeNodes[questId]);
            _activeNodes.Remove(questId);
        }
        foreach (var questId in visible)
        {
            if (_activeNodes.ContainsKey(questId)
                || !_topology.NodesById.TryGetValue(questId, out var node)
                || !_projection.NodesById.TryGetValue(questId, out var nodePosition)) continue;
            var view = _nodePool.Acquire();
            view.BindStatic(node, nodePosition, _onSelected);
            ApplyNodeState(questId, view);
            _activeNodes[questId] = view;
        }
        _maximumActiveNodes = Math.Max(_maximumActiveNodes, _activeNodes.Count);
    }

    private void ApplyNodeState(string questId, QuestGraphNodeView view)
    {
        var node = _topology.NodesById[questId];
        _overlay.QuestsById.TryGetValue(questId, out var liveState);
        view.ApplyStatus(node, liveState);
        view.ApplySelection(_selection.GetNodeHighlight(questId));
    }

    private void BuildEmptyState()
    {
        var empty = UnityUiFactory.CreateRect("EmptyState", _viewport);
        UnityUiFactory.Stretch(empty, 24, 24, 24, 24);
        UnityUiFactory.AddText(empty.gameObject, "No quests were found for this view.", 18, TextAlignmentOptions.Center, new Color(0.65f, 0.68f, 0.69f, 1));
    }

    private void BuildControls(RectTransform header)
    {
        AddControl(header, "Fit", "FIT", 0, FitToVisible);
        AddControl(header, "ZoomIn", "+", 78, () => _input.ZoomFromViewportCenter(1.2f));
        AddControl(header, "ZoomOut", "−", 116, () => _input.ZoomFromViewportCenter(0.83f));
        AddControl(header, "CenterSelected", "CENTER", 154, CenterSelected, 72);
    }

    private static void AddControl(RectTransform header, string name, string label, float rightOffset, Action action, float width = 34)
    {
        var rect = UnityUiFactory.CreateRect(name, header);
        rect.anchorMin = new Vector2(1, 0.5f);
        rect.anchorMax = new Vector2(1, 0.5f);
        rect.pivot = new Vector2(1, 0.5f);
        rect.anchoredPosition = new Vector2(-rightOffset, 0);
        rect.sizeDelta = new Vector2(width, 30);
        var button = UnityUiFactory.AddButton(rect.gameObject, new Color(0.22f, 0.24f, 0.25f, 1));
        UnityUiFactory.AddText(rect.gameObject, label, 13, TextAlignmentOptions.Center, Color.white);
        button.onClick.AddListener(() => action());
    }

    private void ViewportSettled()
    {
        _onViewportSettled();
    }

    private void OnEdgeMeshBuilt(double elapsedMilliseconds, int edgeCount)
    {
        _edgeMeshMilliseconds += elapsedMilliseconds;
        _edgeMeshBuilds++;
        if (_debugLogging)
        {
            _log.LogInfo($"QUESTMAP_M05_EDGE_MESH edges={edgeCount}; meshMs={elapsedMilliseconds:F2}; reason=geometry-or-selection");
        }
    }
}
