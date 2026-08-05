using TMPro;
using UnityEngine;
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
        colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        button.colors = colors;
        return button;
    }
}
