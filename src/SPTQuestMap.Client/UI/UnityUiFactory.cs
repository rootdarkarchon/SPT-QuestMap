using System;
using EFT.UI;
using SPTQuestMap.Client.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal static class UnityUiFactory
{
    public static RectTransform CreateRect(string name, Transform parent)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return (RectTransform)gameObject.transform;
    }

    public static TextMeshProUGUI AddText(
        GameObject gameObject,
        string text,
        float size,
        TextAlignmentOptions alignment,
        Color color)
    {
        var textHost = gameObject;
        if (gameObject.GetComponent<Graphic>() is not null)
        {
            var textRect = CreateRect("Label", gameObject.transform);
            Stretch(textRect);
            textHost = textRect.gameObject;
        }

        var label = textHost.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = color;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        return label;
    }

    public static void Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    public static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localScale = source.localScale;
        target.localRotation = source.localRotation;
        target.SetSiblingIndex(source.GetSiblingIndex());
    }

    public static Button AddButton(GameObject gameObject, Color normalColor)
    {
        var image = gameObject.AddComponent<Image>();
        image.color = normalColor;
        var button = gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = QuestGraphPalette.ButtonHoverTint;
        colors.pressedColor = QuestGraphPalette.ButtonPressedTint;
        button.colors = colors;
        gameObject.AddComponent<QuestMapButtonFeedback>();
        return button;
    }

    public static string Initials(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "?";
        var words = value.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        return words.Length == 1
            ? words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant()
            : string.Concat(words[0][0], words[1][0]).ToUpperInvariant();
    }

    public static void AddPortrait(
        RectTransform parent,
        string? url,
        TMP_Text fallback,
        QuestAssetSpriteCache assets,
        float inset = 2f)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var imageRect = CreateRect("Image", parent);
        Stretch(imageRect, inset, inset, inset, inset);
        var image = imageRect.gameObject.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        imageRect.gameObject.SetActive(false);
        assets.Request(url, sprite =>
        {
            if (image == null || sprite is null) return;
            image.sprite = sprite;
            imageRect.gameObject.SetActive(true);
            if (fallback != null) fallback.gameObject.SetActive(false);
        });
    }
}

internal sealed class QuestMapButtonFeedback : ButtonFeedback
{
    private static QuestMapClientConfiguration? _configuration;
    private Button? _button;

    public static void Configure(QuestMapClientConfiguration configuration) => _configuration = configuration;

    public override bool Interactable
    {
        get
        {
            _button ??= GetComponent<Button>();
            return (_button?.interactable ?? true) && base.Interactable;
        }
        set => base.Interactable = value;
    }

    public override void PointerEnterHandler()
    {
        if (_configuration?.PlayHoverSounds.Value != false) base.PointerEnterHandler();
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        if (_configuration?.PlayClickSounds.Value != false) base.OnPointerClick(eventData);
    }
}
