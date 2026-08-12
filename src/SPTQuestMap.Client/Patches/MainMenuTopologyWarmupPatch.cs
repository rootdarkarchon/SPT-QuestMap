using System;

namespace SPTQuestMap.Client.Patches;

internal static class MainMenuTopologyWarmupPatch
{
    private static Action? _warmTopology;

    public static void Configure(Action? warmTopology)
    {
        _warmTopology = warmTopology;
    }

    public static void ShowScreenPostfix()
    {
        _warmTopology?.Invoke();
    }
}
