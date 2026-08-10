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
                    var repeatableKinds = (profileState?.RepeatableQuestGroups ?? [])
                        .SelectMany(group => group.Quests.Select(entry => (entry.Node.Id, group.Kind)))
                        .GroupBy(entry => entry.Id, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First().Kind, StringComparer.Ordinal);
                    var generatedIds = generated.Select(node => node.Id).ToArray();
                    var defaultVisibleQuestIds = (profileState?.DefaultVisibleQuestIds ?? [])
                        .Concat(generatedIds)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    var allApplicableQuestIds = (profileState?.AllApplicableQuestIds ?? [])
                        .Concat(generatedIds)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    var stateEntries = (profileState?.Quests ?? [])
                        .Concat((profileState?.RepeatableQuestGroups ?? []).SelectMany(group => group.Quests.Select(entry => entry.State)))
                        .GroupBy(state => state.QuestId, StringComparer.Ordinal)
                        .Select(group => group.Last())
                        .ToArray();
                    var displayStates = stateEntries.ToDictionary(
                        state => state.QuestId,
                        state => state.DisplayState,
                        StringComparer.Ordinal);
                    var progressPercentages = stateEntries.ToDictionary(
                        state => state.QuestId,
                        state => state.ProgressPercent,
                        StringComparer.Ordinal);
                    var repeatableEndTimes = (profileState?.RepeatableQuestGroups ?? [])
                        .SelectMany(group => group.Quests.Select(entry => (entry.Node.Id, group.EndTime)))
                        .GroupBy(entry => entry.Id, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Last().EndTime, StringComparer.Ordinal);
                    var prerequisiteBlockerIds = stateEntries.ToDictionary(
                        state => state.QuestId,
                        state => state.Blockers
                            .Where(blocker => blocker.Kind == "Prerequisite" && blocker.SubjectId is not null)
                            .Select(blocker => blocker.SubjectId!)
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal);
                    var questSummaries = topology.Quests
                        .Concat(generated)
                        .Where(node => !string.IsNullOrWhiteSpace(node.Summary))
                        .GroupBy(node => node.Id, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Last().Summary!, StringComparer.Ordinal);
                    var questMetaInfo = topology.Quests
                        .Concat(generated)
                        .Where(node => !string.IsNullOrWhiteSpace(node.WikiUrl))
                        .GroupBy(node => node.Id, StringComparer.Ordinal)
                        .ToDictionary(
                            group => group.Key,
                            group =>
                            {
                                var node = group.Last();
                                return new QuestMetaInfoDto(node.WikiUrl!, node.RelevantItems);
                            },
                            StringComparer.Ordinal);
                    return new ValueTask<string>(httpResponseUtil.NoBody(
                        new QuestMapClientTopologyFeedDto(
                            topology,
                            generated,
                            repeatableKinds,
                            defaultVisibleQuestIds,
                            allApplicableQuestIds,
                            displayStates,
                            progressPercentages,
                            repeatableEndTimes,
                            prerequisiteBlockerIds,
                            questSummaries,
                            questMetaInfo)));
                }),
            new RouteAction<EmptyRequestData>(
                "/questmap/client/repeatables",
                (_, _, sessionId, _) =>
                {
                    var profileState = dataService.GetProfileState(sessionId.ToString());
                    var generated = profileState?.RepeatableQuestGroups
                        .SelectMany(group => group.Quests)
                        .Select(entry => entry.Node)
                        .OrderBy(node => node.Id, StringComparer.Ordinal)
                        .ToArray() ?? [];
                    var repeatableKinds = (profileState?.RepeatableQuestGroups ?? [])
                        .SelectMany(group => group.Quests.Select(entry => (entry.Node.Id, group.Kind)))
                        .GroupBy(entry => entry.Id, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.First().Kind, StringComparer.Ordinal);
                    var generatedIds = generated.Select(node => node.Id).ToArray();
                    var defaultVisibleQuestIds = (profileState?.DefaultVisibleQuestIds ?? [])
                        .Concat(generatedIds)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    var allApplicableQuestIds = (profileState?.AllApplicableQuestIds ?? [])
                        .Concat(generatedIds)
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    var stateEntries = (profileState?.Quests ?? [])
                        .Concat((profileState?.RepeatableQuestGroups ?? []).SelectMany(group => group.Quests.Select(entry => entry.State)))
                        .GroupBy(state => state.QuestId, StringComparer.Ordinal)
                        .Select(group => group.Last())
                        .ToArray();
                    var displayStates = stateEntries.ToDictionary(state => state.QuestId, state => state.DisplayState, StringComparer.Ordinal);
                    var progressPercentages = stateEntries.ToDictionary(state => state.QuestId, state => state.ProgressPercent, StringComparer.Ordinal);
                    var repeatableEndTimes = (profileState?.RepeatableQuestGroups ?? [])
                        .SelectMany(group => group.Quests.Select(entry => (entry.Node.Id, group.EndTime)))
                        .GroupBy(entry => entry.Id, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Last().EndTime, StringComparer.Ordinal);
                    var prerequisiteBlockerIds = stateEntries.ToDictionary(
                        state => state.QuestId,
                        state => state.Blockers
                            .Where(blocker => blocker.Kind == "Prerequisite" && blocker.SubjectId is not null)
                            .Select(blocker => blocker.SubjectId!)
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)
                            .ToArray(),
                        StringComparer.Ordinal);
                    var summaries = generated
                        .Where(node => !string.IsNullOrWhiteSpace(node.Summary))
                        .GroupBy(node => node.Id, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.Last().Summary!, StringComparer.Ordinal);
                    var questMetaInfo = generated
                        .Where(node => !string.IsNullOrWhiteSpace(node.WikiUrl))
                        .GroupBy(node => node.Id, StringComparer.Ordinal)
                        .ToDictionary(
                            group => group.Key,
                            group =>
                            {
                                var node = group.Last();
                                return new QuestMetaInfoDto(node.WikiUrl!, node.RelevantItems);
                            },
                            StringComparer.Ordinal);
                    return new ValueTask<string>(httpResponseUtil.NoBody(
                        new QuestMapClientRepeatableFeedDto(
                            generated,
                            repeatableKinds,
                            defaultVisibleQuestIds,
                            allApplicableQuestIds,
                            displayStates,
                            progressPercentages,
                            repeatableEndTimes,
                            prerequisiteBlockerIds,
                            summaries,
                            questMetaInfo)));
                })
        ])
{
    public const string Route = "/questmap/client/topology";
    public const string RepeatablesRoute = "/questmap/client/repeatables";
}
