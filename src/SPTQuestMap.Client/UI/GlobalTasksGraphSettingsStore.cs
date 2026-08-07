using System;
using System.Globalization;
using System.Text;
using SPTQuestMap.Core.Models;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal static class GlobalTasksGraphSettingsStore
{
    private const string KeyPrefix = "SPTQuestMap.GlobalTasks.v1.";

    public static bool TryLoad(string profileId, out GlobalTasksGraphSettings settings)
    {
        var value = PlayerPrefs.GetString(Key(profileId), string.Empty);
        var parts = value.Split('|');
        if (parts.Length != 9
            || parts[0] is not ("2" or "3")
            || !Enum.TryParse(parts[1], out GlobalQuestGraphMode mode)
            || !bool.TryParse(parts[2], out var showAllFuture)
            || !bool.TryParse(parts[3], out var hideFinished)
            || !bool.TryParse(parts[4], out var levelEligibleOnly)
            || !Enum.TryParse(parts[5], out QuestRouteFilter routeFilter))
        {
            settings = default;
            return false;
        }

        try
        {
            settings = new GlobalTasksGraphSettings(
                parts[0] == "2" ? GlobalQuestGraphMode.InProgress : mode,
                showAllFuture,
                hideFinished,
                levelEligibleOnly,
                Decode(parts[6]),
                Decode(parts[7]) ?? string.Empty,
                Decode(parts[8]),
                routeFilter);
            return true;
        }
        catch (FormatException)
        {
            settings = default;
            return false;
        }
    }

    public static void Save(string profileId, GlobalTasksGraphSettings settings)
    {
        var value = string.Join("|",
            "3",
            settings.Mode.ToString(),
            settings.ShowAllFuture.ToString(CultureInfo.InvariantCulture),
            settings.HideFinished.ToString(CultureInfo.InvariantCulture),
            settings.LevelEligibleOnly.ToString(CultureInfo.InvariantCulture),
            settings.RouteFilter.ToString(),
            Encode(settings.TraderId),
            Encode(settings.Search),
            Encode(settings.FocusQuestId));
        PlayerPrefs.SetString(Key(profileId), value);
    }

    private static string Key(string profileId) => KeyPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(profileId));

    private static string Encode(string? value) => string.IsNullOrEmpty(value)
        ? string.Empty
        : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string? Decode(string value) => string.IsNullOrEmpty(value)
        ? null
        : Encoding.UTF8.GetString(Convert.FromBase64String(value));
}

internal readonly struct GlobalTasksGraphSettings
{
    public GlobalTasksGraphSettings(
        GlobalQuestGraphMode mode,
        bool showAllFuture,
        bool hideFinished,
        bool levelEligibleOnly,
        string? traderId,
        string search,
        string? focusQuestId,
        QuestRouteFilter routeFilter)
    {
        Mode = mode;
        ShowAllFuture = showAllFuture;
        HideFinished = hideFinished;
        LevelEligibleOnly = levelEligibleOnly;
        TraderId = traderId;
        Search = search;
        FocusQuestId = focusQuestId;
        RouteFilter = routeFilter;
    }

    public GlobalQuestGraphMode Mode { get; }
    public bool ShowAllFuture { get; }
    public bool HideFinished { get; }
    public bool LevelEligibleOnly { get; }
    public string? TraderId { get; }
    public string Search { get; }
    public string? FocusQuestId { get; }
    public QuestRouteFilter RouteFilter { get; }
}
