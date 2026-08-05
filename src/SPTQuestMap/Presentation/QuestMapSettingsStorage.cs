using System.Text.Json;

namespace SPTQuestMap.Presentation;

public static class QuestMapSettingsStorage
{
    public const string PageStorageKey = "sptQuestMap.ui.v4";
    public const string PreviousStorageKey = "sptQuestMap.ui.v3";
    public const string VersionTwoStorageKey = "sptQuestMap.ui.v2";
    public const string LegacyStorageKey = "sptQuestMap.ui.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(QuestMapSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    public static QuestMapSettings? Deserialize(string? currentJson, string? legacyJson)
        => Deserialize(currentJson, null, null, legacyJson);

    public static QuestMapSettings? Deserialize(string? currentJson, string? previousJson, string? legacyJson)
        => Deserialize(currentJson, previousJson, null, legacyJson);

    public static QuestMapSettings? Deserialize(string? currentJson, string? versionThreeJson, string? versionTwoJson, string? legacyJson)
    {
        var current = DeserializeCurrent(currentJson);
        if (current is not null) return current;
        var versionThree = DeserializeVersionThree(versionThreeJson);
        if (versionThree is not null) return versionThree;
        var versionTwo = DeserializeCurrent(versionTwoJson);
        if (versionTwo is not null) return versionTwo;

        if (string.IsNullOrWhiteSpace(legacyJson)) return null;
        try
        {
            var legacy = JsonSerializer.Deserialize<LegacyQuestMapSettings>(legacyJson, JsonOptions);
            if (legacy is null) return null;
            return new QuestMapSettings(
                legacy.SelectedProfileId,
                legacy.Language,
                legacy.ShowAllFuture ?? false,
                legacy.ShowFinished ?? !(legacy.HideFinished ?? false),
                legacy.LevelEligibleOnly ?? true,
                legacy.InProgressExpanded ?? false,
                legacy.Search ?? string.Empty,
                legacy.TraderFilter ?? string.Empty,
                legacy.Profiles ?? new Dictionary<string, QuestProfileUiSettings>()
            );
        }
        catch (JsonException) { return null; }
    }

    private static QuestMapSettings? DeserializeCurrent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<QuestMapSettings>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static QuestMapSettings? DeserializeVersionThree(string? json)
    {
        var settings = DeserializeCurrent(json);
        if (settings is null || string.IsNullOrWhiteSpace(json)) return settings;
        try
        {
            using var document = JsonDocument.Parse(json);
            var differencesOnly = document.RootElement.TryGetProperty("differencesOnly", out var value) && value.ValueKind == JsonValueKind.True;
            return settings with { ComparisonFilter = differencesOnly ? QuestComparisonFilter.AllChanges : QuestComparisonFilter.AllQuests };
        }
        catch (JsonException) { return settings; }
    }

    private sealed record LegacyQuestMapSettings(
        string? SelectedProfileId,
        string? Language,
        bool? ShowAllFuture,
        bool? ShowFinished,
        bool? HideFinished,
        bool? LevelEligibleOnly,
        bool? InProgressExpanded,
        string? Search,
        string? TraderFilter,
        IReadOnlyDictionary<string, QuestProfileUiSettings>? Profiles
    );
}
