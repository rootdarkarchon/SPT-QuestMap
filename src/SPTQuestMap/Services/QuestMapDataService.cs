using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

[Injectable(InjectionType.Singleton)]
public sealed class QuestMapDataService
{
    private readonly QuestMapLocalizationService _localization;
    private readonly QuestTopologyBuilder _topology;
    private readonly QuestProfileStateBuilder _profiles;
    private readonly SaveServer _saveServer;

#pragma warning disable CS0618 // SPT 4.0.13 provides ConfigServer; direct config DI starts in 4.1.
    public QuestMapDataService(
        DatabaseService databaseService,
        LocaleService localeService,
        SaveServer saveServer,
        QuestHelper questHelper,
        SeasonalEventService seasonalEventService,
        ConfigServer configServer,
        ISptLogger<QuestMapDataService> logger
    )
#pragma warning restore CS0618
    {
        _saveServer = saveServer;
        _localization = new QuestMapLocalizationService(databaseService, localeService);
        _topology = new QuestTopologyBuilder(
            databaseService,
            localeService,
            questHelper,
            seasonalEventService,
#pragma warning disable CS0618 // SPT 4.0.13 exposes config instances through ConfigServer.
            configServer.GetConfig<QuestConfig>(),
#pragma warning restore CS0618
            logger
        );
        _profiles = new QuestProfileStateBuilder(
            databaseService,
            localeService,
            saveServer,
            questHelper,
            seasonalEventService,
#pragma warning disable CS0618 // SPT 4.0.13 exposes config instances through ConfigServer.
            configServer.GetConfig<QuestConfig>(),
#pragma warning restore CS0618
            logger
        );
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
