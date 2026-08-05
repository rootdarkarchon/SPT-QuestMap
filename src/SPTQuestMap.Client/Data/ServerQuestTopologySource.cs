using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPTQuestMap.Core.Adapters;
using SPTQuestMap.Core.Models;
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
        stopwatch.Stop();
        return new QuestTopologySourceResult(topology, rawTemplateCount, stopwatch.Elapsed.TotalMilliseconds);
    }
}

internal sealed class QuestTopologySourceResult
{
    public QuestTopologySourceResult(QuestGraphTopology topology, int rawTemplateCount, double elapsedMilliseconds)
    {
        Topology = topology;
        RawTemplateCount = rawTemplateCount;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public QuestGraphTopology Topology { get; }

    public int RawTemplateCount { get; }

    public double ElapsedMilliseconds { get; }
}
