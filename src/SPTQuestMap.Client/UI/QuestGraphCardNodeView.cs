using System;
using System.Linq;
using SPTQuestMap.Client.Localization;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestGraphCardNodeView
{
    private static readonly Color CardColor = new(0.10f, 0.115f, 0.12f, 0.98f);
    private static readonly Color SelectedColor = new(0.34f, 0.27f, 0.11f, 1f);
    private readonly QuestAssetSpriteCache _assets;
    private readonly Image _background;
    private readonly Image _statusRail;
    private readonly Image _questArt;
    private readonly Image _mapArt;
    private readonly Image _portrait;
    private readonly TMP_Text _portraitFallback;
    private readonly Image _collectorRoute;
    private readonly Image _lightkeeperRoute;
    private readonly Image _terminalMarker;
    private readonly QuestCompletedMarkerGraphic _completedMarker;
    private readonly GameObject _scavBadge;
    private readonly Outline _outline;
    private readonly Button _button;
    private readonly QuestNodeClickHandler _clickHandler;
    private readonly TMP_Text _name;
    private readonly TMP_Text _state;
    private float _visualAlpha = 1f;
    private bool _isScavRepeatable;

    private QuestGraphCardNodeView(
        RectTransform root,
        QuestAssetSpriteCache assets,
        Image background,
        Image statusRail,
        Image questArt,
        Image mapArt,
        Image portrait,
        TMP_Text portraitFallback,
        Image collectorRoute,
        Image lightkeeperRoute,
        Image terminalMarker,
        QuestCompletedMarkerGraphic completedMarker,
        GameObject scavBadge,
        Outline outline,
        Button button,
        QuestNodeClickHandler clickHandler,
        TMP_Text name,
        TMP_Text state)
    {
        Root = root;
        _assets = assets;
        _background = background;
        _statusRail = statusRail;
        _questArt = questArt;
        _mapArt = mapArt;
        _portrait = portrait;
        _portraitFallback = portraitFallback;
        _collectorRoute = collectorRoute;
        _lightkeeperRoute = lightkeeperRoute;
        _terminalMarker = terminalMarker;
        _completedMarker = completedMarker;
        _scavBadge = scavBadge;
        _outline = outline;
        _button = button;
        _clickHandler = clickHandler;
        _name = name;
        _state = state;
    }

    public RectTransform Root { get; }
    public string? QuestId { get; private set; }

    public static QuestGraphCardNodeView Create(RectTransform parent, QuestAssetSpriteCache assets)
    {
        var root = UnityUiFactory.CreateRect("PooledQuestNode", parent);
        root.anchorMin = new Vector2(0, 1);
        root.anchorMax = new Vector2(0, 1);
        root.pivot = new Vector2(0, 1);
        var button = UnityUiFactory.AddButton(root.gameObject, CardColor);
        root.gameObject.AddComponent<RectMask2D>();
        var clickHandler = root.gameObject.AddComponent<QuestNodeClickHandler>();
        var background = root.GetComponent<Image>();
        var outline = root.gameObject.AddComponent<Outline>();
        outline.effectDistance = new Vector2(3, -3);
        outline.enabled = false;

        var questArt = AddImage("QuestArt", root, Vector2.zero, Vector2.one, new Color(1, 1, 1, 0.62f), false);
        questArt.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        var mapArt = AddImage("MapBanner", root, Vector2.zero, Vector2.one, Color.clear, false);
        AddImage("ArtShade", root, Vector2.zero, Vector2.one, new Color(0.02f, 0.025f, 0.028f, 0.42f), true);

        var rail = AddImage("StatusRail", root, Vector2.zero, new Vector2(0, 1), Color.white, true);
        rail.rectTransform.pivot = new Vector2(0, 0.5f);
        rail.rectTransform.sizeDelta = new Vector2(7, 0);

        var portraitRoot = UnityUiFactory.CreateRect("Trader", root);
        portraitRoot.anchorMin = portraitRoot.anchorMax = new Vector2(0, 0.5f);
        portraitRoot.pivot = new Vector2(0, 0.5f);
        portraitRoot.anchoredPosition = new Vector2(14, 0);
        portraitRoot.sizeDelta = new Vector2(46, 46);
        portraitRoot.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.24f, 0.20f, 0.96f);
        var fallback = UnityUiFactory.AddText(portraitRoot.gameObject, string.Empty, 15, TextAlignmentOptions.Center, new Color(0.94f, 0.88f, 0.68f, 1));
        var portrait = AddImage("Portrait", portraitRoot, Vector2.zero, Vector2.one, Color.white, false);
        portrait.rectTransform.offsetMin = new Vector2(2, 2);
        portrait.rectTransform.offsetMax = new Vector2(-2, -2);

        var nameRect = UnityUiFactory.CreateRect("Name", root);
        UnityUiFactory.Stretch(nameRect, 70, 10, 8, 35);
        var name = UnityUiFactory.AddText(nameRect.gameObject, string.Empty, 17, TextAlignmentOptions.MidlineLeft, Color.white);
        name.fontStyle = FontStyles.Bold;
        AddShadow(name.gameObject);

        var stateRect = UnityUiFactory.CreateRect("State", root);
        stateRect.anchorMin = new Vector2(0, 0);
        stateRect.anchorMax = new Vector2(1, 0);
        stateRect.pivot = new Vector2(0.5f, 0);
        stateRect.offsetMin = new Vector2(70, 14);
        stateRect.offsetMax = new Vector2(-10, 34);
        var state = UnityUiFactory.AddText(stateRect.gameObject, string.Empty, 13, TextAlignmentOptions.MidlineLeft, new Color(0.82f, 0.83f, 0.80f, 1));
        AddShadow(state.gameObject);

        var routes = UnityUiFactory.CreateRect("Routes", root);
        routes.anchorMin = new Vector2(0, 0);
        routes.anchorMax = new Vector2(1, 0);
        routes.pivot = new Vector2(0.5f, 0);
        routes.offsetMin = new Vector2(14, 4);
        routes.offsetMax = new Vector2(-10, 10);
        var collector = AddImage("Collector", routes, Vector2.zero, Vector2.one, QuestGraphPalette.Collector, true);
        var lightkeeper = AddImage("Lightkeeper", routes, Vector2.zero, Vector2.one, QuestGraphPalette.Lightkeeper, true);
        UnityUiFactory.AddText(collector.gameObject, ClientLocale.Text("legend.collector"), 8, TextAlignmentOptions.Center, Color.white);
        UnityUiFactory.AddText(lightkeeper.gameObject, ClientLocale.Text("legend.lightkeeper"), 8, TextAlignmentOptions.Center, Color.white);

        var terminal = AddImage("Terminal", root, new Vector2(1, 0), Vector2.one, QuestGraphPalette.Terminal, false);
        terminal.rectTransform.pivot = new Vector2(1, 0.5f);
        terminal.rectTransform.sizeDelta = new Vector2(5, -18);

        var completedRoot = UnityUiFactory.CreateRect("Completed", root);
        completedRoot.anchorMin = completedRoot.anchorMax = completedRoot.pivot = new Vector2(1, 1);
        completedRoot.sizeDelta = new Vector2(38, 38);
        var completedMarker = completedRoot.gameObject.AddComponent<QuestCompletedMarkerGraphic>();
        completedMarker.raycastTarget = false;
        completedRoot.gameObject.SetActive(false);

        var scavBadge = UnityUiFactory.CreateRect("ScavBadge", root);
        scavBadge.anchorMin = scavBadge.anchorMax = scavBadge.pivot = new Vector2(1, 1);
        scavBadge.anchoredPosition = new Vector2(-45, -7);
        scavBadge.sizeDelta = new Vector2(48, 18);
        scavBadge.gameObject.AddComponent<Image>().color = new Color(0.40f, 0.34f, 0.16f, 0.98f);
        var scavText = UnityUiFactory.AddText(scavBadge.gameObject, ClientLocale.Text("common.scav"), 9,
            TextAlignmentOptions.Center, new Color(1f, 0.96f, 0.78f, 1f));
        scavText.fontStyle = FontStyles.Bold;
        scavBadge.gameObject.SetActive(false);

        root.gameObject.SetActive(false);
        return new QuestGraphCardNodeView(root, assets, background, rail, questArt, mapArt, portrait, fallback, collector, lightkeeper, terminal, completedMarker, scavBadge.gameObject, outline, button, clickHandler, name, state);
    }

    public void BindStatic(
        QuestGraphNode node,
        QuestNodePosition position,
        bool collector,
        bool lightkeeper,
        bool terminal,
        Action<string> selected,
        Action<string>? focusRequested)
    {
        QuestId = node.Id;
        Root.name = $"Node-{node.Id}";
        Root.anchoredPosition = new Vector2((float)position.X, (float)-position.Y);
        Root.sizeDelta = new Vector2((float)position.Width, (float)position.Height);
        _portraitFallback.text = UnityUiFactory.Initials(node.TraderName);
        _portraitFallback.gameObject.SetActive(true);
        _name.text = node.Name;
        _isScavRepeatable = node.ScavRepeatable;
        _scavBadge.SetActive(_isScavRepeatable);
        SetRoutes(collector, lightkeeper);
        _terminalMarker.gameObject.SetActive(terminal);
        _visualAlpha = 1f;
        BindImage(node.Id, node.ImageUrl, _questArt, 0.62f, false);
        Clear(_mapArt);
        BindImage(node.Id, node.TraderImageUrl, _portrait, 1, true);
        _button.onClick.RemoveAllListeners();
        _clickHandler.Bind(node.Id, selected, focusRequested);
        QuestMapNativeTooltips.Bind(Root.gameObject, () => focusRequested is null
            ? string.Empty
            : ClientLocale.Format("tooltip.doubleClickFocus", ClientLocale.Arg("quest", node.Name)));
        Root.gameObject.SetActive(true);
    }

    public void ApplyStatus(QuestGraphTopology topology, QuestGraphNode node, QuestProfileOverlay overlay, QuestLiveState? state)
    {
        var kind = QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay);
        var deEmphasized = kind is QuestMapDisplayStateKind.Locked or QuestMapDisplayStateKind.PrerequisiteGated
            or QuestMapDisplayStateKind.PrestigeGated
            or QuestMapDisplayStateKind.LevelGated or QuestMapDisplayStateKind.TraderGated
            or QuestMapDisplayStateKind.TraderUnavailable or QuestMapDisplayStateKind.Completed
            or QuestMapDisplayStateKind.Excluded;
        _visualAlpha = deEmphasized ? 0.42f : 1f;
        _statusRail.color = QuestGraphPalette.Status(kind);
        _background.color = CardColor;
        _name.color = deEmphasized ? new Color(0.58f, 0.59f, 0.57f, 1) : Color.white;
        _state.color = deEmphasized ? new Color(0.43f, 0.45f, 0.44f, 1) : new Color(0.82f, 0.83f, 0.80f, 1);
        if (_questArt.sprite is not null) _questArt.color = new Color(1, 1, 1, 0.62f * _visualAlpha);
        if (_portrait.sprite is not null) _portrait.color = new Color(1, 1, 1, _visualAlpha);
        _portraitFallback.color = deEmphasized ? new Color(0.55f, 0.53f, 0.46f, 1) : new Color(0.94f, 0.88f, 0.68f, 1);
        _completedMarker.gameObject.SetActive(kind == QuestMapDisplayStateKind.Completed);
        var status = StateLabel(kind);
        var progressPercent = kind == QuestMapDisplayStateKind.InProgress
            ? QuestGraphRules.ResolveProfileProgressPercent(node.Id, overlay)
            : null;
        var progress = progressPercent.HasValue
            ? ClientLocale.Format("common.inlineDetail", ClientLocale.Arg("detail",
                ClientLocale.Format("common.approximatePercent", ClientLocale.Arg("percent", progressPercent.Value))))
            : string.Empty;
        var handover = state?.HandoverReady == true
            ? ClientLocale.Format("common.inlineDetail", ClientLocale.Arg("detail", ClientLocale.Text("label.handoverReady")))
            : string.Empty;
        var expirationTime = overlay.RepeatableEndTimes.TryGetValue(node.Id, out var serverEnd)
            ? serverEnd
            : state?.ExpirationTime;
        var expiry = expirationTime is long end
            ? ClientLocale.Format("common.inlineDetail", ClientLocale.Arg("detail",
                ClientLocale.Format("common.expires", ClientLocale.Arg("remaining", FormatRemaining(end)))))
            : string.Empty;
        _state.text = status + progress + handover + expiry;
    }

    public void ApplySelection(QuestNodeHighlightKind highlight, bool selectionActive)
    {
        _background.color = selectionActive && highlight == QuestNodeHighlightKind.None
            ? new Color(0.065f, 0.07f, 0.075f, 0.72f)
            : highlight == QuestNodeHighlightKind.Selected ? SelectedColor : CardColor;
        _outline.enabled = highlight != QuestNodeHighlightKind.None;
        _outline.effectColor = highlight switch
        {
            QuestNodeHighlightKind.Selected => QuestGraphPalette.Selected,
            QuestNodeHighlightKind.Prerequisite => QuestGraphPalette.Prerequisite,
            _ => QuestGraphPalette.Successor,
        };
    }

    public void SetOverview(bool overview)
    {
        _questArt.gameObject.SetActive(!overview && _questArt.sprite is not null);
        _mapArt.gameObject.SetActive(!overview && _mapArt.sprite is not null);
        _portrait.gameObject.SetActive(!overview && _portrait.sprite is not null);
        _portraitFallback.gameObject.SetActive(!overview && _portrait.sprite is null);
        _state.gameObject.SetActive(!overview);
        _scavBadge.SetActive(!overview && _isScavRepeatable);
        _name.fontSize = overview ? 20 : 17;
    }

    public void Recycle()
    {
        _button.onClick.RemoveAllListeners();
        _clickHandler.Clear();
        QuestId = null;
        Clear(_questArt);
        Clear(_mapArt);
        Clear(_portrait);
        _terminalMarker.gameObject.SetActive(false);
        _completedMarker.gameObject.SetActive(false);
        _isScavRepeatable = false;
        _scavBadge.SetActive(false);
        Root.gameObject.SetActive(false);
    }

    private void BindImage(string questId, string? url, Image image, float alpha, bool portrait)
    {
        Clear(image);
        _assets.Request(url, sprite =>
        {
            if (!string.Equals(QuestId, questId, StringComparison.Ordinal) || sprite is null) return;
            image.sprite = sprite;
            if (portrait)
            {
                image.preserveAspect = true;
            }
            else
            {
                image.preserveAspect = false;
                var fitter = image.GetComponent<AspectRatioFitter>();
                if (fitter is not null) fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            }
            image.color = new Color(1, 1, 1, alpha * _visualAlpha);
            image.gameObject.SetActive(true);
            if (portrait) _portraitFallback.gameObject.SetActive(false);
        });
    }

    private void SetRoutes(bool collector, bool lightkeeper)
    {
        _collectorRoute.gameObject.SetActive(collector);
        _lightkeeperRoute.gameObject.SetActive(lightkeeper);
        SetAnchors(_collectorRoute.rectTransform, 0, collector && lightkeeper ? 0.5f : 1);
        SetAnchors(_lightkeeperRoute.rectTransform, collector && lightkeeper ? 0.5f : 0, 1);
    }

    private static Image AddImage(string name, RectTransform parent, Vector2 minimum, Vector2 maximum, Color color, bool active)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = minimum;
        rect.anchorMax = maximum;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        image.gameObject.SetActive(active);
        return image;
    }

    private static void SetAnchors(RectTransform rect, float minimum, float maximum)
    {
        rect.anchorMin = new Vector2(minimum, 0);
        rect.anchorMax = new Vector2(maximum, 1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void AddShadow(GameObject target)
    {
        var shadow = target.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.92f);
        shadow.effectDistance = new Vector2(1, -1);
    }

    private static void Clear(Image image)
    {
        image.sprite = null;
        image.gameObject.SetActive(false);
    }

    internal static string StateLabel(QuestMapDisplayStateKind kind)
    {
        return kind switch
        {
            QuestMapDisplayStateKind.PrerequisiteGated => ClientLocale.Text("state.prerequisiteGated"),
            QuestMapDisplayStateKind.PrestigeGated => ClientLocale.Text("state.prestigeGated"),
            QuestMapDisplayStateKind.LevelGated => ClientLocale.Text("state.levelGated"),
            QuestMapDisplayStateKind.TraderGated => ClientLocale.Text("state.traderGated"),
            QuestMapDisplayStateKind.TraderUnavailable => ClientLocale.Text("state.traderUnavailable"),
            QuestMapDisplayStateKind.Available => ClientLocale.Text("state.available"),
            QuestMapDisplayStateKind.InProgress => ClientLocale.Text("state.inProgress"),
            QuestMapDisplayStateKind.ReadyToFinish => ClientLocale.Text("state.readyToFinish"),
            QuestMapDisplayStateKind.Completed => ClientLocale.Text("state.completed"),
            QuestMapDisplayStateKind.Failed => ClientLocale.Text("state.failed"),
            QuestMapDisplayStateKind.Excluded => ClientLocale.Text("state.excluded"),
            QuestMapDisplayStateKind.RestartableFailure => ClientLocale.Text("state.restartableFailure"),
            QuestMapDisplayStateKind.Expired => ClientLocale.Text("state.expired"),
            QuestMapDisplayStateKind.Pending => ClientLocale.Text("state.pending"),
            QuestMapDisplayStateKind.Unknown => ClientLocale.Text("state.unknown"),
            _ => ClientLocale.Text("state.locked"),
        };
    }

    private static string FormatRemaining(long expirationTime)
    {
        var seconds = Math.Max(0, expirationTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return ClientLocale.Format("common.remainingHours", ClientLocale.Arg("hours", seconds / 3600),
            ClientLocale.Arg("minutes", seconds % 3600 / 60));
    }

}

internal sealed class QuestNodeClickHandler : MonoBehaviour, IPointerClickHandler
{
    private const float SingleClickDelaySeconds = 0.24f;
    private string? _questId;
    private Action<string>? _selected;
    private Action<string>? _focusRequested;
    private Coroutine? _pendingSingleClick;

    public void Bind(string questId, Action<string> selected, Action<string>? focusRequested)
    {
        Clear();
        _questId = questId;
        _selected = selected;
        _focusRequested = focusRequested;
    }

    public void Clear()
    {
        if (_pendingSingleClick is not null) StopCoroutine(_pendingSingleClick);
        _pendingSingleClick = null;
        _questId = null;
        _selected = null;
        _focusRequested = null;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || _questId is null) return;
        if (_focusRequested is null)
        {
            _selected?.Invoke(_questId);
            return;
        }
        if (eventData.clickCount >= 2)
        {
            if (_pendingSingleClick is not null) StopCoroutine(_pendingSingleClick);
            _pendingSingleClick = null;
            _focusRequested?.Invoke(_questId);
            return;
        }

        if (_pendingSingleClick is not null) StopCoroutine(_pendingSingleClick);
        _pendingSingleClick = StartCoroutine(DispatchSingleClick());
    }

    private System.Collections.IEnumerator DispatchSingleClick()
    {
        yield return new WaitForSecondsRealtime(SingleClickDelaySeconds);
        _pendingSingleClick = null;
        if (_questId is not null) _selected?.Invoke(_questId);
    }

    private void OnDisable()
    {
        if (_pendingSingleClick is not null) StopCoroutine(_pendingSingleClick);
        _pendingSingleClick = null;
    }
}

internal sealed class QuestCompletedMarkerGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        var rect = GetPixelAdjustedRect();
        var first = helper.currentVertCount;
        helper.AddVert(new Vector2(rect.xMin, rect.yMax), QuestGraphPalette.CompletedMarker, Vector2.zero);
        helper.AddVert(new Vector2(rect.xMax, rect.yMax), QuestGraphPalette.CompletedMarker, Vector2.zero);
        helper.AddVert(new Vector2(rect.xMax, rect.yMin), QuestGraphPalette.CompletedMarker, Vector2.zero);
        helper.AddTriangle(first, first + 1, first + 2);

        var center = new Vector2(rect.xMax - rect.width / 3f, rect.yMax - rect.height / 3f);
        AddLine(helper, center + new Vector2(-7, 0), center + new Vector2(-2, -5), 3.5f, Color.white);
        AddLine(helper, center + new Vector2(-2, -5), center + new Vector2(8, 7), 3.5f, Color.white);
    }

    private static void AddLine(VertexHelper helper, Vector2 start, Vector2 end, float width, Color color)
    {
        var direction = end - start;
        var normal = new Vector2(-direction.y, direction.x).normalized * width / 2f;
        var first = helper.currentVertCount;
        helper.AddVert(start - normal, color, Vector2.zero);
        helper.AddVert(start + normal, color, Vector2.zero);
        helper.AddVert(end + normal, color, Vector2.zero);
        helper.AddVert(end - normal, color, Vector2.zero);
        helper.AddTriangle(first, first + 1, first + 2);
        helper.AddTriangle(first, first + 2, first + 3);
    }
}
