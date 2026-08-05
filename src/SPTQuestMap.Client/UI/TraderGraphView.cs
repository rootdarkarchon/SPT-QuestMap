using System;
using System.Collections.Generic;
using SPTQuestMap.Core.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class TraderGraphView : IDisposable
{
    private static readonly Color DefaultNodeColor = new(0.17f, 0.19f, 0.20f, 0.98f);
    private static readonly Color SelectedNodeColor = new(0.55f, 0.42f, 0.16f, 1f);
    private readonly Dictionary<string, Image> _nodeImages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Color> _nodeBaseColors = new(StringComparer.Ordinal);
    private readonly RectTransform _viewport;
    private readonly RectTransform _content;
    private readonly TraderGraphProjection _projection;
    private bool _disposed;

    private TraderGraphView(
        RectTransform root,
        RectTransform viewport,
        RectTransform content,
        TraderGraphProjection projection)
    {
        Root = root;
        _viewport = viewport;
        _content = content;
        _projection = projection;
    }

    public RectTransform Root { get; }

    public static TraderGraphView Create(
        RectTransform vanillaListRect,
        TraderGraphProjection projection,
        QuestProfileOverlay overlay,
        Action<string> onSelected)
    {
        var root = UnityUiFactory.CreateRect("QuestMapTraderGraph", vanillaListRect.parent);
        UnityUiFactory.CopyRect(vanillaListRect, root);
        var rootImage = root.gameObject.AddComponent<Image>();
        rootImage.color = new Color(0.055f, 0.062f, 0.066f, 0.985f);

        var header = UnityUiFactory.CreateRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(12, -44);
        header.offsetMax = new Vector2(-12, 0);
        var title = UnityUiFactory.AddText(
            header.gameObject,
            projection.Nodes.Count == 0 ? "QUEST MAP" : $"QUEST MAP  ·  {projection.Nodes[0].TraderName}",
            19,
            TextAlignmentOptions.MidlineLeft,
            new Color(0.88f, 0.84f, 0.72f, 1));
        title.fontStyle = FontStyles.UpperCase;

        var viewport = UnityUiFactory.CreateRect("Viewport", root);
        UnityUiFactory.Stretch(viewport, 8, 8, 48, 8);
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0.025f, 0.029f, 0.032f, 0.85f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = UnityUiFactory.CreateRect("GraphContent", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(0, 1);
        content.pivot = new Vector2(0, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(
            Mathf.Max(1, (float)projection.Width),
            Mathf.Max(1, (float)projection.Height));

        var view = new TraderGraphView(root, viewport, content, projection);
        view.BuildEdges();
        view.BuildNodes(overlay, onSelected);
        if (projection.Nodes.Count == 0) view.BuildEmptyState();

        var input = viewport.gameObject.AddComponent<GraphPanZoomHandler>();
        input.Bind(viewport, content);
        view.BuildFitButton(header);
        Canvas.ForceUpdateCanvases();
        view.FitToVisible();
        return view;
    }

    public void SetSelected(string questId)
    {
        foreach (var pair in _nodeImages)
        {
            pair.Value.color = pair.Key == questId ? SelectedNodeColor : _nodeBaseColors[pair.Key];
        }
    }

    public void FitToVisible()
    {
        if (_projection.Nodes.Count == 0)
        {
            _content.localScale = Vector3.one;
            _content.anchoredPosition = Vector2.zero;
            return;
        }

        Canvas.ForceUpdateCanvases();
        var viewportSize = _viewport.rect.size;
        var graphWidth = Mathf.Max(1, (float)_projection.Width);
        var graphHeight = Mathf.Max(1, (float)_projection.Height);
        var scale = Mathf.Clamp(Mathf.Min(viewportSize.x / graphWidth, viewportSize.y / graphHeight) * 0.92f, 0.2f, 1f);
        _content.localScale = new Vector3(scale, scale, 1);
        _content.anchoredPosition = new Vector2(
            Mathf.Max(12, (viewportSize.x - graphWidth * scale) / 2),
            -Mathf.Max(12, (viewportSize.y - graphHeight * scale) / 2));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
        _nodeImages.Clear();
        _nodeBaseColors.Clear();
    }

    private void BuildEdges()
    {
        var edgeRect = UnityUiFactory.CreateRect("BatchedEdges", _content);
        edgeRect.anchorMin = new Vector2(0, 1);
        edgeRect.anchorMax = new Vector2(0, 1);
        edgeRect.pivot = new Vector2(0, 1);
        edgeRect.anchoredPosition = Vector2.zero;
        edgeRect.sizeDelta = _content.sizeDelta;
        edgeRect.gameObject.AddComponent<QuestGraphEdgeGraphic>().Bind(_projection);
    }

    private void BuildNodes(QuestProfileOverlay overlay, Action<string> onSelected)
    {
        foreach (var node in _projection.Nodes)
        {
            var position = _projection.NodesById[node.Id];
            var nodeRect = UnityUiFactory.CreateRect($"Node-{node.Id}", _content);
            nodeRect.anchorMin = new Vector2(0, 1);
            nodeRect.anchorMax = new Vector2(0, 1);
            nodeRect.pivot = new Vector2(0, 1);
            nodeRect.anchoredPosition = new Vector2((float)position.X, (float)-position.Y);
            nodeRect.sizeDelta = new Vector2((float)position.Width, (float)position.Height);
            var baseColor = StatusColor(node.Id, overlay);
            var button = UnityUiFactory.AddButton(nodeRect.gameObject, baseColor);
            var image = nodeRect.GetComponent<Image>();
            _nodeImages[node.Id] = image;
            _nodeBaseColors[node.Id] = baseColor;
            button.onClick.AddListener(() => onSelected(node.Id));

            var badge = UnityUiFactory.CreateRect("TraderFallback", nodeRect);
            badge.anchorMin = new Vector2(0, 0.5f);
            badge.anchorMax = new Vector2(0, 0.5f);
            badge.pivot = new Vector2(0, 0.5f);
            badge.anchoredPosition = new Vector2(10, 0);
            badge.sizeDelta = new Vector2(42, 42);
            badge.gameObject.AddComponent<Image>().color = new Color(0.31f, 0.29f, 0.24f, 1);
            UnityUiFactory.AddText(
                badge.gameObject,
                TraderInitials(node.TraderName),
                15,
                TextAlignmentOptions.Center,
                new Color(0.94f, 0.88f, 0.68f, 1));

            var nameRect = UnityUiFactory.CreateRect("Name", nodeRect);
            UnityUiFactory.Stretch(nameRect, 62, 10, 8, 34);
            var name = UnityUiFactory.AddText(
                nameRect.gameObject,
                node.Name,
                17,
                TextAlignmentOptions.MidlineLeft,
                Color.white);
            name.fontStyle = FontStyles.Bold;

            var stateRect = UnityUiFactory.CreateRect("State", nodeRect);
            stateRect.anchorMin = new Vector2(0, 0);
            stateRect.anchorMax = new Vector2(1, 0);
            stateRect.pivot = new Vector2(0.5f, 0);
            stateRect.offsetMin = new Vector2(62, 8);
            stateRect.offsetMax = new Vector2(-10, 31);
            UnityUiFactory.AddText(
                stateRect.gameObject,
                StateLabel(node.Id, overlay),
                13,
                TextAlignmentOptions.MidlineLeft,
                new Color(0.72f, 0.75f, 0.76f, 1));
        }
    }

    private void BuildEmptyState()
    {
        var empty = UnityUiFactory.CreateRect("EmptyState", _viewport);
        UnityUiFactory.Stretch(empty, 24, 24, 24, 24);
        UnityUiFactory.AddText(
            empty.gameObject,
            "No quests were found for this trader.",
            18,
            TextAlignmentOptions.Center,
            new Color(0.65f, 0.68f, 0.69f, 1));
    }

    private void BuildFitButton(RectTransform header)
    {
        var fitRect = UnityUiFactory.CreateRect("Fit", header);
        fitRect.anchorMin = new Vector2(1, 0.5f);
        fitRect.anchorMax = new Vector2(1, 0.5f);
        fitRect.pivot = new Vector2(1, 0.5f);
        fitRect.anchoredPosition = Vector2.zero;
        fitRect.sizeDelta = new Vector2(72, 30);
        var fit = UnityUiFactory.AddButton(fitRect.gameObject, new Color(0.22f, 0.24f, 0.25f, 1));
        UnityUiFactory.AddText(fitRect.gameObject, "FIT", 14, TextAlignmentOptions.Center, Color.white);
        fit.onClick.AddListener(FitToVisible);
    }

    private static Color StatusColor(string questId, QuestProfileOverlay overlay)
    {
        if (!overlay.QuestsById.TryGetValue(questId, out var state) || !state.HasLiveQuest)
            return new Color(0.13f, 0.14f, 0.15f, 0.98f);

        return state.ExactStatus switch
        {
            "AvailableForStart" => new Color(0.25f, 0.38f, 0.25f, 0.98f),
            "Started" => new Color(0.20f, 0.31f, 0.43f, 0.98f),
            "AvailableForFinish" => new Color(0.36f, 0.35f, 0.18f, 0.98f),
            "Success" => new Color(0.16f, 0.29f, 0.18f, 0.98f),
            "Fail" or "MarkedAsFailed" => new Color(0.37f, 0.16f, 0.16f, 0.98f),
            "FailRestartable" => new Color(0.38f, 0.24f, 0.15f, 0.98f),
            _ => DefaultNodeColor,
        };
    }

    private static string StateLabel(string questId, QuestProfileOverlay overlay) =>
        overlay.QuestsById.TryGetValue(questId, out var state) && state.HasLiveQuest
            ? state.ExactStatus ?? "Unknown"
            : "Locked future · read-only";

    private static string TraderInitials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        return words.Length == 1
            ? words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant()
            : $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
    }
}
