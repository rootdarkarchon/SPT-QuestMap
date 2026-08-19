using System;
using BepInEx.Configuration;
using UnityEngine;

namespace SPTQuestMap.Client.Configuration;

// BepInEx.ConfigurationManager discovers this reflection-only metadata shape by
// type and field name. Keeping it local avoids a hard dependency on the optional
// F12 configuration-manager plugin.
internal sealed class ConfigurationManagerAttributes
{
    public Action<ConfigEntryBase>? CustomDrawer;
    public bool? HideDefaultButton;
    public bool? IsAdvanced;
    public int? Order;
}

internal static class QuestMapConfigurationManagerActions
{
    private static Action? _forceTopologyReload;
    private static Func<bool>? _canForceTopologyReload;

    public static void Configure(Action forceTopologyReload, Func<bool> canForceTopologyReload)
    {
        _forceTopologyReload = forceTopologyReload;
        _canForceTopologyReload = canForceTopologyReload;
    }

    public static void Clear()
    {
        _forceTopologyReload = null;
        _canForceTopologyReload = null;
    }

    public static void DrawForceTopologyReload(ConfigEntryBase _)
    {
        var previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && _forceTopologyReload is not null
            && (_canForceTopologyReload?.Invoke() ?? false);
        try
        {
            if (GUILayout.Button("Reload now")) _forceTopologyReload?.Invoke();
        }
        finally
        {
            GUI.enabled = previousEnabled;
        }
    }
}
