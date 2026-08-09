using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        if (parts.Length is not (9 or 10 or 11 or 12 or 13)
            || parts[0] is not ("2" or "3" or "4" or "5" or "6" or "7")
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
                routeFilter,
                parts.Length >= 10 ? Decode(parts[9])?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [] : [],
                parts[0] is "4" or "5" or "6" or "7",
                parts.Length >= 11 ? DecodeSortCriteria(parts[10]) : [],
                parts.Length >= 12 && bool.TryParse(parts[11], out var hideCompletedTasks) && hideCompletedTasks,
                parts.Length == 13 && bool.TryParse(parts[12], out var includeAvailableRepeatables) && includeAvailableRepeatables);
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
            "7",
            settings.Mode.ToString(),
            settings.ShowAllFuture.ToString(CultureInfo.InvariantCulture),
            settings.HideFinished.ToString(CultureInfo.InvariantCulture),
            settings.LevelEligibleOnly.ToString(CultureInfo.InvariantCulture),
            settings.RouteFilter.ToString(),
            Encode(settings.TraderId),
            Encode(settings.Search),
            Encode(settings.FocusQuestId),
            Encode(string.Join(",", settings.LocationIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))),
            Encode(string.Join(",", settings.InProgressSortCriteria.Select(EncodeSortCriterion))),
            settings.HideCompletedInProgressTasks.ToString(CultureInfo.InvariantCulture),
            settings.IncludeAvailableRepeatables.ToString(CultureInfo.InvariantCulture));
        PlayerPrefs.SetString(Key(profileId), value);
    }

    private static string Key(string profileId) => KeyPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(profileId));

    private static string Encode(string? value) => string.IsNullOrEmpty(value)
        ? string.Empty
        : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string? Decode(string value) => string.IsNullOrEmpty(value)
        ? null
        : Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static string EncodeSortCriterion(QuestTableSortCriterion criterion) =>
        $"{criterion.Column}:{criterion.Direction}";

    private static IReadOnlyList<QuestTableSortCriterion> DecodeSortCriteria(string value)
    {
        var decoded = Decode(value);
        if (string.IsNullOrWhiteSpace(decoded)) return [];
        var criteria = new List<QuestTableSortCriterion>();
        foreach (var token in decoded.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = token.Split(':');
            if (parts.Length != 2
                || !Enum.TryParse(parts[0], out QuestTableSortColumn column)
                || !Enum.TryParse(parts[1], out QuestTableSortDirection direction)
                || criteria.Any(criterion => criterion.Column == column)) continue;
            criteria.Add(new QuestTableSortCriterion(column, direction));
        }
        return criteria;
    }
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
        QuestRouteFilter routeFilter,
        IReadOnlyCollection<string> locationIds,
        bool hasLocationFilter = true,
        IReadOnlyList<QuestTableSortCriterion>? inProgressSortCriteria = null,
        bool hideCompletedInProgressTasks = false,
        bool includeAvailableRepeatables = false)
    {
        Mode = mode;
        ShowAllFuture = showAllFuture;
        HideFinished = hideFinished;
        LevelEligibleOnly = levelEligibleOnly;
        TraderId = traderId;
        Search = search;
        FocusQuestId = focusQuestId;
        RouteFilter = routeFilter;
        LocationIds = locationIds;
        HasLocationFilter = hasLocationFilter;
        InProgressSortCriteria = inProgressSortCriteria ?? [];
        HideCompletedInProgressTasks = hideCompletedInProgressTasks;
        IncludeAvailableRepeatables = includeAvailableRepeatables;
    }

    public GlobalQuestGraphMode Mode { get; }
    public bool ShowAllFuture { get; }
    public bool HideFinished { get; }
    public bool LevelEligibleOnly { get; }
    public string? TraderId { get; }
    public string Search { get; }
    public string? FocusQuestId { get; }
    public QuestRouteFilter RouteFilter { get; }
    public IReadOnlyCollection<string> LocationIds { get; }
    public bool HasLocationFilter { get; }
    public IReadOnlyList<QuestTableSortCriterion> InProgressSortCriteria { get; }
    public bool HideCompletedInProgressTasks { get; }
    public bool IncludeAvailableRepeatables { get; }
}
