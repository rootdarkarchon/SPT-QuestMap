using BepInEx.Configuration;
using BepInEx.Logging;

namespace SPTQuestMap.Client;

internal static class QuestMapDebugLog
{
    private static ConfigEntry<bool>? _enabled;

    public static bool Enabled => _enabled?.Value == true;

    public static void Configure(ConfigEntry<bool> enabled) => _enabled = enabled;

    public static void Info(ManualLogSource? log, object data)
    {
        if (Enabled) log?.LogInfo(data);
    }
}
