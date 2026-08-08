using System;
using BepInEx.Logging;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Core.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class RaidQuestProgressNotificationView : MonoBehaviour, IDisposable
{
    private const float FadeInSeconds = 0.18f;
    private const float VisibleSeconds = 4f;
    private const float FadeOutSeconds = 0.55f;

    private RectTransform? _root;
    private CanvasGroup? _group;
    private Func<float>? _opacity;
    private QuestAssetSpriteCache? _assetCache;
    private Image? _questArt;
    private Image? _portrait;
    private TextMeshProUGUI? _portraitFallback;
    private TextMeshProUGUI? _questName;
    private Image? _statusRail;
    private TextMeshProUGUI? _task;
    private TextMeshProUGUI? _count;
    private RectTransform? _progressRoot;
    private RectTransform? _progressFill;
    private float _shownAt;
    private float _fadeStartAlpha;
    private int _contentVersion;
    private bool _displaying;

    public static RaidQuestProgressNotificationView Create(
        Transform owner,
        ManualLogSource log,
        Func<float> opacity,
        QuestAssetSpriteCache assetCache)
    {
        var host = new GameObject("QuestMapRaidProgressNotifications", typeof(RectTransform));
        host.transform.SetParent(owner, false);
        var view = host.AddComponent<RaidQuestProgressNotificationView>();
        view._opacity = opacity;
        view._assetCache = assetCache;
        view.Build(log);
        return view;
    }

    public bool Displaying => _displaying;

    public void SetStackIndex(int index)
    {
        if (_root is not null) _root.anchoredPosition = new Vector2(-28, -28 - index * 102);
    }

    public void Show(InRaidQuestProgressChange change)
    {
        if (_root is null || _group is null || _assetCache is null) return;
        var version = ++_contentVersion;
        var node = change.Quest;
        var progress = change.Progress;

        _questName!.text = node.Name;
        var statusColor = QuestGraphPalette.Status(DisplayState(change.ExactStatus));
        if (_statusRail is not null) _statusRail.color = statusColor;
        _task!.text = change.Objective?.Text ?? "Quest status changed";
        _task.color = progress?.Complete == true
            ? new Color(0.61f, 0.86f, 0.63f, 1f)
            : Color.white;

        var hasCount = progress?.ProgressKnown == true
            && progress.Current.HasValue
            && progress.Required.HasValue;
        _count!.text = hasCount
            ? $"{CappedCurrent(progress!):0.##} / {progress!.Required!.Value:0.##}"
            : string.Empty;

        var percent = ObjectivePercent(progress);
        _progressRoot!.gameObject.SetActive(percent.HasValue);
        if (percent.HasValue)
        {
            _progressFill!.anchorMax = new Vector2((float)(percent.Value / 100d), 1);
            _progressFill.gameObject.GetComponent<Image>().color = progress?.Complete == true
                ? QuestGraphPalette.Status(QuestMapDisplayStateKind.ReadyToFinish)
                : QuestGraphPalette.Status(QuestMapDisplayStateKind.InProgress);
        }

        _questArt!.sprite = null;
        _questArt.gameObject.SetActive(false);
        _assetCache.Request(node.ImageUrl, sprite =>
        {
            if (version != _contentVersion || _questArt is null || sprite is null) return;
            _questArt.sprite = sprite;
            var fitter = _questArt.GetComponent<AspectRatioFitter>();
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            _questArt.gameObject.SetActive(true);
        });

        _portrait!.sprite = null;
        _portrait.gameObject.SetActive(false);
        _portraitFallback!.text = Initials(node.TraderName);
        _portraitFallback.gameObject.SetActive(true);
        _assetCache.Request(node.TraderImageUrl, sprite =>
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
        _root.gameObject.AddComponent<Image>().color = new Color(0.035f, 0.04f, 0.042f, 0.99f);
        _group = _root.gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        var artRoot = UnityUiFactory.CreateRect("QuestArt", _root);
        UnityUiFactory.Stretch(artRoot);
        _questArt = artRoot.gameObject.AddComponent<Image>();
        _questArt.raycastTarget = false;
        _questArt.preserveAspect = false;
        var artFitter = artRoot.gameObject.AddComponent<AspectRatioFitter>();
        artFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        artRoot.gameObject.SetActive(false);

        var shade = UnityUiFactory.CreateRect("Shade", _root);
        UnityUiFactory.Stretch(shade);
        shade.gameObject.AddComponent<Image>().color = new Color(0.01f, 0.013f, 0.014f, 0.62f);

        var statusRail = UnityUiFactory.CreateRect("StatusRail", _root);
        statusRail.anchorMin = Vector2.zero;
        statusRail.anchorMax = new Vector2(0, 1);
        statusRail.pivot = new Vector2(0, 0.5f);
        statusRail.sizeDelta = new Vector2(6, 0);
        _statusRail = statusRail.gameObject.AddComponent<Image>();
        _statusRail.color = QuestGraphPalette.Status(QuestMapDisplayStateKind.InProgress);

        var portraitRoot = UnityUiFactory.CreateRect("Trader", _root);
        portraitRoot.anchorMin = portraitRoot.anchorMax = new Vector2(0, 0.5f);
        portraitRoot.pivot = new Vector2(0, 0.5f);
        portraitRoot.anchoredPosition = new Vector2(16, 3);
        portraitRoot.sizeDelta = new Vector2(56, 56);
        portraitRoot.gameObject.AddComponent<Image>().color = new Color(0.20f, 0.19f, 0.16f, 0.98f);
        _portraitFallback = UnityUiFactory.AddText(portraitRoot.gameObject, string.Empty, 16,
            TextAlignmentOptions.Center, new Color(0.94f, 0.88f, 0.68f, 1));
        var portraitImageRoot = UnityUiFactory.CreateRect("Image", portraitRoot);
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

        var border = _root.gameObject.AddComponent<Outline>();
        border.effectColor = QuestGraphPalette.Border;
        border.effectDistance = Vector2.one;

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
        var elapsed = Time.unscaledTime - _shownAt;
        if (elapsed < FadeInSeconds)
        {
            _group.alpha = Mathf.Lerp(_fadeStartAlpha, TargetOpacity, elapsed / FadeInSeconds);
            return;
        }
        if (elapsed < FadeInSeconds + VisibleSeconds)
        {
            _group.alpha = TargetOpacity;
            return;
        }
        if (elapsed < FadeInSeconds + VisibleSeconds + FadeOutSeconds)
        {
            var fadeElapsed = elapsed - FadeInSeconds - VisibleSeconds;
            _group.alpha = TargetOpacity * (1f - fadeElapsed / FadeOutSeconds);
            return;
        }

        _group.alpha = 0;
        _displaying = false;
        _root.gameObject.SetActive(false);
    }

    private static double CappedCurrent(QuestObjectiveProgress progress) =>
        progress.Current.HasValue && progress.Required.HasValue
            ? Math.Min(progress.Current.Value, progress.Required.Value)
            : progress.Current ?? 0;

    private static double? ObjectivePercent(QuestObjectiveProgress? progress)
    {
        if (progress is null || !progress.ProgressKnown || !progress.Current.HasValue || progress.Required is not > 0)
            return null;
        return Math.Clamp(progress.Current.Value / progress.Required.Value * 100d, 0d, 100d);
    }

    private float TargetOpacity => Mathf.Clamp(_opacity?.Invoke() ?? 1f, 0.2f, 1f);

    private static QuestMapDisplayStateKind DisplayState(string exactStatus) => exactStatus switch
    {
        "AvailableForFinish" => QuestMapDisplayStateKind.ReadyToFinish,
        "Success" => QuestMapDisplayStateKind.Completed,
        "MarkedAsFailed" or "Fail" => QuestMapDisplayStateKind.Failed,
        "FailRestartable" => QuestMapDisplayStateKind.RestartableFailure,
        _ => QuestMapDisplayStateKind.InProgress,
    };

    private static string Initials(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "?";
        var words = value.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 1
            ? words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant()
            : string.Concat(words[0][0], words[1][0]).ToUpperInvariant();
    }
}
