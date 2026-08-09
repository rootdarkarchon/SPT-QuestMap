using BepInEx.Configuration;
using UnityEngine;

namespace SPTQuestMap.Client.Configuration;

internal sealed class QuestMapClientConfiguration
{
    private QuestMapClientConfiguration(
        string configFilePath,
        ConfigEntry<bool> enableTraderQuestGraph,
        ConfigEntry<bool> enableGlobalTasksGraph,
        ConfigEntry<bool> showHiddenQuestRewards,
        ConfigEntry<bool> defaultQuestDetailsToSummary,
        ConfigEntry<bool> autoTrackNewQuests,
        ConfigEntry<bool> trackFavoriteQuests,
        ConfigEntry<bool> autoTrackMapRelatedQuests,
        ConfigEntry<float> raidNotificationOpacity,
        ConfigEntry<bool> raidNotificationMinimal,
        ConfigEntry<float> raidOverlayFadeDurationSeconds,
        ConfigEntry<float> raidNotificationDisplayDurationSeconds,
        ConfigEntry<float> trackedQuestListDisplayDurationSeconds,
        ConfigEntry<KeyboardShortcut> trackedQuestListHotkey,
        ConfigEntry<Color> selectedHighlightColor,
        ConfigEntry<Color> prerequisiteHighlightColor,
        ConfigEntry<Color> successorHighlightColor,
        ConfigEntry<Color> availableColor,
        ConfigEntry<Color> inProgressColor,
        ConfigEntry<Color> readyToTurnInColor,
        ConfigEntry<Color> completedColor,
        ConfigEntry<Color> failedColor,
        ConfigEntry<Color> levelGateColor,
        ConfigEntry<Color> traderGateColor,
        ConfigEntry<Color> lockedColor,
        ConfigEntry<Color> collectorColor,
        ConfigEntry<Color> lightkeeperColor,
        ConfigEntry<Color> endOfLineColor,
        ConfigEntry<bool> enableDebugLogging)
    {
        ConfigFilePath = configFilePath;
        EnableTraderQuestGraph = enableTraderQuestGraph;
        EnableGlobalTasksGraph = enableGlobalTasksGraph;
        ShowHiddenQuestRewards = showHiddenQuestRewards;
        DefaultQuestDetailsToSummary = defaultQuestDetailsToSummary;
        AutoTrackNewQuests = autoTrackNewQuests;
        TrackFavoriteQuests = trackFavoriteQuests;
        AutoTrackMapRelatedQuests = autoTrackMapRelatedQuests;
        RaidNotificationOpacity = raidNotificationOpacity;
        RaidNotificationMinimal = raidNotificationMinimal;
        RaidOverlayFadeDurationSeconds = raidOverlayFadeDurationSeconds;
        RaidNotificationDisplayDurationSeconds = raidNotificationDisplayDurationSeconds;
        TrackedQuestListDisplayDurationSeconds = trackedQuestListDisplayDurationSeconds;
        TrackedQuestListHotkey = trackedQuestListHotkey;
        SelectedHighlightColor = selectedHighlightColor;
        PrerequisiteHighlightColor = prerequisiteHighlightColor;
        SuccessorHighlightColor = successorHighlightColor;
        AvailableColor = availableColor;
        InProgressColor = inProgressColor;
        ReadyToTurnInColor = readyToTurnInColor;
        CompletedColor = completedColor;
        FailedColor = failedColor;
        LevelGateColor = levelGateColor;
        TraderGateColor = traderGateColor;
        LockedColor = lockedColor;
        CollectorColor = collectorColor;
        LightkeeperColor = lightkeeperColor;
        EndOfLineColor = endOfLineColor;
        EnableDebugLogging = enableDebugLogging;
    }

    public ConfigEntry<bool> EnableTraderQuestGraph { get; }

    public string ConfigFilePath { get; }

    public ConfigEntry<bool> EnableGlobalTasksGraph { get; }

    public ConfigEntry<bool> ShowHiddenQuestRewards { get; }

    public ConfigEntry<bool> DefaultQuestDetailsToSummary { get; }

    public ConfigEntry<bool> AutoTrackNewQuests { get; }

    public ConfigEntry<bool> TrackFavoriteQuests { get; }

    public ConfigEntry<bool> AutoTrackMapRelatedQuests { get; }

    public ConfigEntry<float> RaidNotificationOpacity { get; }

    public ConfigEntry<bool> RaidNotificationMinimal { get; }

    public ConfigEntry<float> RaidOverlayFadeDurationSeconds { get; }

    public ConfigEntry<float> RaidNotificationDisplayDurationSeconds { get; }

    public ConfigEntry<float> TrackedQuestListDisplayDurationSeconds { get; }

    public ConfigEntry<KeyboardShortcut> TrackedQuestListHotkey { get; }

    public ConfigEntry<Color> SelectedHighlightColor { get; }

    public ConfigEntry<Color> PrerequisiteHighlightColor { get; }

    public ConfigEntry<Color> SuccessorHighlightColor { get; }

    public ConfigEntry<Color> AvailableColor { get; }

    public ConfigEntry<Color> InProgressColor { get; }

    public ConfigEntry<Color> ReadyToTurnInColor { get; }

    public ConfigEntry<Color> CompletedColor { get; }

    public ConfigEntry<Color> FailedColor { get; }

    public ConfigEntry<Color> LevelGateColor { get; }

    public ConfigEntry<Color> TraderGateColor { get; }

    public ConfigEntry<Color> LockedColor { get; }

    public ConfigEntry<Color> CollectorColor { get; }

    public ConfigEntry<Color> LightkeeperColor { get; }

    public ConfigEntry<Color> EndOfLineColor { get; }

    public ConfigEntry<bool> EnableDebugLogging { get; }

    public static QuestMapClientConfiguration Bind(ConfigFile config)
    {
        return new QuestMapClientConfiguration(
            config.ConfigFilePath,
            config.Bind(
                "Features",
                "Replace trader task screens",
                false,
                "Replace each trader's vanilla quest list with the pooled native QuestMap graph."),
            config.Bind(
                "Features",
                "Replace global Tasks screen",
                false,
                "Replace the global Tasks quest views with Tasks and full QuestMap views while retaining native Notes and Quest Items."),
            config.Bind(
                "Quest details",
                "Show hidden quest rewards",
                true,
                "Show rewards marked hidden by the quest template in QuestMap's custom detail pane."),
            config.Bind(
                "Quest details",
                "Prefer quest summary",
                false,
                "Open the Summary tab by default when the selected quest has a QuestMap summary."),
            config.Bind(
                "Quest tracking",
                "Track newly accepted quests",
                true,
                "Automatically add newly accepted quests to QuestMap's manual tracking list."),
            config.Bind(
                "Quest tracking",
                "Track favorite quests",
                true,
                "Implicitly track quests pinned with Tarkov's native favorite control."),
            config.Bind(
                "Quest tracking",
                "Track quests for current map",
                true,
                "Implicitly track active quests assigned to the current raid map. Any and transit quests are not included."),
            config.Bind(
                "Raid overlays",
                "Background opacity",
                0.92f,
                new ConfigDescription(
                    "Background-only opacity for QuestMap's in-raid progression overlays: the normal notification's composited artwork/backdrop and the translucent black minimal-notification/tracked-list panels. Text, icons, rails, and progress bars remain fully opaque outside the fade animation.",
                    new AcceptableValueRange<float>(0.2f, 1f))),
            config.Bind(
                "Raid overlays",
                "Use minimal progress notifications",
                false,
                "Use a compact text-only in-raid quest progress notification without artwork, status rail, or progress bar."),
            config.Bind(
                "Raid overlays",
                "Fade duration (seconds)",
                0.3f,
                new ConfigDescription(
                    "Fade-in and fade-out duration used by in-raid quest progress notifications and the tracked quest list.",
                    new AcceptableValueRange<float>(0.05f, 3f))),
            config.Bind(
                "Raid overlays",
                "Progress notification duration (seconds)",
                4f,
                new ConfigDescription(
                    "Time an in-raid quest progress notification remains fully visible between its fade animations.",
                    new AcceptableValueRange<float>(0.5f, 30f))),
            config.Bind(
                "Raid overlays",
                "Tracked quest list duration (seconds)",
                8f,
                new ConfigDescription(
                    "Time the tracked quest list remains fully visible after its hotkey is pressed before fading out automatically.",
                    new AcceptableValueRange<float>(1f, 60f))),
            config.Bind(
                "Quest tracking",
                "Tracked quest list hotkey",
                new KeyboardShortcut(KeyCode.L),
                "Show or hide the compact in-raid list of tracked quests applicable to the current map, Any, or transit."),
            BindColor(config, "Highlight colors", "Selected quest", 0xEFD470,
                "Selected cards, selected filter outlines, pinned quest emphasis, and active selection accents."),
            BindColor(config, "Highlight colors", "Prerequisite quest", 0x7AB5D9,
                "Quest cards highlighted as prerequisites of the current selection."),
            BindColor(config, "Highlight colors", "Successor quest", 0xD6A35C,
                "Quest cards highlighted as successors of the current selection."),
            BindColor(config, "Quest state colors", "Available", 0x4D9E72,
                "Available quest rails and successful prerequisite edges."),
            BindColor(config, "Quest state colors", "In progress", 0x3F89B8,
                "Active quest rails, progress bars, and started prerequisite edges."),
            BindColor(config, "Quest state colors", "Ready to turn in", 0xD4A83F,
                "Ready-to-turn-in quest rails and action-ready accents."),
            BindColor(config, "Quest state colors", "Completed", 0x67A77A,
                "Completed quest rails, objective text, and completion markers."),
            BindColor(config, "Quest state colors", "Failed or excluded", 0xC55C62,
                "Failed, excluded, and expired quest rails and failed prerequisite edges."),
            BindColor(config, "Quest state colors", "Level gated", 0x9D7A45,
                "Quest rails gated by player level."),
            BindColor(config, "Quest state colors", "Trader gated", 0x8B659E,
                "Quest rails gated by trader availability, loyalty, or standing."),
            BindColor(config, "Quest state colors", "Locked or pending", 0x78808B,
                "Locked, prerequisite-gated, pending, and unknown quest rails."),
            BindColor(config, "Quest route colors", "Collector", 0x945BC4,
                "Collector route bars and legend entries."),
            BindColor(config, "Quest route colors", "Lightkeeper", 0x234F7F,
                "Lightkeeper route bars and legend entries."),
            BindColor(config, "Quest route colors", "End of quest line", 0xD1B35F,
                "End-of-line quest markers and legend entries."),
            config.Bind(
                "Diagnostics",
                "Enable debug logging",
                false,
                "Write detailed topology, projection, rendering, lifecycle, performance, and raid-monitor diagnostics to the BepInEx log. Warnings and errors are always logged."));
    }

    private static ConfigEntry<Color> BindColor(
        ConfigFile config,
        string section,
        string name,
        int rgb,
        string description) =>
        config.Bind(section, name, ColorFromRgb(rgb), description);

    private static Color ColorFromRgb(int rgb) => new(
        ((rgb >> 16) & 0xff) / 255f,
        ((rgb >> 8) & 0xff) / 255f,
        (rgb & 0xff) / 255f,
        1f);
}
