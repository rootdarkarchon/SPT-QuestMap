using BepInEx.Configuration;
using UnityEngine;

namespace SPTQuestMap.Client.Input;

/// <summary>
/// Matches the configured main key and required modifiers without rejecting
/// unrelated gameplay keys that happen to be held at the same time.
/// </summary>
internal static class QuestMapKeyboardShortcut
{
    public static bool IsDown(KeyboardShortcut shortcut)
    {
        if (shortcut.MainKey == KeyCode.None || !UnityEngine.Input.GetKeyDown(shortcut.MainKey)) return false;
        foreach (var modifier in shortcut.Modifiers)
        {
            if (!UnityEngine.Input.GetKey(modifier)) return false;
        }
        return true;
    }

    public static bool IsPressed(KeyboardShortcut shortcut)
    {
        if (shortcut.MainKey == KeyCode.None || !UnityEngine.Input.GetKey(shortcut.MainKey)) return false;
        foreach (var modifier in shortcut.Modifiers)
        {
            if (!UnityEngine.Input.GetKey(modifier)) return false;
        }
        return true;
    }
}
