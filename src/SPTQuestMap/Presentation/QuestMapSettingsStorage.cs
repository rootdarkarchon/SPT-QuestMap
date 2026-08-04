using System.Text.Json;

namespace SPTQuestMap.Presentation;

public static class QuestMapSettingsStorage
{
    public const string PageStorageKey = "sptQuestMap.ui.v2";
    public const string LegacyStorageKey = "sptQuestMap.ui.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(QuestMapSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    public static QuestMapSettings? Deserialize(string? currentJson, string? legacyJson)
    {
        if (!string.IsNullOrWhiteSpace(currentJson))
        {
            try { return JsonSerializer.Deserialize<QuestMapSettings>(currentJson, JsonOptions); }
            catch (JsonException) { }
        }

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
