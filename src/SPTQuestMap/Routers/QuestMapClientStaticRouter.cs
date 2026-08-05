using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;
using SPTQuestMap.Services;

namespace SPTQuestMap.Routers;

/// <summary>
/// Read-only native-client feed. It exposes the same sanitized topology used by
/// the browser UI and profile-generated quest definitions for the authenticated
/// session, without returning or modifying a raw profile.
/// </summary>
[Injectable]
public sealed class QuestMapClientStaticRouter(
    JsonUtil jsonUtil,
    HttpResponseUtil httpResponseUtil,
    QuestMapDataService dataService)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<EmptyRequestData>(
                "/questmap/client/topology",
                (_, _, sessionId, _) =>
                {
                    var topology = dataService.GetTopology();
                    var profileState = dataService.GetProfileState(sessionId.ToString());
                    var generated = profileState?.RepeatableQuestGroups
                        .SelectMany(group => group.Quests)
                        .Select(entry => entry.Node)
                        .OrderBy(node => node.Id, StringComparer.Ordinal)
                        .ToArray() ?? [];
                    return new ValueTask<string>(httpResponseUtil.NoBody(
                        new QuestMapClientTopologyFeedDto(topology, generated)));
                })
        ])
{
    public const string Route = "/questmap/client/topology";
}
