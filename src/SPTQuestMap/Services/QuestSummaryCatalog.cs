using System.Reflection;
using System.Text.Json;

namespace SPTQuestMap.Services;

internal sealed class QuestSummaryCatalog
{
    internal const string ExternalRelativePath = "SPT/user/mods/SPT-QuestMap/Summaries";
    private const string ResourcePrefix = "SPTQuestMap.Localization.Summaries.";
    private readonly string _externalDirectory;
    private readonly Action<string>? _warning;
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _embedded;
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _externalCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _embeddedLanguageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _traderLanguageCache = new(StringComparer.OrdinalIgnoreCase);

    internal QuestSummaryCatalog(
        string? externalDirectory = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? embedded = null,
        Action<string>? warning = null)
    {
        _externalDirectory = externalDirectory ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "user", "mods", "SPT-QuestMap", "Summaries"));
        _warning = warning;
        _embedded = embedded ?? LoadEmbedded();
    }

    internal string? Get(string questId, string traderId, string language)
    {
        if (IsSafeFileStem(traderId))
        {
            var traderSummaries = GetTraderSummaries(traderId, language);
            if (TryGet(traderSummaries, questId, out var traderSummary)) return traderSummary;
        }

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

    private IReadOnlyDictionary<string, string> GetTraderSummaries(string traderId, string language)
    {
        var cacheKey = $"{traderId}|{language}";
        if (_traderLanguageCache.TryGetValue(cacheKey, out var cached)) return cached;
        var summaries = new Dictionary<string, string>(StringComparer.Ordinal);
        var defaultFile = $"{traderId}.json";
        Overlay(summaries, File.Exists(Path.Combine(_externalDirectory, defaultFile))
            ? LoadExternal(defaultFile)
            : LoadExternal($"{traderId}.en.json"));
        if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            Overlay(summaries, LoadExternal($"{traderId}.{language}.json"));
        }

        return _traderLanguageCache[cacheKey] = summaries;
    }

    private IReadOnlyDictionary<string, string> LoadExternal(string fileName)
    {
        if (_externalCache.TryGetValue(fileName, out var cached)) return cached;
        var path = Path.Combine(_externalDirectory, fileName);
        if (!File.Exists(path)) return _externalCache[fileName] = new Dictionary<string, string>();

        try
        {
            using var stream = File.OpenRead(path);
            return _externalCache[fileName] = Deserialize(stream, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _warning?.Invoke($"SPT-QuestMap: could not load optional quest summaries '{path}': {exception.Message}");
            return _externalCache[fileName] = new Dictionary<string, string>();
        }
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

    private static bool TryGet(IReadOnlyDictionary<string, string> values, string questId, out string summary)
    {
        if (values.TryGetValue(questId, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            summary = value.Trim();
            return true;
        }

        summary = string.Empty;
        return false;
    }

    private static bool IsSafeFileStem(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !value.Contains(Path.DirectorySeparatorChar)
        && !value.Contains(Path.AltDirectorySeparatorChar);
}
