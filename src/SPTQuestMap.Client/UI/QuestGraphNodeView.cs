using System;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestGraphNodeView
{
    private static readonly Color DefaultNodeColor = new(0.17f, 0.19f, 0.20f, 0.98f);
    private static readonly Color SelectedNodeColor = new(0.55f, 0.42f, 0.16f, 1f);
    private readonly Image _image;
    private readonly Outline _outline;
    private readonly Button _button;
    private readonly TMP_Text _badge;
    private readonly TMP_Text _name;
    private readonly TMP_Text _state;
    private Color _baseColor;

    private QuestGraphNodeView(
        RectTransform root,
        Image image,
        Outline outline,
        Button button,
        TMP_Text badge,
        TMP_Text name,
        TMP_Text state)
    {
        Root = root;
        _image = image;
        _outline = outline;
        _button = button;
        _badge = badge;
        _name = name;
        _state = state;
    }

    public RectTransform Root { get; }
    public string? QuestId { get; private set; }

    public static QuestGraphNodeView Create(RectTransform parent)
    {
        var root = UnityUiFactory.CreateRect("PooledQuestNode", parent);
        root.anchorMin = new Vector2(0, 1);
        root.anchorMax = new Vector2(0, 1);
        root.pivot = new Vector2(0, 1);
        var button = UnityUiFactory.AddButton(root.gameObject, DefaultNodeColor);
        var image = root.GetComponent<Image>();
        var outline = root.gameObject.AddComponent<Outline>();
        outline.effectDistance = new Vector2(3, -3);
        outline.enabled = false;

        var badgeRect = UnityUiFactory.CreateRect("TraderFallback", root);
        badgeRect.anchorMin = new Vector2(0, 0.5f);
        badgeRect.anchorMax = new Vector2(0, 0.5f);
        badgeRect.pivot = new Vector2(0, 0.5f);
        badgeRect.anchoredPosition = new Vector2(10, 0);
        badgeRect.sizeDelta = new Vector2(42, 42);
        badgeRect.gameObject.AddComponent<Image>().color = new Color(0.31f, 0.29f, 0.24f, 1);
        var badge = UnityUiFactory.AddText(badgeRect.gameObject, string.Empty, 15, TextAlignmentOptions.Center, new Color(0.94f, 0.88f, 0.68f, 1));

        var nameRect = UnityUiFactory.CreateRect("Name", root);
        UnityUiFactory.Stretch(nameRect, 62, 10, 8, 34);
        var name = UnityUiFactory.AddText(nameRect.gameObject, string.Empty, 17, TextAlignmentOptions.MidlineLeft, Color.white);
        name.fontStyle = FontStyles.Bold;

        var stateRect = UnityUiFactory.CreateRect("State", root);
        stateRect.anchorMin = new Vector2(0, 0);
        stateRect.anchorMax = new Vector2(1, 0);
        stateRect.pivot = new Vector2(0.5f, 0);
        stateRect.offsetMin = new Vector2(62, 8);
        stateRect.offsetMax = new Vector2(-10, 31);
        var state = UnityUiFactory.AddText(stateRect.gameObject, string.Empty, 13, TextAlignmentOptions.MidlineLeft, new Color(0.72f, 0.75f, 0.76f, 1));
        root.gameObject.SetActive(false);
        return new QuestGraphNodeView(root, image, outline, button, badge, name, state);
    }

    public void BindStatic(QuestGraphNode node, QuestNodePosition position, Action<string> onSelected)
    {
        QuestId = node.Id;
        Root.name = $"Node-{node.Id}";
        Root.anchoredPosition = new Vector2((float)position.X, (float)-position.Y);
        Root.sizeDelta = new Vector2((float)position.Width, (float)position.Height);
        _badge.text = TraderInitials(node.TraderName);
        _name.text = node.Name;
        _button.onClick.RemoveAllListeners();
        _button.onClick.AddListener(() => onSelected(node.Id));
        Root.gameObject.SetActive(true);
    }

    public void ApplyStatus(QuestGraphNode node, QuestLiveState? state)
    {
        var kind = QuestGraphRules.ClassifyDisplayState(state?.ExactStatus, state?.HasLiveQuest == true, node.Restartable);
        _baseColor = StatusColor(kind);
        _image.color = _baseColor;
        _state.text = StateLabel(kind, state?.ExactStatus);
    }

    public void ApplySelection(QuestNodeHighlightKind highlight)
    {
        _image.color = highlight == QuestNodeHighlightKind.Selected ? SelectedNodeColor : _baseColor;
        _outline.enabled = highlight is QuestNodeHighlightKind.Prerequisite or QuestNodeHighlightKind.DirectSuccessor;
        _outline.effectColor = highlight == QuestNodeHighlightKind.Prerequisite
            ? new Color(0.38f, 0.82f, 0.45f, 0.95f)
            : new Color(0.37f, 0.64f, 0.95f, 0.95f);
    }

    public void Recycle()
    {
        _button.onClick.RemoveAllListeners();
        QuestId = null;
        Root.gameObject.SetActive(false);
    }

    private static Color StatusColor(QuestDisplayStateKind kind) => kind switch
    {
        QuestDisplayStateKind.LockedFuture => new Color(0.13f, 0.14f, 0.15f, 0.98f),
        QuestDisplayStateKind.AvailableForStart => new Color(0.25f, 0.38f, 0.25f, 0.98f),
        QuestDisplayStateKind.Started => new Color(0.20f, 0.31f, 0.43f, 0.98f),
        QuestDisplayStateKind.AvailableForFinish => new Color(0.36f, 0.35f, 0.18f, 0.98f),
        QuestDisplayStateKind.Success => new Color(0.16f, 0.29f, 0.18f, 0.98f),
        QuestDisplayStateKind.Fail or QuestDisplayStateKind.MarkedAsFailed => new Color(0.37f, 0.16f, 0.16f, 0.98f),
        QuestDisplayStateKind.FailRestartable => new Color(0.38f, 0.24f, 0.15f, 0.98f),
        _ => DefaultNodeColor,
    };

    private static string StateLabel(QuestDisplayStateKind kind, string? exactStatus) =>
        kind == QuestDisplayStateKind.LockedFuture ? "Locked future · read-only" : exactStatus ?? "Unknown";

    private static string TraderInitials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        return words.Length == 1
            ? words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant()
            : $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
    }
}
