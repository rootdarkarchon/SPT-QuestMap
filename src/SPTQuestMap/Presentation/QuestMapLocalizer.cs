using System.Globalization;
using System.Text.RegularExpressions;
using SPTQuestMap.Services;

namespace SPTQuestMap.Presentation;

public sealed class QuestMapLocalizer(QuestMapBootstrapDto bootstrap)
{
    private static readonly Regex PlaceholderPattern = new(@"\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

    public string Language { get; } = bootstrap.Language;
    public string BrowserLocale { get; } = bootstrap.BrowserLocale;
    public IReadOnlyDictionary<string, string> Strings { get; } = bootstrap.Strings;

    public string this[string key] => Strings.TryGetValue(key, out var value) ? value : key;

    public string Format(string key, params (string Name, object? Value)[] values)
    {
        var replacements = values.ToDictionary(pair => pair.Name, pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? string.Empty, StringComparer.Ordinal);
        return PlaceholderPattern.Replace(this[key], match => replacements.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
    }
}
