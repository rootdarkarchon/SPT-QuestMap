using System;
using BepInEx.Logging;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Client.Localization;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class RaidQuestProgressNotificationView : MonoBehaviour, IDisposable
{
    private RectTransform? _root;
    private CanvasGroup? _group;
    private Func<float>? _opacity;
    private Func<bool>? _minimal;
    private Func<QuestTaskTextSize>? _taskTextSize;
    private Func<float>? _fadeDuration;
    private Func<float>? _displayDuration;
    private QuestAssetSpriteCache? _assetCache;
    private CanvasGroup? _backgroundGroup;
    private Image? _background;
    private Image? _questArt;
    private Image? _portrait;
    private TextMeshProUGUI? _portraitFallback;
    private TextMeshProUGUI? _questName;
    private Image? _statusRail;
    private RectTransform? _shade;
    private RectTransform? _portraitRoot;
    private Outline? _border;
    private TextMeshProUGUI? _task;
    private TextMeshProUGUI? _count;
    private RectTransform? _progressRoot;
    private RectTransform? _progressFill;
    private float _shownAt;
    private float _fadeStartAlpha;
    private int _contentVersion;
    private bool _displaying;
    private bool _appliedMinimal;
    private QuestTaskTextSize _appliedTaskTextSize;

    public static RaidQuestProgressNotificationView Create(
        Transform owner,
        ManualLogSource log,
        Func<float> opacity,
        Func<bool> minimal,
        Func<QuestTaskTextSize> taskTextSize,
        Func<float> fadeDuration,
        Func<float> displayDuration,
        QuestAssetSpriteCache assetCache)
    {
        var host = new GameObject("QuestMapRaidProgressNotifications", typeof(RectTransform));
        host.transform.SetParent(owner, false);
        var view = host.AddComponent<RaidQuestProgressNotificationView>();
        view._opacity = opacity;
        view._minimal = minimal;
        view._taskTextSize = taskTextSize;
        view._fadeDuration = fadeDuration;
        view._displayDuration = displayDuration;
        view._assetCache = assetCache;
        view.Build(log);
        return view;
    }

    public bool Displaying => _displaying;

    public float CardHeight => _root?.rect.height > 0 ? _root.rect.height : (_minimal?.Invoke() == true ? 54f : 94f);

    public void SetStackTop(float top)
    {
        if (_root is not null) _root.anchoredPosition = new Vector2(-28, -top);
    }

    public void Show(InRaidQuestProgressChange change)
    {
        if (_root is null || _group is null || _assetCache is null) return;
        var version = ++_contentVersion;
        var node = change.Quest;
        var progress = change.Progress;
        var minimal = _minimal?.Invoke() == true;
        ApplyPresentationMode(minimal, TaskTextSize);

        _questName!.text = node.Name;
        var statusColor = QuestGraphPalette.Status(DisplayState(change.ExactStatus));
        if (_statusRail is not null) _statusRail.color = statusColor;
        var objectiveText = change.Objective?.Text ?? ClientLocale.Text("label.questStatusChanged");
        _task!.text = minimal && progress?.ProgressKnown == true
            && progress.Current.HasValue && progress.Required.HasValue
                ? ClientLocale.Format("common.progressCompact", ClientLocale.Arg("current", CappedCurrent(progress)),
                    ClientLocale.Arg("required", progress.Required.Value), ClientLocale.Arg("text", objectiveText))
                : objectiveText;
        _task.color = minimal ? Color.white : progress?.Complete == true
            ? QuestGraphPalette.Completed : Color.white;

        var hasCount = progress?.ProgressKnown == true
            && progress.Current.HasValue
            && progress.Required.HasValue;
        _count!.text = hasCount
            ? ClientLocale.Format("common.progress", ClientLocale.Arg("current", CappedCurrent(progress!)),
                ClientLocale.Arg("required", progress!.Required!.Value))
            : string.Empty;

        var percent = progress?.Required is > 1d ? ObjectivePercent(progress) : null;
        _progressRoot!.gameObject.SetActive(!minimal && percent.HasValue);
        if (percent.HasValue)
        {
            _progressFill!.anchorMax = new Vector2((float)(percent.Value / 100d), 1);
            _progressFill.gameObject.GetComponent<Image>().color = progress?.Complete == true
                ? QuestGraphPalette.Status(QuestMapDisplayStateKind.ReadyToFinish)
                : QuestGraphPalette.Status(QuestMapDisplayStateKind.InProgress);
        }

        _questArt!.sprite = null;
        _questArt.gameObject.SetActive(false);
        if (!minimal) _assetCache.Request(node.ImageUrl, sprite =>
        {
            if (version != _contentVersion || _questArt is null || sprite is null) return;
            _questArt.sprite = sprite;
            var fitter = _questArt.GetComponent<AspectRatioFitter>();
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            _questArt.gameObject.SetActive(true);
        });

        _portrait!.sprite = null;
        _portrait.gameObject.SetActive(false);
        _portraitFallback!.text = UnityUiFactory.Initials(node.TraderName);
        _portraitFallback.gameObject.SetActive(!minimal);
        if (!minimal) _assetCache.Request(node.TraderImageUrl, sprite =>
        {
            if (version != _contentVersion || _portrait is null || sprite is null) return;
            _portrait.sprite = sprite;
            _portrait.gameObject.SetActive(true);
            if (_portraitFallback is not null) _portraitFallback.gameObject.SetActive(false);
        });

        _fadeStartAlpha = _displaying ? _group.alpha : 0f;
        _shownAt = Time.unscaledTime;
        _displaying = true;
        _root.gameObject.SetActive(true);
    }

    public void Dispose()
    {
        _contentVersion++;
        if (gameObject != null) Destroy(gameObject);
        _root = null;
        _group = null;
        _assetCache = null;
        _opacity = null;
        _minimal = null;
        _taskTextSize = null;
        _fadeDuration = null;
        _displayDuration = null;
    }

    public void Hide()
    {
        _contentVersion++;
        _displaying = false;
        if (_group is not null) _group.alpha = 0;
        if (_root is not null) _root.gameObject.SetActive(false);
    }

    private void Build(ManualLogSource log)
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _root = UnityUiFactory.CreateRect("NotificationCard", transform);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1, 1);
        _root.anchoredPosition = new Vector2(-28, -28);
        _root.sizeDelta = new Vector2(390, 94);
        _root.gameObject.AddComponent<RectMask2D>();
        _group = _root.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        var backgroundRoot = UnityUiFactory.CreateRect("BackgroundLayers", _root);
        UnityUiFactory.Stretch(backgroundRoot);
        _background = backgroundRoot.gameObject.AddComponent<Image>();
        _background.color = new Color(0.035f, 0.04f, 0.042f, 0.99f);
        _background.raycastTarget = false;
        _backgroundGroup = backgroundRoot.gameObject.AddComponent<CanvasGroup>();
        _backgroundGroup.interactable = false;
        _backgroundGroup.blocksRaycasts = false;

        var artRoot = UnityUiFactory.CreateRect("QuestArt", backgroundRoot);
        UnityUiFactory.Stretch(artRoot);
        _questArt = artRoot.gameObject.AddComponent<Image>();
        _questArt.raycastTarget = false;
        _questArt.preserveAspect = false;
        var artFitter = artRoot.gameObject.AddComponent<AspectRatioFitter>();
        artFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        artRoot.gameObject.SetActive(false);

        _shade = UnityUiFactory.CreateRect("Shade", backgroundRoot);
        UnityUiFactory.Stretch(_shade);
        _shade.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.013f, 0.014f, 0.62f);

        var statusRail = UnityUiFactory.CreateRect("StatusRail", _root);
        statusRail.anchorMin = Vector2.zero;
        statusRail.anchorMax = new Vector2(0, 1);
        statusRail.pivot = new Vector2(0, 0.5f);
        statusRail.sizeDelta = new Vector2(6, 0);
        _statusRail = statusRail.gameObject.AddComponent<Image>();
        _statusRail.color = QuestGraphPalette.Status(QuestMapDisplayStateKind.InProgress);

        _portraitRoot = UnityUiFactory.CreateRect("Trader", _root);
        _portraitRoot.anchorMin = _portraitRoot.anchorMax = new Vector2(0, 0.5f);
        _portraitRoot.pivot = new Vector2(0, 0.5f);
        _portraitRoot.anchoredPosition = new Vector2(16, 3);
        _portraitRoot.sizeDelta = new Vector2(56, 56);
        _portraitRoot.gameObject.AddComponent<Image>().color = new Color(0.20f, 0.19f, 0.16f, 0.98f);
        _portraitFallback = UnityUiFactory.AddText(_portraitRoot.gameObject, string.Empty, 16,
            TextAlignmentOptions.Center, new Color(0.94f, 0.88f, 0.68f, 1));
        var portraitImageRoot = UnityUiFactory.CreateRect("Image", _portraitRoot);
        UnityUiFactory.Stretch(portraitImageRoot, 2, 2, 2, 2);
        _portrait = portraitImageRoot.gameObject.AddComponent<Image>();
        _portrait.preserveAspect = true;
        _portrait.raycastTarget = false;
        portraitImageRoot.gameObject.SetActive(false);

        var titleRoot = UnityUiFactory.CreateRect("QuestName", _root);
        titleRoot.anchorMin = new Vector2(0, 1);
        titleRoot.anchorMax = new Vector2(1, 1);
        titleRoot.pivot = new Vector2(0.5f, 1);
        titleRoot.offsetMin = new Vector2(84, -38);
        titleRoot.offsetMax = new Vector2(-12, -8);
        _questName = UnityUiFactory.AddText(titleRoot.gameObject, string.Empty, 16,
            TextAlignmentOptions.MidlineLeft, Color.white);
        _questName.fontStyle = FontStyles.Bold;
        _questName.enableWordWrapping = false;
        _questName.overflowMode = TextOverflowModes.Ellipsis;
        _questName.outlineColor = Color.black;
        _questName.outlineWidth = 0.14f;

        var taskRoot = UnityUiFactory.CreateRect("Task", _root);
        taskRoot.anchorMin = new Vector2(0, 0);
        taskRoot.anchorMax = new Vector2(1, 1);
        taskRoot.offsetMin = new Vector2(84, 24);
        taskRoot.offsetMax = new Vector2(-12, -39);
        _task = UnityUiFactory.AddText(taskRoot.gameObject, string.Empty, 11,
            TextAlignmentOptions.MidlineLeft, Color.white);
        _task.enableWordWrapping = true;
        _task.overflowMode = TextOverflowModes.Ellipsis;

        _progressRoot = UnityUiFactory.CreateRect("Progress", _root);
        _progressRoot.anchorMin = new Vector2(0, 0);
        _progressRoot.anchorMax = new Vector2(1, 0);
        _progressRoot.pivot = new Vector2(0.5f, 0);
        _progressRoot.offsetMin = new Vector2(84, 8);
        _progressRoot.offsetMax = new Vector2(-12, 21);
        _progressRoot.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.20f, 0.20f, 0.94f);
        _progressFill = UnityUiFactory.CreateRect("Fill", _progressRoot);
        _progressFill.anchorMin = Vector2.zero;
        _progressFill.anchorMax = new Vector2(0, 1);
        _progressFill.offsetMin = Vector2.zero;
        _progressFill.offsetMax = Vector2.zero;
        _progressFill.gameObject.AddComponent<Image>().color = QuestGraphPalette.Status(QuestMapDisplayStateKind.InProgress);
        var countRoot = UnityUiFactory.CreateRect("Count", _progressRoot);
        UnityUiFactory.Stretch(countRoot, 4, 4, 0, 0);
        _count = UnityUiFactory.AddText(countRoot.gameObject, string.Empty, 10,
            TextAlignmentOptions.Center, Color.white);
        _count.fontStyle = FontStyles.Bold;
        _count.outlineColor = Color.black;
        _count.outlineWidth = 0.18f;

        _border = _root.gameObject.AddComponent<Outline>();
        _border.effectColor = QuestGraphPalette.Border;
        _border.effectDistance = Vector2.one;

        if (_assetCache is null)
        {
            _assetCache = gameObject.AddComponent<QuestAssetSpriteCache>();
            _assetCache.Bind(log);
        }
        _root.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!_displaying || _root is null || _group is null) return;
        var minimal = _minimal?.Invoke() == true;
        var taskTextSize = TaskTextSize;
        if (minimal != _appliedMinimal || taskTextSize != _appliedTaskTextSize)
            ApplyPresentationMode(minimal, taskTextSize);
        ApplyBackgroundOpacity(minimal);
        var elapsed = Time.unscaledTime - _shownAt;
        var fadeDuration = FadeDuration;
        var displayDuration = DisplayDuration;
        if (elapsed < fadeDuration)
        {
            _group.alpha = Mathf.Lerp(_fadeStartAlpha, 1f, elapsed / fadeDuration);
            return;
        }
        if (elapsed < fadeDuration + displayDuration)
        {
            _group.alpha = 1f;
            return;
        }
        if (elapsed < fadeDuration + displayDuration + fadeDuration)
        {
            var fadeElapsed = elapsed - fadeDuration - displayDuration;
            _group.alpha = 1f - fadeElapsed / fadeDuration;
            return;
        }

        _group.alpha = 0;
        _displaying = false;
        _root.gameObject.SetActive(false);
    }

    private static double CappedCurrent(QuestObjectiveProgress progress) =>
        QuestProgressRules.CapCurrent(progress.Current, progress.Required) ?? 0;

    private static double? ObjectivePercent(QuestObjectiveProgress? progress)
    {
        if (progress is null || !progress.ProgressKnown || !progress.Current.HasValue || progress.Required is not > 0)
            return null;
        return QuestProgressRules.CalculatePercent(progress.Current, progress.Required);
    }

    private float BackgroundOpacity => Mathf.Clamp(_opacity?.Invoke() ?? 1f, 0.2f, 1f);

    private float FadeDuration => Mathf.Clamp(_fadeDuration?.Invoke() ?? 0.3f, 0.05f, 3f);

    private float DisplayDuration => Mathf.Clamp(_displayDuration?.Invoke() ?? 4f, 0.5f, 30f);

    private QuestTaskTextSize TaskTextSize => _taskTextSize?.Invoke() ?? QuestTaskTextSize.Small;

    private void ApplyPresentationMode(bool minimal, QuestTaskTextSize taskTextSize)
    {
        if (_root is null || _questName is null || _task is null) return;
        _appliedMinimal = minimal;
        _appliedTaskTextSize = taskTextSize;
        _root.sizeDelta = minimal ? new Vector2(320, 54) : new Vector2(390, 94);
        ApplyBackgroundOpacity(minimal);
        _shade?.gameObject.SetActive(!minimal);
        _statusRail?.gameObject.SetActive(!minimal);
        _portraitRoot?.gameObject.SetActive(!minimal);
        if (_border is not null) _border.enabled = !minimal;

        var titleRoot = (RectTransform)_questName.transform;
        titleRoot.offsetMin = minimal ? new Vector2(10, -25) : new Vector2(84, -38);
        titleRoot.offsetMax = minimal ? new Vector2(-10, -5) : new Vector2(-12, -8);
        _questName.fontSize = minimal ? 13 : 16;
        _questName.fontStyle = minimal ? FontStyles.Normal : FontStyles.Bold;
        _questName.outlineWidth = minimal ? 0 : 0.14f;

        var taskRoot = (RectTransform)_task.transform;
        taskRoot.offsetMin = minimal ? new Vector2(10, 4) : new Vector2(84, 24);
        taskRoot.offsetMax = minimal ? new Vector2(-10, -26) : new Vector2(-12, -39);
        _task.fontSize = QuestTableLayoutRules.RaidPopupTaskFontSize(taskTextSize);
        _task.enableWordWrapping = false;
        _task.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void ApplyBackgroundOpacity(bool minimal)
    {
        if (_backgroundGroup is not null)
            _backgroundGroup.alpha = BackgroundOpacity;
        if (_background is not null)
        {
            _background.color = minimal
                ? new Color(0.035f, 0.04f, 0.042f, 1f)
                : new Color(0.035f, 0.04f, 0.042f, 0.99f);
        }
        if (_questArt is not null)
        {
            var color = _questArt.color;
            color.a = minimal ? 0f : 1f;
            _questArt.color = color;
        }
    }

    private static QuestMapDisplayStateKind DisplayState(string exactStatus) => exactStatus switch
    {
        "AvailableForFinish" => QuestMapDisplayStateKind.ReadyToFinish,
        "Success" => QuestMapDisplayStateKind.Completed,
        "MarkedAsFailed" or "Fail" => QuestMapDisplayStateKind.Failed,
        "FailRestartable" => QuestMapDisplayStateKind.RestartableFailure,
        _ => QuestMapDisplayStateKind.InProgress,
    };

}
