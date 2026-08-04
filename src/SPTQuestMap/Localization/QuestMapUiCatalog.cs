using System.Reflection;
using System.Text.Json;

namespace SPTQuestMap.Services;

internal static class QuestMapUiCatalog
{
    private const string ResourcePrefix = "SPTQuestMap.Localization.Locales.";
    private static readonly string[] SupportedOverrides =
    [
        "ch", "cz", "es", "es-mx", "fr", "ge", "hu", "it", "jp", "kr", "pl", "po", "ro", "ru", "sk", "tu",
    ];

    internal static readonly IReadOnlyDictionary<string, string> English = Load("en");
    private static readonly IReadOnlyDictionary<string, string> SptGlobalKeys = Load("spt-global-keys");
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Overrides = SupportedOverrides
        .ToDictionary(language => language, Load, StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyDictionary<string, string> For(string language, IReadOnlyDictionary<string, string> sptLocale)
    {
        var result = English.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (catalogKey, localeKey) in SptGlobalKeys)
            {
                if (sptLocale.TryGetValue(localeKey, out var localized) && !string.IsNullOrWhiteSpace(localized))
                {
                    result[catalogKey] = localized;
                }
            }
        }

        if (Overrides.TryGetValue(language, out var overrides))
        {
            foreach (var (key, value) in overrides)
            {
                if (English.ContainsKey(key)) result[key] = value;
            }
        }

        // Product names are identifiers, not localizable UI prose.
        result["app.title"] = "SPT QuestMap";
        result["app.heading"] = "QuestMap";
        return result;
    }

    private static IReadOnlyDictionary<string, string> Load(string name)
    {
        var resourceName = $"{ResourcePrefix}{name}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded QuestMap locale resource '{resourceName}'.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"QuestMap locale resource '{resourceName}' is empty.");
    }
}
