using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public sealed class QuestMapDataService : IOnLoad
{
    private readonly DatabaseService _databaseService;
    private readonly QuestMapLocalizationService _localization;
    private readonly QuestMetaInfoCatalog _metaInfoCatalog;
    private readonly QuestTopologyBuilder _topology;
    private readonly QuestProfileStateBuilder _profiles;
    private readonly QuestMapTopologyPreload _preload;
    private readonly SaveServer _saveServer;

    public QuestMapDataService(
        DatabaseService databaseService,
        LocaleService localeService,
        SaveServer saveServer,
        QuestHelper questHelper,
        SeasonalEventService seasonalEventService,
        QuestMapTopologyPreload preload,
        ISptLogger<QuestMapDataService> logger
    )
    {
        _databaseService = databaseService;
        _saveServer = saveServer;
        _preload = preload;
        _localization = new QuestMapLocalizationService(databaseService, localeService);
        _metaInfoCatalog = new QuestMetaInfoCatalog(
            warning: message => logger.Warning(message));
        var zoneMapCatalog = new QuestZoneMapCatalog();
        _topology = new QuestTopologyBuilder(
            databaseService,
            localeService,
            questHelper,
            seasonalEventService,
            preload,
            new QuestSummaryCatalog(warning: message => logger.Warning(message)),
            _metaInfoCatalog,
            zoneMapCatalog,
            logger
        );
        _profiles = new QuestProfileStateBuilder(
            databaseService,
            localeService,
            saveServer,
            questHelper,
            seasonalEventService,
            preload,
            zoneMapCatalog,
            logger
        );
    }

    public Task OnLoad()
    {
        _metaInfoCatalog.Resolve(_preload.Items);
        return Task.CompletedTask;
    }

    public QuestTopologyDto GetTopology(string? requestedLanguage = null) =>
        _topology.Get(_localization.ResolveLanguage(requestedLanguage));

    public QuestMapBootstrapDto GetBootstrap(string? requestedLanguage = null) =>
        _localization.GetBootstrap(requestedLanguage);

    public IReadOnlyList<ProfileSummaryDto> GetProfiles(string? requestedLanguage = null)
    {
        var strings = GetBootstrap(requestedLanguage).Strings;
        return _saveServer
            .GetProfiles()
            .Where(pair => !_saveServer.IsProfileInvalidOrUnloadable(pair.Key))
            .Select(pair =>
            {
                var info = pair.Value.CharacterData?.PmcData?.Info;
                return new ProfileSummaryDto(
                    pair.Key.ToString(),
                    info?.Nickname ?? strings["profile.unnamed"],
                    info?.Side ?? strings["profile.unknownSide"],
                    info?.Level ?? 0
                );
            })
            .Where(profile => QuestMapProfilePolicy.ShouldInclude(profile.Nickname, profile.Level))
            .OrderBy(profile => profile.Nickname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public ProfileStateDto? GetProfileState(string rawProfileId, string? requestedLanguage = null)
    {
        var language = _localization.ResolveLanguage(requestedLanguage);
        return _profiles.Build(rawProfileId, GetTopology(language), language);
    }
}
