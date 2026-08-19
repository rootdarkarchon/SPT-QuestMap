using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using SPTQuestMap.Core.Localization;

namespace SPTQuestMap.Client.Localization;

internal static class ClientLocale
{
    private const string English = "en";
    private static readonly Assembly Assembly = typeof(ClientLocale).Assembly;
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Text(string key)
    {
        var language = CurrentLanguage();
        if (TryText(language, key, out var value) || TryText(English, key, out value)) return value;
        return key;
    }

    public static string Format(string key, params ClientTextArgument[] arguments)
    {
        var values = arguments.ToDictionary(argument => argument.Name, argument => argument.Value, StringComparer.Ordinal);
        return NamedTemplateFormatter.Format(Text(key), values, CultureInfo.CurrentCulture);
    }

    public static ClientTextArgument Arg(string name, object? value) => new(name, value);

    private static string CurrentLanguage()
    {
        var language = LocaleManagerClass.LocaleManagerClass?.String_0;
        if (string.IsNullOrWhiteSpace(language)) language = LocaleManagerClass.DefaultLanguage;
        return string.IsNullOrWhiteSpace(language) ? English : language;
    }

    private static bool TryText(string language, string key, out string value)
    {
        var catalog = LoadCatalog(language);
        return catalog.TryGetValue(key, out value!);
    }

    private static IReadOnlyDictionary<string, string> LoadCatalog(string language)
    {
        if (Catalogs.TryGetValue(language, out var cached)) return cached;
        var suffix = $".Localization.Locales.{language}.json";
        var resourceName = Assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        IReadOnlyDictionary<string, string> catalog = new Dictionary<string, string>(StringComparer.Ordinal);
        if (resourceName is not null)
        {
            using var stream = Assembly.GetManifestResourceStream(resourceName);
            using var reader = stream is null ? null : new StreamReader(stream);
            var parsed = reader is null
                ? null
                : JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd());
            if (parsed is not null) catalog = new Dictionary<string, string>(parsed, StringComparer.Ordinal);
        }
        Catalogs[language] = catalog;
        return catalog;
    }
}

internal readonly struct ClientTextArgument
{
    public ClientTextArgument(string name, object? value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public object? Value { get; }
}
