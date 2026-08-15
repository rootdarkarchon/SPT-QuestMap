using BepInEx.Configuration;
using SPTQuestMap.Core.Rules;
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
        ConfigEntry<QuestTaskTextSize> taskListQuestTextSize,
        ConfigEntry<QuestTaskTextSize> inRaidQuestTaskTextSize,
        ConfigEntry<bool> invertLocationFilterMouseButtons,
        ConfigEntry<bool> enableTaskSkipping,
        ConfigEntry<KeyboardShortcut> taskSkipModifier,
        ConfigEntry<bool> autoTrackNewQuests,
        ConfigEntry<bool> trackFavoriteQuests,
        ConfigEntry<bool> autoTrackMapRelatedQuests,
        ConfigEntry<bool> smartInRaidTracking,
        ConfigEntry<float> raidNotificationOpacity,
        ConfigEntry<bool> raidNotificationMinimal,
        ConfigEntry<float> raidOverlayFadeDurationSeconds,
        ConfigEntry<float> raidNotificationDisplayDurationSeconds,
        ConfigEntry<float> trackedQuestListDisplayDurationSeconds,
        ConfigEntry<KeyboardShortcut> trackedQuestListHotkey,
        ConfigEntry<bool> playHoverSounds,
        ConfigEntry<bool> playClickSounds,
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
        TaskListQuestTextSize = taskListQuestTextSize;
        InRaidQuestTaskTextSize = inRaidQuestTaskTextSize;
        InvertLocationFilterMouseButtons = invertLocationFilterMouseButtons;
        EnableTaskSkipping = enableTaskSkipping;
        TaskSkipModifier = taskSkipModifier;
        AutoTrackNewQuests = autoTrackNewQuests;
        TrackFavoriteQuests = trackFavoriteQuests;
        AutoTrackMapRelatedQuests = autoTrackMapRelatedQuests;
        SmartInRaidTracking = smartInRaidTracking;
        RaidNotificationOpacity = raidNotificationOpacity;
        RaidNotificationMinimal = raidNotificationMinimal;
        RaidOverlayFadeDurationSeconds = raidOverlayFadeDurationSeconds;
        RaidNotificationDisplayDurationSeconds = raidNotificationDisplayDurationSeconds;
        TrackedQuestListDisplayDurationSeconds = trackedQuestListDisplayDurationSeconds;
        TrackedQuestListHotkey = trackedQuestListHotkey;
        PlayHoverSounds = playHoverSounds;
        PlayClickSounds = playClickSounds;
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

    public ConfigEntry<QuestTaskTextSize> TaskListQuestTextSize { get; }

    public ConfigEntry<QuestTaskTextSize> InRaidQuestTaskTextSize { get; }

    public ConfigEntry<bool> InvertLocationFilterMouseButtons { get; }

    public ConfigEntry<bool> EnableTaskSkipping { get; }

    public ConfigEntry<KeyboardShortcut> TaskSkipModifier { get; }

    public ConfigEntry<bool> AutoTrackNewQuests { get; }

    public ConfigEntry<bool> TrackFavoriteQuests { get; }

    public ConfigEntry<bool> AutoTrackMapRelatedQuests { get; }

    public ConfigEntry<bool> SmartInRaidTracking { get; }

    public ConfigEntry<float> RaidNotificationOpacity { get; }

    public ConfigEntry<bool> RaidNotificationMinimal { get; }

    public ConfigEntry<float> RaidOverlayFadeDurationSeconds { get; }

    public ConfigEntry<float> RaidNotificationDisplayDurationSeconds { get; }

    public ConfigEntry<float> TrackedQuestListDisplayDurationSeconds { get; }

    public ConfigEntry<KeyboardShortcut> TrackedQuestListHotkey { get; }

    public ConfigEntry<bool> PlayHoverSounds { get; }

    public ConfigEntry<bool> PlayClickSounds { get; }

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
        BindForceTopologyReloadAction(config);
        return new QuestMapClientConfiguration(
            config.ConfigFilePath,
            config.Bind(
                "Features",
                "Replace trader task screens",
                true,
                "Replace each trader's vanilla quest list with the pooled native QuestMap graph."),
            config.Bind(
                "Features",
                "Replace global Tasks screen",
                true,
                "Replace the global Tasks quest views with Tasks and full QuestMap views while retaining native Notes and Quest Items."),
            config.Bind(
                "Quest details",
                "Show hidden quest rewards",
                true,
                "Show rewards marked hidden by the quest template in QuestMap's custom detail pane."),
            config.Bind(
                "Quest details",
                "Prefer quest summary",
                true,
                "Open the Summary tab by default when the selected quest has a QuestMap summary."),
            config.Bind(
                "Task list",
                "Quest task text size",
                QuestTaskTextSize.Small,
                "Text-size preset for quest objective text in QuestMap's in-menu task lists."),
            config.Bind(
                "Raid overlays",
                "In-raid quest task text size",
                QuestTaskTextSize.Small,
                "Text-size preset for quest objective text in the in-raid tracked quest list and progress popups."),
            config.Bind(
                "Task list",
                "Invert location-filter mouse buttons",
                true,
                "Use left-click to isolate a location and right-click to include or exclude it. Disable to use left-click for include/exclude and right-click for isolation."),
            config.Bind(
                "Quest actions",
                "Enable task skipping",
                false,
                "Allow individual active quest tasks to be marked complete without performing their requirements. Hold the configured modifier to reveal SKIP buttons. Quest turn-in remains separate."),
            config.Bind(
                "Quest actions",
                "Task skip modifier",
                new KeyboardShortcut(KeyCode.LeftControl),
                "Hold this configurable key or shortcut to reveal SKIP buttons while task skipping is enabled."),
            config.Bind(
                "Quest tracking",
                "Track newly accepted quests",
                false,
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
                "Implicitly track active quests assigned to the current raid map, including server-resolved Any and Transition quest tasks. Genuinely Any quests are not included by this policy."),
            config.Bind(
                "Quest tracking",
                "Smart in-raid tracking",
                true,
                "Only show tracked objectives in the in-raid quest list when their objective type is useful during a raid. Trader hand-ins, weapon assembly, loyalty, standing, skill, hideout, and other menu-only objectives are omitted."),
            config.Bind(
                "Raid overlays",
                "Background opacity",
                0.25f,
                new ConfigDescription(
                    "Background-only opacity for QuestMap's in-raid progression overlays: the normal notification's composited artwork/backdrop and the translucent black minimal-notification/tracked-list panels. Text, icons, rails, and progress bars remain fully opaque outside the fade animation.",
                    new AcceptableValueRange<float>(0.1f, 1f))),
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
                new KeyboardShortcut(KeyCode.I),
                "Show or hide the compact in-raid list of tracked quests applicable to the current map, Any, or transit."),
            config.Bind(
                "Interface feedback",
                "Play mouse-over sounds",
                true,
                "Play Tarkov's native interface sound when the pointer enters a custom QuestMap button."),
            config.Bind(
                "Interface feedback",
                "Play click sounds",
                true,
                "Play Tarkov's native interface sound when a custom QuestMap button is clicked."),
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

    private static void BindForceTopologyReloadAction(ConfigFile config) =>
        config.Bind(
            "Diagnostics",
            "Force reload server topology",
            false,
            new ConfigDescription(
                "Force QuestMap to fetch and rebuild the complete server topology outside a raid. Use this to recover stale or missing quest data without restarting EFT.",
                null,
                new ConfigurationManagerAttributes
                {
                    CustomDrawer = QuestMapConfigurationManagerActions.DrawForceTopologyReload,
                    HideDefaultButton = true,
                    IsAdvanced = false,
                    Order = 10,
                }));

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
