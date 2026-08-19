using System.Reflection;
using System.Text.Json;

namespace SPTQuestMap.Services;

internal sealed class QuestSummaryCatalog
{
    internal const string ExternalRelativePath = "SPT/user/mods/SPT-QuestMap/summaries";
    private const string ResourcePrefix = "SPTQuestMap.Localization.Summaries.";
    private readonly string _externalDirectory;
    private readonly Action<string>? _warning;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _embedded;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _embeddedLanguageCache = new(StringComparer.OrdinalIgnoreCase);
    private ExternalCatalog? _external;

    internal QuestSummaryCatalog(
        string? externalDirectory = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? embedded = null,
        Action<string>? warning = null)
    {
        var modDirectory = Path.GetDirectoryName(typeof(QuestSummaryCatalog).Assembly.Location) ?? AppContext.BaseDirectory;
        _externalDirectory = externalDirectory ?? Path.Combine(modDirectory, "summaries");
        _warning = warning;
        _embedded = embedded ?? LoadEmbedded();
    }

    internal string? Get(string questId, string language)
    {
        var custom = GetExternal();
        if (TryGet(custom.ByLanguage.GetValueOrDefault(language), questId, out var localized)) return localized;
        if (TryGet(custom.ByLanguage.GetValueOrDefault("en"), questId, out var english)) return english;
        if (TryGet(custom.AnyLanguage, questId, out var available)) return available;
        return TryGet(GetEmbeddedSummaries(language), questId, out var embeddedSummary) ? embeddedSummary : null;
    }

    private IReadOnlyDictionary<string, string> GetEmbeddedSummaries(string language)
    {
        if (_embeddedLanguageCache.TryGetValue(language, out var cached)) return cached;
        var summaries = new Dictionary<string, string>(StringComparer.Ordinal);
        Overlay(summaries, _embedded.GetValueOrDefault("en"));
        if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)) Overlay(summaries, _embedded.GetValueOrDefault(language));
        return _embeddedLanguageCache[language] = summaries;
    }

    private ExternalCatalog GetExternal()
    {
        if (_external is not null) return _external;

        var byLanguage = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var anyLanguage = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(_externalDirectory))
        {
            return _external = new ExternalCatalog(new Dictionary<string, IReadOnlyDictionary<string, string>>(), anyLanguage);
        }

        try
        {
            var files = Directory
                .EnumerateFiles(_externalDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(Path.GetFileName, StringComparer.Ordinal);
            foreach (var path in files)
            {
                var summaries = LoadExternal(path);
                var language = GetFileLanguage(path);
                if (!byLanguage.TryGetValue(language, out var localized))
                {
                    localized = new Dictionary<string, string>(StringComparer.Ordinal);
                    byLanguage[language] = localized;
                }

                Overlay(localized, summaries);
                Overlay(anyLanguage, summaries);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _warning?.Invoke($"SPT-QuestMap: could not enumerate optional quest summaries in '{_externalDirectory}': {exception.Message}");
        }

        return _external = new ExternalCatalog(
            byLanguage.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, string>)pair.Value, StringComparer.OrdinalIgnoreCase),
            anyLanguage);
    }

    private IReadOnlyDictionary<string, string> LoadExternal(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Deserialize(stream, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _warning?.Invoke($"SPT-QuestMap: could not load optional quest summaries '{path}': {exception.Message}");
            return new Dictionary<string, string>();
        }
    }

    private static string GetFileLanguage(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var separator = stem.LastIndexOf('.');
        return separator >= 0 && separator < stem.Length - 1 ? stem[(separator + 1)..] : "en";
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadEmbedded()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                name => name[ResourcePrefix.Length..^5],
                name =>
                {
                    using var stream = assembly.GetManifestResourceStream(name)
                        ?? throw new InvalidOperationException($"Missing embedded quest-summary resource '{name}'.");
                    return Deserialize(stream, name);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> Deserialize(Stream stream, string source)
    {
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new JsonException($"Quest-summary catalog '{source}' is empty.");
        return values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static void Overlay(Dictionary<string, string> target, IReadOnlyDictionary<string, string>? source)
    {
        if (source is null) return;
        foreach (var (questId, summary) in source) target[questId] = summary;
    }

    private static bool TryGet(IReadOnlyDictionary<string, string>? values, string questId, out string summary)
    {
        if (values is not null && values.TryGetValue(questId, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            summary = value.Trim();
            return true;
        }

        summary = string.Empty;
        return false;
    }

    private sealed record ExternalCatalog(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ByLanguage,
        IReadOnlyDictionary<string, string> AnyLanguage);
}
