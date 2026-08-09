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
        bool enabled = true)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        button.interactable = enabled;
        UnityUiFactory.AddText(rect.gameObject, label, height <= 24 ? 10 : 12, TextAlignmentOptions.Center,
            enabled ? Color.white : new Color(0.48f, 0.50f, 0.50f, 1f));
        button.onClick.AddListener(() => action());
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
        bool enabled = true)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-right, -4);
        rect.sizeDelta = new Vector2(width, 32);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        button.interactable = enabled;
        UnityUiFactory.AddText(rect.gameObject, label, 12, TextAlignmentOptions.Center,
            enabled ? Color.white : new Color(0.48f, 0.50f, 0.50f, 1f));
        button.onClick.AddListener(() => action());
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
