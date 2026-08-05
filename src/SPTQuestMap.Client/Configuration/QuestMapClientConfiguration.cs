using BepInEx.Configuration;

namespace SPTQuestMap.Client.Configuration;

internal sealed class QuestMapClientConfiguration
{
    private QuestMapClientConfiguration(
        ConfigEntry<bool> enableTraderQuestGraph,
        ConfigEntry<bool> enableGlobalTasksGraph,
        ConfigEntry<bool> enableCustomQuestDetails,
        ConfigEntry<bool> enableDebugLogging,
        ConfigEntry<bool> forceCompatibilityFailure,
        ConfigEntry<bool> forceTraderGraphInitializationFailure)
    {
        EnableTraderQuestGraph = enableTraderQuestGraph;
        EnableGlobalTasksGraph = enableGlobalTasksGraph;
        EnableCustomQuestDetails = enableCustomQuestDetails;
        EnableDebugLogging = enableDebugLogging;
        ForceCompatibilityFailure = forceCompatibilityFailure;
        ForceTraderGraphInitializationFailure = forceTraderGraphInitializationFailure;
    }

    public ConfigEntry<bool> EnableTraderQuestGraph { get; }

    public ConfigEntry<bool> EnableGlobalTasksGraph { get; }

    public ConfigEntry<bool> EnableCustomQuestDetails { get; }

    public ConfigEntry<bool> EnableDebugLogging { get; }

    public ConfigEntry<bool> ForceCompatibilityFailure { get; }

    public ConfigEntry<bool> ForceTraderGraphInitializationFailure { get; }

    public static QuestMapClientConfiguration Bind(ConfigFile config)
    {
        return new QuestMapClientConfiguration(
            config.Bind(
                "Features",
                "EnableTraderQuestGraph",
                false,
                "Replace each trader's vanilla quest list with the Milestone 3 QuestMap graph."),
            config.Bind(
                "Features",
                "EnableGlobalTasksGraph",
                false,
                "Reserved for the global Tasks graph. Milestone 1 remains inert."),
            config.Bind(
                "Features",
                "EnableCustomQuestDetails",
                false,
                "Reserved for custom quest details. Milestone 1 remains inert."),
            config.Bind(
                "Diagnostics",
                "EnableDebugLogging",
                false,
                "Enable additional startup diagnostics. No per-frame logging is performed."),
            config.Bind(
                "Diagnostics",
                "ForceCompatibilityFailure",
                false,
                "Force the startup guard to fail so safe-disable behavior can be tested without modifying game files."),
            config.Bind(
                "Diagnostics",
                "ForceTraderGraphInitializationFailure",
                false,
                "Force the trader graph mount to fail before the vanilla list is hidden, for fallback testing."));
    }
}
