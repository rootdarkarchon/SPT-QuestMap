using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Logging;
using SPTQuestMap.Client.Localization;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestGraphView : IGlobalTasksContentView
{
    private const float MinimumScale = 0.2f;
    private const float MaximumScale = 1.8f;
    private const double CullPadding = 160;
    private readonly RectTransform _viewport;
    private readonly RectTransform _content;
    private IQuestGraphProjection _projection;
    private QuestGraphTopology _topology;
    private QuestGraphSpatialIndex _spatialIndex;
    private readonly QuestGraphNodePool _nodePool;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly QuestGraphEdgeLayer _edgeLayer;
    private readonly GraphPanZoomHandler _input;
    private readonly Action<string> _onSelected;
    private readonly Action<string>? _onFocusRequested;
    private readonly Action _onViewportSettled;
    private readonly ManualLogSource _log;
    private readonly bool _debugLogging;
    private readonly Dictionary<string, QuestGraphCardNodeView> _activeNodes = new(StringComparer.Ordinal);
    private readonly List<Button> _selectionDependentButtons = [];
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
        Action<string>? onFocusRequested,
        Action onViewportSettled,
        ManualLogSource log,
        bool debugLogging,
        QuestAssetSpriteCache assetCache)
    {
        Root = root;
        _viewport = viewport;
        _content = content;
        _projection = projection;
        _topology = topology;
        _overlay = overlay;
        _onSelected = onSelected;
        _onFocusRequested = onFocusRequested;
        _onViewportSettled = onViewportSettled;
        _log = log;
        _debugLogging = debugLogging;
        _spatialIndex = new QuestGraphSpatialIndex(projection.NodesById);
        _assetCache = assetCache;
        _nodePool = new QuestGraphNodePool(content, _assetCache);
        BuildRepeatableBand(content, projection, overlay);
        BuildInProgressHeaders(content, projection);
        _edgeLayer = QuestGraphEdgeLayer.Create(content, projection, _selection, OnEdgeMeshBuilt);
        _input = viewport.gameObject.AddComponent<GraphPanZoomHandler>();
        _input.Bind(viewport, content, RefreshViewportCulling, ViewportSettled);
        viewport.gameObject.AddComponent<GraphViewportObserver>().Bind(RefreshViewportCulling);
    }

    public RectTransform Root { get; }
    public int ActiveNodeCount => _activeNodes.Count;
    public int PooledNodeCount => _nodePool.AvailableCount;
    public int CreatedNodeCount => _nodePool.TotalCreated;
    public Vector2 ViewportSize => _viewport.rect.size;
    public QuestAssetSpriteCache AssetCache => _assetCache;

    private static void BuildRepeatableBand(RectTransform content, IQuestGraphProjection projection, QuestProfileOverlay overlay)
    {
        var repeatables = projection.Nodes.Where(node => node.ProfileGenerated).ToArray();
        if (repeatables.Length == 0) return;
        foreach (var kind in new[] { "Daily", "Weekly" })
        {
            var group = repeatables.Where(node => string.Equals(node.RepeatableKind, kind, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (group.Length == 0) continue;
            var positions = group.Select(node => projection.NodesById[node.Id]).ToArray();
            var minimumX = (float)positions.Min(position => position.X);
            var maximumX = (float)positions.Max(position => position.X + position.Width);
            var ends = group.Select(node => overlay.RepeatableEndTimes.TryGetValue(node.Id, out var end) ? end : 0)
                .Where(end => end > 0).ToArray();
            var remaining = ends.Length == 0
                ? string.Empty
                : ClientLocale.Format("common.inlineDetailWide", ClientLocale.Arg("detail", FormatRemaining(ends.Min())));
            var kindLabel = ClientLocale.Text($"repeatable.{kind.ToLowerInvariant()}");

            var header = UnityUiFactory.CreateRect($"{kind}Header", content);
            header.anchorMin = header.anchorMax = header.pivot = new Vector2(0, 1);
            header.anchoredPosition = new Vector2(minimumX, -2);
            header.sizeDelta = new Vector2(Mathf.Max(180, maximumX - minimumX), 28);
            var label = UnityUiFactory.AddText(header.gameObject, ClientLocale.Format("common.groupCount",
                    ClientLocale.Arg("kind", kindLabel.ToUpperInvariant()), ClientLocale.Arg("count", group.Length),
                    ClientLocale.Arg("remaining", remaining)), 15,
                TextAlignmentOptions.MidlineLeft, new Color(0.88f, 0.84f, 0.72f, 1));
            label.fontStyle = FontStyles.Bold;

            var groupSeparator = UnityUiFactory.CreateRect($"{kind}Separator", content);
            groupSeparator.anchorMin = groupSeparator.anchorMax = groupSeparator.pivot = new Vector2(0, 1);
            groupSeparator.anchoredPosition = new Vector2(minimumX, -31);
            groupSeparator.sizeDelta = new Vector2(Mathf.Max(180, maximumX - minimumX), 1);
            groupSeparator.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
        }

        var separator = UnityUiFactory.CreateRect("RepeatableSeparator", content);
        separator.anchorMin = separator.anchorMax = separator.pivot = new Vector2(0, 1);
        separator.anchoredPosition = new Vector2(0, -184);
        separator.sizeDelta = new Vector2(Mathf.Max(1, (float)projection.Width), 2);
        separator.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
    }

    private static string FormatRemaining(long endTime)
    {
        var seconds = Math.Max(0, endTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var days = seconds / 86400;
        return days > 0
            ? ClientLocale.Format("common.remainingDays", ClientLocale.Arg("days", days),
                ClientLocale.Arg("hours", (seconds % 86400) / 3600), ClientLocale.Arg("minutes", seconds % 3600 / 60))
            : ClientLocale.Format("common.remainingHours", ClientLocale.Arg("hours", seconds / 3600),
                ClientLocale.Arg("minutes", seconds % 3600 / 60));
    }

    private static void BuildInProgressHeaders(RectTransform content, IQuestGraphProjection projection)
    {
        if (projection is not GlobalQuestGraphProjection { Mode: GlobalQuestGraphMode.InProgress }) return;
        foreach (var group in projection.Nodes.GroupBy(node => (node.TraderId, node.TraderName)))
        {
            var first = group.Select(node => projection.NodesById[node.Id]).OrderBy(position => position.X).First();
            var rect = UnityUiFactory.CreateRect($"TraderHeader-{group.Key.TraderId}", content);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2((float)first.X, 0);
            rect.sizeDelta = new Vector2((float)first.Width, 30);
            var label = UnityUiFactory.AddText(rect.gameObject, group.Key.TraderName.ToUpperInvariant(), 16,
                TextAlignmentOptions.MidlineLeft, new Color(0.88f, 0.84f, 0.72f, 1));
            label.fontStyle = FontStyles.Bold;
        }
    }

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
        QuestAssetSpriteCache assetCache,
        GraphViewportState? initialViewport = null,
        IReadOnlyList<QuestGraphHeaderAction>? extraActions = null,
        bool ignoreParentLayout = false,
        Action? onBackgroundClick = null,
        bool globalChrome = false,
        Action<string>? onFocusRequested = null,
        float? globalHeaderHeightOverride = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var root = UnityUiFactory.CreateRect("QuestMapGraph", mountRect.parent);
        UnityUiFactory.CopyRect(mountRect, root);
        if (ignoreParentLayout)
        {
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }
        root.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.062f, 0.066f, 0.985f);

        var header = UnityUiFactory.CreateRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        var globalHeaderHeight = globalHeaderHeightOverride
            ?? (projection is GlobalQuestGraphProjection { Mode: GlobalQuestGraphMode.InProgress } ? 140 : 96);
        header.offsetMin = new Vector2(0, globalChrome ? -globalHeaderHeight : -44);
        header.offsetMax = Vector2.zero;
        header.gameObject.AddComponent<Image>().color = globalChrome ? new Color(0.055f, 0.07f, 0.075f, 0.99f) : Color.clear;
        if (!globalChrome)
        {
            var title = UnityUiFactory.AddText(header.gameObject, titleText, 19, TextAlignmentOptions.MidlineLeft, new Color(0.88f, 0.84f, 0.72f, 1));
            title.margin = new Vector4(12, 0, 12, 0);
            title.fontStyle = FontStyles.UpperCase;
        }

        var viewport = UnityUiFactory.CreateRect("Viewport", root);
        UnityUiFactory.Stretch(viewport, 8, 8, globalChrome ? globalHeaderHeight + 4 : 48, 8);
        viewport.gameObject.AddComponent<Image>().color = new Color(0.025f, 0.029f, 0.032f, 0.85f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = UnityUiFactory.CreateRect("GraphContent", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(0, 1);
        content.pivot = new Vector2(0, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(Mathf.Max(1, (float)projection.Width), Mathf.Max(1, (float)projection.Height));

        var view = new QuestGraphView(root, viewport, content, projection, topology, overlay, onSelected, onFocusRequested,
            onViewportSettled, log, debugLogging, assetCache);
        view._input.Bind(viewport, content, view.RefreshViewportCulling, view.ViewportSettled, onBackgroundClick);
        if (globalChrome)
        {
            if (projection is not GlobalQuestGraphProjection { Mode: GlobalQuestGraphMode.InProgress })
                view.BuildCanvasControls(extraActions ?? Array.Empty<QuestGraphHeaderAction>());
        }
        else
        {
            view.BuildControls(header, extraActions ?? Array.Empty<QuestGraphHeaderAction>());
        }
        view.BuildLegend(globalChrome);
        if (projection.Nodes.Count == 0) view.BuildEmptyState();
        Canvas.ForceUpdateCanvases();
        if (initialViewport.HasValue) view.RestoreViewportState(initialViewport.Value);
        else view.FitToVisible(false);
        view.RefreshViewportCulling();
        Canvas.ForceUpdateCanvases();
        stopwatch.Stop();
        QuestMapDebugLog.Info(log,
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
            QuestMapDebugLog.Info(_log, $"QUESTMAP_M05_OVERLAY activeNodes={_activeNodes.Count}; totalNodes={_projection.Nodes.Count}; overlayMs={stopwatch.Elapsed.TotalMilliseconds:F2}; layoutRebuilt=False");
        }
    }

    public void RefreshQuest(QuestProfileOverlay overlay, string questId)
    {
        _overlay = overlay;
        if (_activeNodes.TryGetValue(questId, out var view)) ApplyNodeState(questId, view);
    }

    public bool ApplyTopologyDelta(
        IQuestGraphProjection projection,
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        string? selectedQuestId)
    {
        if (_disposed) return false;
        var stopwatch = Stopwatch.StartNew();
        var priorNodeIds = _projection.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var nextNodeIds = projection.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var added = nextNodeIds.Except(priorNodeIds, StringComparer.Ordinal).Count();
        var removed = priorNodeIds.Except(nextNodeIds, StringComparer.Ordinal).Count();

        // Repeatable quests are isolated nodes. If an unexpected replacement
        // changes dependency edges, fall back to the controller's safe full
        // rebuild rather than leave a stale edge mesh behind.
        if (!_projection.Edges.SequenceEqual(projection.Edges)) return false;

        foreach (var view in _activeNodes.Values) _nodePool.Release(view);
        _activeNodes.Clear();
        RemoveRepeatableBand();
        _projection = projection;
        _topology = topology;
        _overlay = overlay;
        _spatialIndex = new QuestGraphSpatialIndex(projection.NodesById);
        _content.sizeDelta = new Vector2(
            Mathf.Max(1, (float)projection.Width),
            Mathf.Max(1, (float)projection.Height));
        BuildRepeatableBand(_content, projection, overlay);
        _selection = QuestGraphRules.BuildSelection(_topology, selectedQuestId);
        _edgeLayer.SetSelection(_selection);
        RefreshViewportCulling();
        stopwatch.Stop();
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M05_TOPOLOGY_DELTA " +
            $"added={added}; removed={removed}; activeNodes={_activeNodes.Count}; " +
            $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}; edgeMeshRebuilt=False; fullRebuild=False");
        return true;
    }

    private void RemoveRepeatableBand()
    {
        foreach (Transform child in _content)
        {
            if (child.name is "DailyHeader" or "WeeklyHeader" or "DailySeparator" or "WeeklySeparator" or "RepeatableSeparator")
            {
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    public void SetSelected(string? questId)
    {
        if (string.Equals(_selection.SelectedQuestId, questId, StringComparison.Ordinal)) return;
        _selection = QuestGraphRules.BuildSelection(_topology, questId);
        UpdateSelectionDependentButtons();
        _edgeLayer.SetSelection(_selection);
        foreach (var pair in _activeNodes) pair.Value.ApplySelection(_selection.GetNodeHighlight(pair.Key), _selection.SelectedQuestId is not null);
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

    public void ZoomIn() => _input.ZoomFromViewportCenter(1.2f);

    public void ZoomOut() => _input.ZoomFromViewportCenter(0.83f);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_debugLogging)
        {
            QuestMapDebugLog.Info(_log,
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
            view.BindStatic(
                node,
                nodePosition,
                _topology.CollectorPathQuestIds.Contains(node.Id),
                _topology.LightkeeperPathQuestIds.Contains(node.Id),
                QuestGraphRules.IsTerminalQuest(_topology, node.Id),
                _onSelected,
                _onFocusRequested);
            ApplyNodeState(questId, view);
            view.SetOverview(scale < 0.42f);
            _activeNodes[questId] = view;
        }
        foreach (var view in _activeNodes.Values) view.SetOverview(scale < 0.42f);
        _maximumActiveNodes = Math.Max(_maximumActiveNodes, _activeNodes.Count);
    }

    private void BuildLegend(bool globalChrome)
    {
        var legend = UnityUiFactory.CreateRect("Legend", Root);
        if (!globalChrome)
        {
            legend.anchorMin = new Vector2(0, 0);
            legend.anchorMax = new Vector2(1, 0);
            legend.pivot = new Vector2(0.5f, 0);
            legend.offsetMin = new Vector2(12, 10);
            legend.offsetMax = new Vector2(-12, 34);
            var text = UnityUiFactory.AddText(legend.gameObject,
                ClientLocale.Text("legend.compact"),
                11, TextAlignmentOptions.MidlineLeft, new Color(0.72f, 0.74f, 0.72f, 0.9f));
            text.enableWordWrapping = false;
            return;
        }

        legend.anchorMin = legend.anchorMax = legend.pivot = Vector2.zero;
        legend.anchoredPosition = new Vector2(12, 12);
        legend.sizeDelta = new Vector2(510, 62);
        legend.gameObject.AddComponent<Image>().color = QuestGraphPalette.Panel;
        var outline = legend.gameObject.AddComponent<Outline>();
        outline.effectColor = QuestGraphPalette.Border;
        outline.effectDistance = Vector2.one;
        AddLegendKey(legend, ClientLocale.Text("legend.available"), QuestGraphPalette.Status(QuestMapDisplayStateKind.Available), 10, 34);
        AddLegendKey(legend, ClientLocale.Text("legend.inProgress"), QuestGraphPalette.Status(QuestMapDisplayStateKind.InProgress), 125, 34);
        AddLegendKey(legend, ClientLocale.Text("legend.ready"), QuestGraphPalette.Status(QuestMapDisplayStateKind.ReadyToFinish), 260, 34);
        AddLegendKey(legend, ClientLocale.Text("legend.completed"), QuestGraphPalette.Status(QuestMapDisplayStateKind.Completed), 360, 34);
        AddLegendKey(legend, ClientLocale.Text("legend.collector"), QuestGraphPalette.Collector, 10, 10);
        AddLegendKey(legend, ClientLocale.Text("legend.lightkeeper"), QuestGraphPalette.Lightkeeper, 125, 10);
        AddLegendKey(legend, ClientLocale.Text("legend.endOfLine"), QuestGraphPalette.Terminal, 260, 10);
    }

    private void ApplyNodeState(string questId, QuestGraphCardNodeView view)
    {
        var node = _topology.NodesById[questId];
        _overlay.QuestsById.TryGetValue(questId, out var liveState);
        view.ApplyStatus(_topology, node, _overlay, liveState);
        view.ApplySelection(_selection.GetNodeHighlight(questId), _selection.SelectedQuestId is not null);
    }

    private void BuildEmptyState()
    {
        var empty = UnityUiFactory.CreateRect("EmptyState", _viewport);
        UnityUiFactory.Stretch(empty, 24, 24, 24, 24);
        UnityUiFactory.AddText(empty.gameObject, ClientLocale.Text("label.noQuests"), 18, TextAlignmentOptions.Center, new Color(0.65f, 0.68f, 0.69f, 1));
    }

    private void BuildControls(RectTransform header, IReadOnlyList<QuestGraphHeaderAction> extraActions)
    {
        AddControl(header, "Fit", ClientLocale.Text("label.fit"), 0, FitToVisible, tooltip: () => ClientLocale.Text("tooltip.fit"));
        AddControl(header, "ZoomIn", "+", 78, () => _input.ZoomFromViewportCenter(1.2f), tooltip: () => ClientLocale.Text("tooltip.zoomIn"));
        AddControl(header, "ZoomOut", "−", 116, () => _input.ZoomFromViewportCenter(0.83f), tooltip: () => ClientLocale.Text("tooltip.zoomOut"));
        _selectionDependentButtons.Add(AddControl(header, "CenterSelected", ClientLocale.Text("label.center"), 154, CenterSelected, 72,
            tooltip: () => ClientLocale.Text(_selection.SelectedQuestId is null ? "tooltip.noQuestSelected" : "tooltip.centerSelected"),
            enabled: _selection.SelectedQuestId is not null));

        var rightOffset = 234f;
        foreach (var action in extraActions)
        {
            var button = AddControl(
                header,
                action.Name,
                action.Label,
                rightOffset,
                action.Action,
                action.Width,
                action.Active ? new Color(0.35f, 0.29f, 0.12f, 1) : new Color(0.22f, 0.24f, 0.25f, 1),
                action.Tooltip,
                action.Enabled);
            if (action.SelectionDependent) _selectionDependentButtons.Add(button);
            rightOffset += action.Width + 8;
        }
    }

    private void BuildCanvasControls(IReadOnlyList<QuestGraphHeaderAction> extraActions)
    {
        var panel = UnityUiFactory.CreateRect("CanvasControls", _viewport);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
        panel.anchoredPosition = new Vector2(12, -12);
        var extraWidth = extraActions.Sum(action => action.Width + 4);
        panel.sizeDelta = new Vector2(228 + extraWidth, 40);
        panel.gameObject.AddComponent<Image>().color = QuestGraphPalette.Panel;
        var outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = QuestGraphPalette.Border;
        outline.effectDistance = Vector2.one;
        _selectionDependentButtons.Add(AddCanvasControl(panel, "CenterSelected", ClientLocale.Text("label.center"), 4, 78, CenterSelected,
            tooltip: () => ClientLocale.Text(_selection.SelectedQuestId is null ? "tooltip.noQuestSelected" : "tooltip.centerSelected"),
            enabled: _selection.SelectedQuestId is not null));
        AddCanvasControl(panel, "ZoomOut", "−", 86, 38, ZoomOut, tooltip: () => ClientLocale.Text("tooltip.zoomOut"));
        AddCanvasControl(panel, "ZoomIn", "+", 128, 38, ZoomIn, tooltip: () => ClientLocale.Text("tooltip.zoomIn"));
        AddCanvasControl(panel, "Fit", ClientLocale.Text("label.fit"), 170, 54, FitToVisible, tooltip: () => ClientLocale.Text("tooltip.fit"));
        var x = 228f;
        foreach (var action in extraActions)
        {
            var button = AddCanvasControl(panel, action.Name, action.Label, x, action.Width, action.Action, action.Active, action.Tooltip, action.Enabled);
            if (action.SelectionDependent) _selectionDependentButtons.Add(button);
            x += action.Width + 4;
        }
        panel.SetAsLastSibling();
    }

    private static Button AddCanvasControl(RectTransform parent, string name, string label, float x, float width, Action action,
        bool active = false, Func<string>? tooltip = null, bool enabled = true)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0);
        rect.sizeDelta = new Vector2(width, 30);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        button.interactable = enabled;
        UnityUiFactory.AddText(rect.gameObject, label, 12, TextAlignmentOptions.Center, Color.white);
        button.onClick.AddListener(() => action());
        if (tooltip is not null) QuestMapNativeTooltips.Bind(rect.gameObject, tooltip);
        return button;
    }

    private static void AddLegendKey(RectTransform parent, string label, Color color, float x, float y)
    {
        var swatch = UnityUiFactory.CreateRect($"Swatch-{label}", parent);
        swatch.anchorMin = swatch.anchorMax = swatch.pivot = Vector2.zero;
        swatch.anchoredPosition = new Vector2(x, y);
        swatch.sizeDelta = new Vector2(13, 13);
        swatch.gameObject.AddComponent<Image>().color = color;
        var textRect = UnityUiFactory.CreateRect($"Label-{label}", parent);
        textRect.anchorMin = textRect.anchorMax = textRect.pivot = Vector2.zero;
        textRect.anchoredPosition = new Vector2(x + 19, y - 1);
        textRect.sizeDelta = new Vector2(110, 16);
        var text = UnityUiFactory.AddText(textRect.gameObject, label, 10, TextAlignmentOptions.MidlineLeft, QuestGraphPalette.MutedText);
        text.enableWordWrapping = false;
    }

    private static Button AddControl(
        RectTransform header,
        string name,
        string label,
        float rightOffset,
        Action action,
        float width = 34,
        Color? color = null,
        Func<string>? tooltip = null,
        bool enabled = true)
    {
        var rect = UnityUiFactory.CreateRect(name, header);
        rect.anchorMin = new Vector2(1, 0.5f);
        rect.anchorMax = new Vector2(1, 0.5f);
        rect.pivot = new Vector2(1, 0.5f);
        rect.anchoredPosition = new Vector2(-rightOffset, 0);
        rect.sizeDelta = new Vector2(width, 30);
        var button = UnityUiFactory.AddButton(rect.gameObject, color ?? new Color(0.22f, 0.24f, 0.25f, 1));
        button.interactable = enabled;
        UnityUiFactory.AddText(rect.gameObject, label, 13, TextAlignmentOptions.Center, Color.white);
        button.onClick.AddListener(() => action());
        if (tooltip is not null) QuestMapNativeTooltips.Bind(rect.gameObject, tooltip);
        return button;
    }

    private void UpdateSelectionDependentButtons()
    {
        var enabled = _selection.SelectedQuestId is not null;
        foreach (var button in _selectionDependentButtons)
            if (button is not null) button.interactable = enabled;
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
            QuestMapDebugLog.Info(_log, $"QUESTMAP_M05_EDGE_MESH edges={edgeCount}; meshMs={elapsedMilliseconds:F2}; reason=geometry-or-selection");
        }
    }
}

internal sealed class QuestGraphHeaderAction
{
    public QuestGraphHeaderAction(
        string name,
        string label,
        float width,
        bool active,
        Action action,
        Func<string>? tooltip = null,
        bool enabled = true,
        bool selectionDependent = false)
    {
        Name = name;
        Label = label;
        Width = width;
        Active = active;
        Action = action;
        Tooltip = tooltip;
        Enabled = enabled;
        SelectionDependent = selectionDependent;
    }

    public string Name { get; }
    public string Label { get; }
    public float Width { get; }
    public bool Active { get; }
    public Action Action { get; }
    public Func<string>? Tooltip { get; }
    public bool Enabled { get; }
    public bool SelectionDependent { get; }
}
