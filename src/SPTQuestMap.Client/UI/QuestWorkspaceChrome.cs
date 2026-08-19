using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal static class QuestWorkspaceChrome
{
    public static Image AddButton(
        RectTransform parent,
        string name,
        string label,
        float x,
        float y,
        float width,
        bool active,
        Action action,
        float height = 32,
        bool enabled = true,
        Func<string>? tooltip = null)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        button.interactable = enabled;
        var text = UnityUiFactory.AddText(rect.gameObject, label, height <= 24 ? 10 : 12, TextAlignmentOptions.Center,
            enabled ? Color.white : new Color(0.48f, 0.50f, 0.50f, 1f));
        UnityUiFactory.FitSingleLine(text, height <= 24 ? 7f : 8f, 5f);
        button.onClick.AddListener(() => action());
        if (tooltip is not null) QuestMapNativeTooltips.Bind(rect.gameObject, tooltip);
        return rect.GetComponent<Image>();
    }

    public static Image AddRightButton(
        RectTransform parent,
        string name,
        string label,
        float right,
        float width,
        bool active,
        Action action,
        bool enabled = true,
        Func<string>? tooltip = null,
        bool allowTwoLines = false)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-right, -4);
        rect.sizeDelta = new Vector2(width, 32);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        button.interactable = enabled;
        var text = UnityUiFactory.AddText(rect.gameObject, label, 12, TextAlignmentOptions.Center,
            enabled ? Color.white : new Color(0.48f, 0.50f, 0.50f, 1f));
        if (allowTwoLines) UnityUiFactory.FitTwoLines(text, 9f, 5f, 2f);
        else UnityUiFactory.FitSingleLine(text, 8f, 5f);
        button.onClick.AddListener(() => action());
        if (tooltip is not null) QuestMapNativeTooltips.Bind(rect.gameObject, tooltip);
        return rect.GetComponent<Image>();
    }

    public static void AddSeparator(RectTransform parent, string name, float x, float y = -4, float height = 32)
    {
        var separator = UnityUiFactory.CreateRect(name, parent);
        separator.anchorMin = separator.anchorMax = separator.pivot = new Vector2(0, 1);
        separator.anchoredPosition = new Vector2(x, y);
        separator.sizeDelta = new Vector2(1, height);
        separator.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
    }
}
