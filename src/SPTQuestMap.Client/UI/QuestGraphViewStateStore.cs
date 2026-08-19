using System;
using System.Globalization;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal static class QuestGraphViewStateStore
{
    private const string KeyPrefix = "SPTQuestMap.GraphView.v1.";

    public static string Scope(string topologyVersion, string profileId, string viewType) =>
        $"{topologyVersion}|{profileId}|{viewType}";

    public static bool TryLoad(string scope, out PersistedQuestGraphViewState state)
    {
        var value = PlayerPrefs.GetString(Key(scope), string.Empty);
        var parts = value.Split('|');
        if (parts.Length != 4
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)
            || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            state = default;
            return false;
        }

        state = new PersistedQuestGraphViewState(new GraphViewportState(scale, new Vector2(x, y)),
            string.IsNullOrWhiteSpace(parts[3]) ? null : parts[3]);
        return true;
    }

    public static void Save(string scope, GraphViewportState viewport, string? selectedQuestId)
    {
        var value = string.Join("|",
            viewport.Scale.ToString("R", CultureInfo.InvariantCulture),
            viewport.AnchoredPosition.x.ToString("R", CultureInfo.InvariantCulture),
            viewport.AnchoredPosition.y.ToString("R", CultureInfo.InvariantCulture),
            selectedQuestId ?? string.Empty);
        PlayerPrefs.SetString(Key(scope), value);
    }

    public static void Flush() => PlayerPrefs.Save();

    private static string Key(string scope)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var character in scope)
        {
            hash ^= character;
            hash *= prime;
        }
        return KeyPrefix + hash.ToString("X16", CultureInfo.InvariantCulture);
    }
}

internal readonly struct PersistedQuestGraphViewState
{
    public PersistedQuestGraphViewState(GraphViewportState viewport, string? selectedQuestId)
    {
        Viewport = viewport;
        SelectedQuestId = selectedQuestId;
    }

    public GraphViewportState Viewport { get; }
    public string? SelectedQuestId { get; }
}
