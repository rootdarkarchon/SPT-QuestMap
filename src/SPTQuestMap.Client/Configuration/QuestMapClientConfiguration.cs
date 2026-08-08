using BepInEx.Configuration;

namespace SPTQuestMap.Client.Configuration;

internal sealed class QuestMapClientConfiguration
{
    private QuestMapClientConfiguration(
        string configFilePath,
        ConfigEntry<bool> enableTraderQuestGraph,
        ConfigEntry<bool> enableGlobalTasksGraph,
        ConfigEntry<bool> enableCustomQuestDetails,
        ConfigEntry<bool> autoTrackNewQuests,
        ConfigEntry<bool> trackFavoriteQuests,
        ConfigEntry<bool> autoTrackMapRelatedQuests,
        ConfigEntry<float> raidNotificationOpacity,
        ConfigEntry<bool> enableDebugLogging,
        ConfigEntry<bool> forceCompatibilityFailure,
        ConfigEntry<bool> forceTraderGraphInitializationFailure,
        ConfigEntry<bool> forceGlobalTasksGraphInitializationFailure)
    {
        ConfigFilePath = configFilePath;
        EnableTraderQuestGraph = enableTraderQuestGraph;
        EnableGlobalTasksGraph = enableGlobalTasksGraph;
        EnableCustomQuestDetails = enableCustomQuestDetails;
        AutoTrackNewQuests = autoTrackNewQuests;
        TrackFavoriteQuests = trackFavoriteQuests;
        AutoTrackMapRelatedQuests = autoTrackMapRelatedQuests;
        RaidNotificationOpacity = raidNotificationOpacity;
        EnableDebugLogging = enableDebugLogging;
        ForceCompatibilityFailure = forceCompatibilityFailure;
        ForceTraderGraphInitializationFailure = forceTraderGraphInitializationFailure;
        ForceGlobalTasksGraphInitializationFailure = forceGlobalTasksGraphInitializationFailure;
    }

    public ConfigEntry<bool> EnableTraderQuestGraph { get; }

    public string ConfigFilePath { get; }

    public ConfigEntry<bool> EnableGlobalTasksGraph { get; }

    public ConfigEntry<bool> EnableCustomQuestDetails { get; }

    public ConfigEntry<bool> AutoTrackNewQuests { get; }

    public ConfigEntry<bool> TrackFavoriteQuests { get; }

    public ConfigEntry<bool> AutoTrackMapRelatedQuests { get; }

    public ConfigEntry<float> RaidNotificationOpacity { get; }

    public ConfigEntry<bool> EnableDebugLogging { get; }

    public ConfigEntry<bool> ForceCompatibilityFailure { get; }

    public ConfigEntry<bool> ForceTraderGraphInitializationFailure { get; }

    public ConfigEntry<bool> ForceGlobalTasksGraphInitializationFailure { get; }

    public static QuestMapClientConfiguration Bind(ConfigFile config)
    {
        return new QuestMapClientConfiguration(
            config.ConfigFilePath,
            config.Bind(
                "Features",
                "EnableTraderQuestGraph",
                false,
                "Replace each trader's vanilla quest list with the pooled native QuestMap graph."),
            config.Bind(
                "Features",
                "EnableGlobalTasksGraph",
                false,
                "Replace the global Tasks quest views with In Progress and full QuestMap graphs while retaining native Notes and Quest Items."),
            config.Bind(
                "Features",
                "EnableCustomQuestDetails",
                false,
                "Reserved for custom quest details. Milestone 1 remains inert."),
            config.Bind(
                "Tracking",
                "AutoTrackNewQuests",
                true,
                "Automatically add newly accepted quests to QuestMap's manual tracking list."),
            config.Bind(
                "Tracking",
                "TrackFavoriteQuests",
                true,
                "Implicitly track quests pinned with Tarkov's native favorite control."),
            config.Bind(
                "Tracking",
                "AutoTrackMapRelatedQuests",
                true,
                "Implicitly track active quests assigned to the current raid map. Any and transit quests are not included."),
            config.Bind(
                "Notifications",
                "RaidNotificationOpacity",
                0.92f,
                new ConfigDescription(
                    "Overall opacity of QuestMap's in-raid quest progress notification.",
                    new AcceptableValueRange<float>(0.2f, 1f))),
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
                "Force the trader graph mount to fail before the vanilla list is hidden, for fallback testing."),
            config.Bind(
                "Diagnostics",
                "ForceGlobalTasksGraphInitializationFailure",
                false,
                "Force the global Tasks graph mount to fail before native task content is hidden, for fallback testing."));
    }
}
