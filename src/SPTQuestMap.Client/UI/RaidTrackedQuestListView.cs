using System;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Client.Input;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Client.Localization;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class RaidTrackedQuestListView : MonoBehaviour, IDisposable
{
    private const float Width = 430f;
    private const float ScreenEdgeMargin = 24f;
    private const float QuestColumnLeft = 48f;

    private ManualLogSource? _log;
    private Func<KeyboardShortcut>? _hotkey;
    private Func<float>? _backgroundOpacity;
    private Func<QuestTaskTextSize>? _taskTextSize;
    private Func<float>? _fadeDuration;
    private Func<float>? _displayDuration;
    private Func<RaidTrackedQuestListProjection>? _projection;
    private QuestAssetSpriteCache? _assetCache;
    private Canvas? _canvas;
    private RectTransform? _panel;
    private Image? _panelBackground;
    private RectTransform? _content;
    private ScrollRect? _scroll;
    private CanvasGroup? _group;
    private bool _shown;
    private float _shownAt;
    private float _transitionAt;
    private float _fadeStartAlpha;
    private int _contentVersion;
    private QuestTaskTextSize _appliedTaskTextSize;

    public static RaidTrackedQuestListView Create(
        Transform owner,
        ManualLogSource log,
        Func<KeyboardShortcut> hotkey,
        Func<float> backgroundOpacity,
        Func<QuestTaskTextSize> taskTextSize,
        Func<float> fadeDuration,
        Func<float> displayDuration,
        Func<RaidTrackedQuestListProjection> projection,
        QuestAssetSpriteCache assetCache)
    {
        var host = new GameObject("QuestMapRaidTrackedQuestList");
        host.transform.SetParent(owner, false);
        var view = host.AddComponent<RaidTrackedQuestListView>();
        view._log = log;
        view._hotkey = hotkey;
        view._backgroundOpacity = backgroundOpacity;
        view._taskTextSize = taskTextSize;
        view._appliedTaskTextSize = taskTextSize();
        view._fadeDuration = fadeDuration;
        view._displayDuration = displayDuration;
        view._projection = projection;
        view._assetCache = assetCache;
        view.Build();
        return view;
    }

    public void RefreshIfVisible()
    {
        if (_shown) Rebuild();
    }

    public void Hide()
    {
        _shown = false;
        _contentVersion++;
        if (_group is not null) _group.alpha = 0;
        if (_panel is not null) _panel.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        _contentVersion++;
        _log = null;
        _hotkey = null;
        _backgroundOpacity = null;
        _taskTextSize = null;
        _fadeDuration = null;
        _displayDuration = null;
        _projection = null;
        _assetCache = null;
        if (gameObject != null) Destroy(gameObject);
    }

    private void Build()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 31990;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _panel = UnityUiFactory.CreateRect("TrackedQuestPanel", transform);
        _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1, 0.5f);
        _panel.anchoredPosition = new Vector2(-28, 0);
        _panel.sizeDelta = new Vector2(Width, 40);
        _panelBackground = _panel.gameObject.AddComponent<Image>();
        _panelBackground.color = new Color(0.035f, 0.04f, 0.042f, BackgroundOpacity);
        _group = _panel.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var viewport = UnityUiFactory.CreateRect("Viewport", _panel);
        UnityUiFactory.Stretch(viewport, 10, 8, 8, 8);
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
        viewport.gameObject.AddComponent<RectMask2D>();
        _content = UnityUiFactory.CreateRect("Content", viewport);
        _content.anchorMin = new Vector2(0, 1);
        _content.anchorMax = new Vector2(1, 1);
        _content.pivot = new Vector2(0.5f, 1);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = Vector2.zero;
        _scroll = viewport.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = viewport;
        _scroll.content = _content;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.inertia = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 34f;
        _panel.gameObject.SetActive(false);
    }

    private void Update()
    {
        var taskTextSize = TaskTextSize;
        if (taskTextSize != _appliedTaskTextSize)
        {
            _appliedTaskTextSize = taskTextSize;
            if (_shown) Rebuild();
        }
        var inRaid = InRaidQuestContext.TryCapture(out _);
        if (_hotkey is not null && inRaid && QuestMapKeyboardShortcut.IsDown(_hotkey()))
        {
            if (_shown) BeginFadeOut();
            else Show();
        }
        else if (_shown && !inRaid) Hide();

        if (_panel is null || _group is null) return;
        if (_panelBackground is not null)
            _panelBackground.color = new Color(0.035f, 0.04f, 0.042f, BackgroundOpacity);
        if (_shown && Time.unscaledTime - _shownAt >= FadeDuration + DisplayDuration) BeginFadeOut();
        var elapsed = Time.unscaledTime - _transitionAt;
        _group.alpha = Mathf.Lerp(_fadeStartAlpha, _shown ? 1f : 0f, Mathf.Clamp01(elapsed / FadeDuration));
        _group.blocksRaycasts = _shown;
        _group.interactable = _shown;
        if (!_shown && _group.alpha <= 0 && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);
    }

    private void Show()
    {
        if (_panel is null || _group is null) return;
        _shown = true;
        _shownAt = Time.unscaledTime;
        _transitionAt = _shownAt;
        _fadeStartAlpha = _group.alpha;
        _panel.gameObject.SetActive(true);
        Rebuild();
    }

    private void BeginFadeOut()
    {
        if (!_shown || _group is null) return;
        _shown = false;
        _transitionAt = Time.unscaledTime;
        _fadeStartAlpha = _group.alpha;
    }

    private void Rebuild()
    {
        if (_panel is null || _content is null || _projection is null || _assetCache is null) return;
        var version = ++_contentVersion;
        foreach (Transform child in _content) Destroy(child.gameObject);
        try
        {
            var projection = _projection();
            var y = 0f;
            if (projection.Sections.Count == 0)
            {
                AddTextRow("NoTrackedQuests", ClientLocale.Text("label.noTrackedQuests"), ref y, 30, 11,
                    TextAlignmentOptions.Center, new Color(0.72f, 0.74f, 0.72f, 1));
            }
            foreach (var section in projection.Sections)
            {
                if (y > 0) y += 5f;
                BuildLocationHeader(section, ref y);
                foreach (var group in section.Groups)
                {
                    var groupTop = y;
                    foreach (var quest in group.Quests) BuildQuest(quest, ref y, QuestColumnLeft);
                    y = Math.Max(y, groupTop + 34f);
                    BuildTraderColumn(group, groupTop, y - groupTop, version);
                    y += 6f;
                }
            }

            ApplyContentGeometry(y);
            var groups = projection.Sections.Sum(section => section.Groups.Count);
            var quests = projection.Sections.Sum(section => section.Groups.Sum(group => group.Quests.Count));
            var objectives = projection.Sections.Sum(section =>
                section.Groups.Sum(group => group.Quests.Sum(quest => quest.Objectives.Count)));
            QuestMapDebugLog.Info(_log,
                "QUESTMAP_M06_RAID_TRACKED_LIST " +
                $"sections={projection.Sections.Count}; groups={groups}; quests={quests}; " +
                $"objectives={objectives}; " +
                $"contentHeight={y:0.#}; visible=True");
        }
        catch (Exception exception)
        {
            _log?.LogError($"QUESTMAP_M06_RAID_TRACKED_LIST_ERROR {exception}");
            foreach (Transform child in _content) Destroy(child.gameObject);
            var y = 0f;
            AddTextRow("TrackedQuestListError", ClientLocale.Text("label.trackedQuestListFailed"), ref y, 30, 11,
                TextAlignmentOptions.Center, new Color(0.86f, 0.45f, 0.38f, 1));
            ApplyContentGeometry(y);
        }
    }

    private void ApplyContentGeometry(float contentHeight)
    {
        if (_panel is null || _content is null) return;
        _content.sizeDelta = new Vector2(0, contentHeight);
        Canvas.ForceUpdateCanvases();
        var scaleFactor = Mathf.Max(_canvas?.scaleFactor ?? 1f, 0.001f);
        var screenHeight = (_canvas?.pixelRect.height ?? Screen.height) / scaleFactor;
        var availableHeight = Mathf.Max(40f, screenHeight - ScreenEdgeMargin);
        _panel.sizeDelta = new Vector2(Width, Mathf.Clamp(contentHeight + 16f, 40f, availableHeight));
        if (_scroll is not null)
        {
            _scroll.verticalNormalizedPosition = 1f;
        }
    }

    private void BuildLocationHeader(RaidTrackedQuestListSection section, ref float y)
    {
        var row = TopRect($"Location-{section.Scope}", y, 24f);
        row.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.115f, 0.115f, 0.96f);
        var textRoot = UnityUiFactory.CreateRect("Text", row);
        UnityUiFactory.Stretch(textRoot, 8, 8, 0, 0);
        var sectionName = section.Scope == RaidTrackedQuestListScope.Any
            ? ClientLocale.Text("common.any")
            : section.Name;
        var label = UnityUiFactory.AddText(textRoot.gameObject, sectionName, 11,
            TextAlignmentOptions.MidlineLeft, new Color(0.84f, 0.81f, 0.68f, 1f));
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        var divider = UnityUiFactory.CreateRect("Divider", row);
        divider.anchorMin = new Vector2(0, 0);
        divider.anchorMax = new Vector2(1, 0);
        divider.pivot = new Vector2(0.5f, 0);
        divider.sizeDelta = new Vector2(0, 1);
        divider.gameObject.AddComponent<Image>().color = new Color(0.42f, 0.39f, 0.27f, 0.85f);
        y += 24f;
    }

    private void BuildTraderColumn(RaidTrackedTraderGroup group, float top, float height, int version)
    {
        if (_content is null || _assetCache is null) return;
        var row = TopRect($"Trader-{group.Trader.Id}", top, height);
        var portraitRoot = UnityUiFactory.CreateRect("Portrait", row);
        portraitRoot.anchorMin = portraitRoot.anchorMax = portraitRoot.pivot = new Vector2(0, 1);
        portraitRoot.anchoredPosition = new Vector2(4, -3);
        portraitRoot.sizeDelta = new Vector2(30, 30);
        var image = portraitRoot.gameObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.13f, 0.13f, 0.9f);
        image.preserveAspect = true;
        image.raycastTarget = false;
        var fallback = UnityUiFactory.AddText(portraitRoot.gameObject, UnityUiFactory.Initials(group.Trader.Name), 9,
            TextAlignmentOptions.Center, Color.white);
        _assetCache.Request(group.Trader.ImageUrl, sprite =>
        {
            if (version != _contentVersion || image is null || sprite is null) return;
            image.sprite = sprite;
            image.color = Color.white;
            if (fallback is not null) fallback.gameObject.SetActive(false);
        });

        var divider = UnityUiFactory.CreateRect("Divider", row);
        divider.anchorMin = new Vector2(0, 0);
        divider.anchorMax = new Vector2(0, 1);
        divider.pivot = new Vector2(0.5f, 0.5f);
        divider.anchoredPosition = new Vector2(40, 0);
        divider.sizeDelta = new Vector2(1, 0);
        divider.gameObject.AddComponent<Image>().color = new Color(0.35f, 0.38f, 0.37f, 0.65f);
    }

    private void BuildQuest(RaidTrackedQuestEntry entry, ref float y, float rowLeft)
    {
        AddTextRow($"Quest-{entry.Quest.Id}", entry.Quest.Name, ref y, 20, 12,
            TextAlignmentOptions.MidlineLeft, Color.white, 8, rowLeft);
        if (entry.Objectives.Count == 0)
        {
            AddTextRow("Ready", ClientLocale.Text("state.readyToTurnIn"), ref y,
                QuestTableLayoutRules.RaidTrackedTaskRowHeight(TaskTextSize),
                QuestTableLayoutRules.RaidTrackedTaskFontSize(TaskTextSize),
                TextAlignmentOptions.MidlineLeft, QuestGraphPalette.Selected, 18, rowLeft);
            return;
        }
        foreach (var objective in entry.Objectives)
        {
            if (IsNumeric(objective.Progress)) AddObjectiveProgress(objective, ref y, rowLeft);
            else AddTextRow($"Task-{objective.Definition.Id}", objective.Definition.Text, ref y,
                QuestTableLayoutRules.RaidTrackedTaskRowHeight(TaskTextSize),
                QuestTableLayoutRules.RaidTrackedTaskFontSize(TaskTextSize),
                TextAlignmentOptions.MidlineLeft, new Color(0.90f, 0.91f, 0.89f, 1), 18, rowLeft);
        }
    }

    private void AddObjectiveProgress(RaidTrackedObjectiveEntry objective, ref float y, float rowLeft)
    {
        if (_content is null || objective.Progress is null) return;
        var rowHeight = QuestTableLayoutRules.RaidTrackedProgressRowHeight(TaskTextSize);
        var row = TopRect($"Task-{objective.Definition.Id}", y, rowHeight, rowLeft);
        var track = UnityUiFactory.CreateRect("Track", row);
        UnityUiFactory.Stretch(track, 18, 4, 3, 3);
        track.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.18f, 0.18f, 0.90f);
        var ratio = (float)Math.Clamp(
            CappedCurrent(objective.Progress) / objective.Progress.Required!.Value, 0d, 1d);
        var fill = UnityUiFactory.CreateRect("Fill", track);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(ratio, 1);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        var progressColor = QuestGraphPalette.InProgress;
        progressColor.a = 0.88f;
        fill.gameObject.AddComponent<Image>().color = progressColor;
        var label = UnityUiFactory.AddText(track.gameObject,
            ClientLocale.Format("common.progressCompact", ClientLocale.Arg("current", CappedCurrent(objective.Progress)),
                ClientLocale.Arg("required", objective.Progress.Required.Value), ClientLocale.Arg("text", objective.Definition.Text)),
            QuestTableLayoutRules.RaidTrackedProgressFontSize(TaskTextSize),
            TextAlignmentOptions.Center, Color.white);
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        y += rowHeight;
    }

    private void AddTextRow(
        string name,
        string value,
        ref float y,
        float height,
        float fontSize,
        TextAlignmentOptions alignment,
        Color color,
        float left = 0,
        float rowLeft = 0)
    {
        var row = TopRect(name, y, height, rowLeft);
        var textRoot = UnityUiFactory.CreateRect("Text", row);
        UnityUiFactory.Stretch(textRoot, left, 4, 0, 0);
        var text = UnityUiFactory.AddText(textRoot.gameObject, value, fontSize, alignment, color);
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        y += height;
    }

    private RectTransform TopRect(string name, float top, float height, float left = 0)
    {
        var rect = UnityUiFactory.CreateRect(name, _content!);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(0, -top);
        return rect;
    }

    private float FadeDuration => Mathf.Clamp(_fadeDuration?.Invoke() ?? 0.3f, 0.05f, 3f);

    private float DisplayDuration => Mathf.Clamp(_displayDuration?.Invoke() ?? 8f, 1f, 60f);

    private float BackgroundOpacity => Mathf.Clamp(_backgroundOpacity?.Invoke() ?? 0.82f, 0.2f, 1f);

    private QuestTaskTextSize TaskTextSize => _taskTextSize?.Invoke() ?? QuestTaskTextSize.Small;

    private static bool IsNumeric(QuestObjectiveProgress? progress) => progress?.ProgressKnown == true
        && progress.Current.HasValue && progress.Required is > 1d;

    private static double CappedCurrent(QuestObjectiveProgress progress) =>
        QuestProgressRules.CapCurrent(progress.Current, progress.Required).GetValueOrDefault();

}
