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
    Task<QuestTopologySourceResult> LoadProfileDeltaAsync(QuestGraphTopology current);
}

internal sealed class ServerQuestTopologySource : IQuestTopologySource
{
    internal const string Route = "/questmap/client/topology";
    internal const string RepeatablesRoute = "/questmap/client/repeatables";

    public async Task<QuestTopologySourceResult> LoadAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var stageStopwatch = Stopwatch.StartNew();
        var bytes = await RequestHandler.GetDataAsync(LocalizedRoute(Route)).ConfigureAwait(false);
        var requestMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        var json = Encoding.UTF8.GetString(bytes);
        var feed = JsonConvert.DeserializeObject<QuestTopologyFeed>(json)
            ?? throw new InvalidOperationException($"QuestMap topology route '{Route}' returned no data.");
        var rawTemplateCount = (feed.Topology.Quests?.Length ?? 0) + (feed.ProfileGeneratedQuests?.Length ?? 0);
        var deserializeMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var normalizeMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
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
        var projectionMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Stop();
        return new QuestTopologySourceResult(
            topology,
            profile,
            rawTemplateCount,
            stopwatch.Elapsed.TotalMilliseconds,
            responseBytes: bytes.Length,
            requestMilliseconds: requestMilliseconds,
            deserializeMilliseconds: deserializeMilliseconds,
            normalizeMilliseconds: normalizeMilliseconds,
            projectionMilliseconds: projectionMilliseconds);
    }

    public async Task<QuestTopologySourceResult> LoadProfileDeltaAsync(QuestGraphTopology current)
    {
        var stopwatch = Stopwatch.StartNew();
        var stageStopwatch = Stopwatch.StartNew();
        var bytes = await RequestHandler.GetDataAsync(LocalizedRoute(RepeatablesRoute)).ConfigureAwait(false);
        var requestMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        var json = Encoding.UTF8.GetString(bytes);
        var feed = JsonConvert.DeserializeObject<QuestRepeatableFeed>(json)
            ?? throw new InvalidOperationException($"QuestMap repeatable route '{RepeatablesRoute}' returned no data.");
        var deserializeMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        var staticTopologyChanged = !QuestTopologyNormalizer.MatchesStaticTopologyVersion(
            current,
            feed.StaticTopologyVersion);
        var topology = QuestTopologyNormalizer.ApplyProfileGeneratedDelta(current, feed);
        var normalizeMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stageStopwatch.Restart();
        var profile = BuildProfile(
            feed.DisplayStates,
            feed.ProgressPercentages,
            feed.RepeatableEndTimes,
            feed.PrerequisiteBlockerIds,
            feed.DefaultVisibleQuestIds,
            feed.AllApplicableQuestIds);
        var projectionMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Stop();
        return new QuestTopologySourceResult(
            topology,
            profile,
            feed.ProfileGeneratedQuests?.Length ?? 0,
            stopwatch.Elapsed.TotalMilliseconds,
            staticTopologyChanged,
            bytes.Length,
            requestMilliseconds,
            deserializeMilliseconds,
            normalizeMilliseconds,
            projectionMilliseconds);
    }

    private static QuestServerProfileProjection BuildProfile(
        Dictionary<string, string>? rawDisplayStates,
        Dictionary<string, double?>? progressPercentages,
        Dictionary<string, long>? repeatableEndTimes,
        Dictionary<string, string[]>? prerequisiteBlockerIds,
        string[]? defaultVisibleQuestIds,
        string[]? allApplicableQuestIds)
    {
        var displayStates = (rawDisplayStates ?? new Dictionary<string, string>(StringComparer.Ordinal))
            .Where(pair => QuestGraphRules.TryParseProfileDisplayState(pair.Value, out _))
            .ToDictionary(
                pair => pair.Key,
                pair => { QuestGraphRules.TryParseProfileDisplayState(pair.Value, out var state); return state; },
                StringComparer.Ordinal);
        return new QuestServerProfileProjection(
            new ReadOnlyDictionary<string, QuestMapDisplayStateKind>(displayStates),
            new ReadOnlyDictionary<string, double?>(progressPercentages ?? new Dictionary<string, double?>(StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, long>(repeatableEndTimes ?? new Dictionary<string, long>(StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, IReadOnlyCollection<string>>(
                (prerequisiteBlockerIds ?? new Dictionary<string, string[]>(StringComparer.Ordinal))
                .ToDictionary(pair => pair.Key, pair => (IReadOnlyCollection<string>)(pair.Value ?? []), StringComparer.Ordinal)),
            (defaultVisibleQuestIds ?? []).Distinct(StringComparer.Ordinal).ToArray(),
            (allApplicableQuestIds ?? []).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string LocalizedRoute(string route)
    {
        var localeManager = LocaleManagerClass.LocaleManagerClass;
        var language = localeManager?.String_0;
        if (string.IsNullOrWhiteSpace(language)) language = LocaleManagerClass.DefaultLanguage;
        if (string.IsNullOrWhiteSpace(language)) language = LocaleManagerClass.ENGLISH_LOCALIZATION;
        return $"{route}/{Uri.EscapeDataString(language)}";
    }
}

internal sealed class QuestTopologySourceResult
{
    public QuestTopologySourceResult(
        QuestGraphTopology topology,
        QuestServerProfileProjection profile,
        int rawTemplateCount,
        double elapsedMilliseconds,
        bool requiresFullTopologyReload = false,
        int responseBytes = 0,
        double requestMilliseconds = 0,
        double deserializeMilliseconds = 0,
        double normalizeMilliseconds = 0,
        double projectionMilliseconds = 0)
    {
        Topology = topology;
        Profile = profile;
        RawTemplateCount = rawTemplateCount;
        ElapsedMilliseconds = elapsedMilliseconds;
        RequiresFullTopologyReload = requiresFullTopologyReload;
        ResponseBytes = responseBytes;
        RequestMilliseconds = requestMilliseconds;
        DeserializeMilliseconds = deserializeMilliseconds;
        NormalizeMilliseconds = normalizeMilliseconds;
        ProjectionMilliseconds = projectionMilliseconds;
    }

    public QuestGraphTopology Topology { get; }

    public QuestServerProfileProjection Profile { get; }

    public int RawTemplateCount { get; }

    public double ElapsedMilliseconds { get; }

    public bool RequiresFullTopologyReload { get; }

    public int ResponseBytes { get; }

    public double RequestMilliseconds { get; }

    public double DeserializeMilliseconds { get; }

    public double NormalizeMilliseconds { get; }

    public double ProjectionMilliseconds { get; }
}
