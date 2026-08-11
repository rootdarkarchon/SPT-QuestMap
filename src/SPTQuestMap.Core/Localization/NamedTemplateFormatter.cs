using System.Globalization;
using System.Text.RegularExpressions;

namespace SPTQuestMap.Core.Localization;

public static class NamedTemplateFormatter
{
    private static readonly Regex Placeholder = new(
        @"\{(?<name>[A-Za-z][A-Za-z0-9_]*)(?::(?<format>[^{}]+))?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Format(
        string template,
        IReadOnlyDictionary<string, object?> values,
        IFormatProvider? formatProvider = null)
    {
        if (string.IsNullOrEmpty(template) || values.Count == 0) return template;
        var provider = formatProvider ?? CultureInfo.CurrentCulture;
        return Placeholder.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            if (!values.TryGetValue(name, out var value)) return match.Value;
            if (value is null) return string.Empty;
            var format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
            return value is IFormattable formattable
                ? formattable.ToString(format, provider) ?? string.Empty
                : Convert.ToString(value, provider) ?? string.Empty;
        });
    }
}
