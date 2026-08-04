using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

internal sealed class QuestMapLocalizationService(DatabaseService databaseService, LocaleService localeService)
{
    internal QuestMapBootstrapDto GetBootstrap(string? requestedLanguage)
    {
        var language = ResolveLanguage(requestedLanguage);
        var locales = databaseService.GetLocales();
        var languages = locales.Languages
            .Where(pair => locales.Global.ContainsKey(pair.Key))
            .Select(pair => new QuestMapLanguageDto(pair.Key, pair.Value))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.Ordinal)
            .ToArray();

        return new QuestMapBootstrapDto(
            language,
            ToBrowserLocale(language),
            languages,
            QuestMapUiCatalog.For(language, localeService.GetLocaleDb(language))
        );
    }

    internal string ResolveLanguage(string? requestedLanguage)
    {
        var locales = databaseService.GetLocales().Global;
        var requested = requestedLanguage?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(requested) && locales.ContainsKey(requested)) return requested;

        var configured = localeService.GetDesiredGameLocale();
        if (locales.ContainsKey(configured)) return configured;
        if (locales.ContainsKey("en")) return "en";
        return locales.Keys.Order(StringComparer.Ordinal).First();
    }

    internal static string ToBrowserLocale(string language) => language.ToLowerInvariant() switch
    {
        "ch" => "zh-CN",
        "cz" => "cs",
        "ge" => "de",
        "jp" => "ja",
        "kr" => "ko",
        "po" => "pt-PT",
        "tu" => "tr",
        _ => language,
    };
}
