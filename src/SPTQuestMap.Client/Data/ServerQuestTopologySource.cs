using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPTQuestMap.Core.Adapters;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using SPTQuestMap.Core.Transport;

namespace SPTQuestMap.Client.Data;

internal interface IQuestTopologySource
{
    Task<QuestTopologySourceResult> LoadAsync();
}

internal sealed class ServerQuestTopologySource : IQuestTopologySource
{
    internal const string Route = "/questmap/client/topology";

    public async Task<QuestTopologySourceResult> LoadAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var bytes = await RequestHandler.GetDataAsync(Route).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(bytes);
        var feed = JsonConvert.DeserializeObject<QuestTopologyFeed>(json)
            ?? throw new InvalidOperationException($"QuestMap topology route '{Route}' returned no data.");
        var rawTemplateCount = (feed.Topology.Quests?.Length ?? 0) + (feed.ProfileGeneratedQuests?.Length ?? 0);
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var displayStates = (feed.DisplayStates ?? new Dictionary<string, string>(StringComparer.Ordinal))
            .Where(pair => QuestGraphRules.TryParseProfileDisplayState(pair.Value, out _))
            .ToDictionary(
                pair => pair.Key,
                pair => { QuestGraphRules.TryParseProfileDisplayState(pair.Value, out var state); return state; },
                StringComparer.Ordinal);
        var profile = new QuestServerProfileProjection(
            new ReadOnlyDictionary<string, QuestMapDisplayStateKind>(displayStates),
            new ReadOnlyDictionary<string, double?>(feed.ProgressPercentages ?? new Dictionary<string, double?>(StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, long>(feed.RepeatableEndTimes ?? new Dictionary<string, long>(StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, IReadOnlyCollection<string>>(
                (feed.PrerequisiteBlockerIds ?? new Dictionary<string, string[]>(StringComparer.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => (IReadOnlyCollection<string>)(pair.Value ?? []), StringComparer.Ordinal)),
            (feed.DefaultVisibleQuestIds ?? []).Distinct(StringComparer.Ordinal).ToArray(),
            (feed.AllApplicableQuestIds ?? []).Distinct(StringComparer.Ordinal).ToArray());
        stopwatch.Stop();
        return new QuestTopologySourceResult(topology, profile, rawTemplateCount, stopwatch.Elapsed.TotalMilliseconds);
    }
}

internal sealed class QuestTopologySourceResult
{
    public QuestTopologySourceResult(QuestGraphTopology topology, QuestServerProfileProjection profile, int rawTemplateCount, double elapsedMilliseconds)
    {
        Topology = topology;
        Profile = profile;
        RawTemplateCount = rawTemplateCount;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public QuestGraphTopology Topology { get; }

    public QuestServerProfileProjection Profile { get; }

    public int RawTemplateCount { get; }

    public double ElapsedMilliseconds { get; }
}
