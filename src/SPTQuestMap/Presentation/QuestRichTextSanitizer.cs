using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace SPTQuestMap.Presentation;

public static partial class QuestRichTextSanitizer
{
    private static readonly IReadOnlyDictionary<string, string> AllowedTags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "strong", ["strong"] = "strong", ["i"] = "em", ["em"] = "em", ["u"] = "u",
        ["s"] = "s", ["strike"] = "s", ["p"] = "p", ["div"] = "div", ["blockquote"] = "blockquote",
        ["ul"] = "ul", ["ol"] = "ol", ["li"] = "li", ["code"] = "code", ["span"] = "span",
        ["font"] = "span", ["a"] = "a",
    };

    private static readonly HashSet<string> DangerousTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "svg", "math", "form", "input", "button", "textarea", "select", "meta", "link",
    };

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var source = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        source = DangerousBlockPattern().Replace(source, string.Empty);
        source = ColorOpenPattern().Replace(source, match =>
        {
            var color = SafeColor(match.Groups[1].Value);
            return color is null ? "<span>" : $"<span data-qm-color=\"{color}\">";
        });
        source = ColorClosePattern().Replace(source, "</span>");
        source = SizeOpenPattern().Replace(source, match =>
        {
            var size = SafeSize(match.Groups[1].Value);
            return size is null ? "<span>" : $"<span data-qm-size=\"{size}\">";
        });
        source = SizeClosePattern().Replace(source, "</span>");
        source = AlignOpenPattern().Replace(source, match => $"<div data-qm-align=\"{match.Groups[1].Value.ToLowerInvariant()}\">");
        source = AlignClosePattern().Replace(source, "</div>");
        source = RepeatedBreakPattern().Replace(source, "\n\n");

        var output = new StringBuilder(source.Length + 64);
        foreach (var block in ParagraphPattern().Split(source).Where(block => !string.IsNullOrWhiteSpace(block)))
        {
            output.Append("<div class=\"description-paragraph\">");
            SanitizeBlock(block, output);
            output.Append("</div>");
        }

        return output.ToString();
    }

    public static string StripMarkup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var withoutTags = TagPattern().Replace(value, string.Empty);
        return WebUtility.HtmlDecode(withoutTags).Replace("\r", string.Empty, StringComparison.Ordinal).Replace('\n', ' ').Trim();
    }

    internal static string? SafeColor(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().Trim('"', '\'');
        if (HexColorPattern().IsMatch(value)) return value;
        return value.ToLowerInvariant() is "red" or "yellow" or "green" or "blue" or "cyan" or "magenta" or "grey" or "gray" or "white" or "black" or "orange"
            ? value.ToLowerInvariant()
            : null;
    }

    internal static string? SafeSize(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().Trim('"', '\'');
        if (value.EndsWith('%') && double.TryParse(value[..^1], out var percent)) return $"{Math.Clamp(percent, 60, 180):0.##}%";
        var numeric = value.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value;
        return double.TryParse(numeric, out var pixels) ? $"{Math.Clamp(pixels, 10, 24):0.##}px" : null;
    }

    private static void SanitizeBlock(string block, StringBuilder output)
    {
        var cursor = 0;
        foreach (Match match in TagPattern().Matches(block))
        {
            AppendText(block[cursor..match.Index], output);
            AppendTag(match.Value, output);
            cursor = match.Index + match.Length;
        }

        AppendText(block[cursor..], output);
    }

    private static void AppendText(string value, StringBuilder output)
    {
        var lines = value.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0) output.Append("<br>");
            output.Append(HtmlEncoder.Default.Encode(lines[index]));
        }
    }

    private static void AppendTag(string token, StringBuilder output)
    {
        var match = TagNamePattern().Match(token);
        if (!match.Success)
        {
            output.Append(HtmlEncoder.Default.Encode(token));
            return;
        }

        var closing = match.Groups[1].Success;
        var name = match.Groups[2].Value.ToLowerInvariant();
        if (name == "br")
        {
            if (!closing) output.Append("<br>");
            return;
        }

        if (DangerousTags.Contains(name)) return;
        if (!AllowedTags.TryGetValue(name, out var mapped))
        {
            output.Append(HtmlEncoder.Default.Encode(token));
            return;
        }

        if (closing)
        {
            output.Append("</").Append(mapped).Append('>');
            return;
        }

        output.Append('<').Append(mapped);
        var styles = new List<string>(3);
        var color = SafeColor(ReadAttribute(token, "data-qm-color") ?? ReadAttribute(token, "color"));
        var size = SafeSize(ReadAttribute(token, "data-qm-size") ?? ReadAttribute(token, "size"));
        var align = ReadAttribute(token, "data-qm-align");
        if (color is not null) styles.Add($"color:{color}");
        if (size is not null) styles.Add($"font-size:{size}");
        if (align is "left" or "center" or "right") styles.Add($"text-align:{align}");
        if (styles.Count > 0) output.Append(" style=\"").Append(string.Join(';', styles)).Append('"');

        if (name == "a")
        {
            var href = ReadAttribute(token, "href");
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                output.Append(" href=\"").Append(HtmlEncoder.Default.Encode(href!)).Append("\" target=\"_blank\" rel=\"noopener noreferrer\"");
            }
        }

        output.Append('>');
    }

    private static string? ReadAttribute(string token, string name)
    {
        var match = Regex.Match(token, $"\\b{Regex.Escape(name)}\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups.Cast<Group>().Skip(1).FirstOrDefault(group => group.Success)?.Value : null;
    }

    [GeneratedRegex(@"<(script|style|iframe|object|embed|svg|math|form)[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DangerousBlockPattern();
    [GeneratedRegex(@"<color\s*=\s*([^>]+)>", RegexOptions.IgnoreCase)] private static partial Regex ColorOpenPattern();
    [GeneratedRegex(@"</color\s*>", RegexOptions.IgnoreCase)] private static partial Regex ColorClosePattern();
    [GeneratedRegex(@"<size\s*=\s*([^>]+)>", RegexOptions.IgnoreCase)] private static partial Regex SizeOpenPattern();
    [GeneratedRegex(@"</size\s*>", RegexOptions.IgnoreCase)] private static partial Regex SizeClosePattern();
    [GeneratedRegex(@"<align\s*=\s*[""']?(left|center|right)[""']?\s*>", RegexOptions.IgnoreCase)] private static partial Regex AlignOpenPattern();
    [GeneratedRegex(@"</align\s*>", RegexOptions.IgnoreCase)] private static partial Regex AlignClosePattern();
    [GeneratedRegex(@"(?:<br\s*/?>(?:[ \t]*)){2,}", RegexOptions.IgnoreCase)] private static partial Regex RepeatedBreakPattern();
    [GeneratedRegex(@"\n[ \t]*\n+")] private static partial Regex ParagraphPattern();
    [GeneratedRegex(@"<[^>]*>")] private static partial Regex TagPattern();
    [GeneratedRegex(@"^<\s*(/)?\s*([a-zA-Z0-9]+)")] private static partial Regex TagNamePattern();
    [GeneratedRegex(@"^#[0-9a-f]{3,8}$", RegexOptions.IgnoreCase)] private static partial Regex HexColorPattern();
}
