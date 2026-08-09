using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT;
using SPTQuestMap.Core.Adapters;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestMapDataAdapter
{
    private readonly IQuestTopologySource _topologySource;
    private readonly ManualLogSource _log;
    private Dictionary<string, QuestLiveState>? _liveQuestStates;

    public QuestMapDataAdapter(IQuestTopologySource topologySource, ManualLogSource log)
    {
        _topologySource = topologySource;
        _log = log;
    }

    public QuestGraphTopology? Topology { get; private set; }

    public QuestGraphLayout? Layout { get; private set; }

    public QuestProfileOverlay? Overlay { get; private set; }

    public QuestServerProfileProjection? ServerProfile { get; private set; }

    public async Task LoadTopologyAsync()
    {
        var loaded = await _topologySource.LoadAsync().ConfigureAwait(false);
        var layoutStopwatch = Stopwatch.StartNew();
        var layout = DeterministicGraphLayout.Build(loaded.Topology);
        layoutStopwatch.Stop();

        Topology = loaded.Topology;
        Layout = layout;
        ServerProfile = loaded.Profile;
        Overlay = null;
        _liveQuestStates = null;
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M02_TOPOLOGY " +
            $"rawTemplates={loaded.RawTemplateCount}; nodes={Topology.Nodes.Count}; edges={Topology.Edges.Count}; " +
            $"serverStates={loaded.Profile.DisplayStates.Count}; repeatableTimers={loaded.Profile.RepeatableEndTimes.Count}; " +
            $"missingPredecessors={Topology.Diagnostics.MissingPredecessorIds.Count}; " +
            $"missingTargets={Topology.Diagnostics.MissingTargetIds.Count}; " +
            $"unsupportedConditions={Topology.Diagnostics.UnknownConditionCount}; " +
            $"topologyMs={loaded.ElapsedMilliseconds:F2}; layoutMs={layoutStopwatch.Elapsed.TotalMilliseconds:F2}");
    }

    public async Task LoadRepeatableTopologyDeltaAsync()
    {
        var currentTopology = Topology
            ?? throw new InvalidOperationException("QuestMap topology must be loaded before applying a repeatable delta.");
        var currentLayout = Layout
            ?? throw new InvalidOperationException("QuestMap layout must be loaded before applying a repeatable delta.");
        var loaded = await _topologySource.LoadRepeatableDeltaAsync(currentTopology).ConfigureAwait(false);
        var layoutStopwatch = Stopwatch.StartNew();
        var positions = currentLayout.NodesById
            .Where(pair => loaded.Topology.NodesById.TryGetValue(pair.Key, out var node) && !node.ProfileGenerated)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var generatedY = positions.Values.Where(position => position.Rank == 0)
            .Select(position => position.Y + position.Height + DeterministicGraphLayout.RowGap)
            .DefaultIfEmpty(0)
            .Max();
        foreach (var node in loaded.Topology.Nodes.Where(node => node.ProfileGenerated))
        {
            positions[node.Id] = new QuestNodePosition(
                node.Id,
                0,
                0,
                generatedY,
                DeterministicGraphLayout.NodeWidth,
                DeterministicGraphLayout.NodeHeight);
            generatedY += DeterministicGraphLayout.NodeHeight + DeterministicGraphLayout.RowGap;
        }
        var layout = new QuestGraphLayout(
            loaded.Topology.Version,
            new ReadOnlyDictionary<string, QuestNodePosition>(positions),
            positions.Count == 0 ? 0 : positions.Values.Max(position => position.X + position.Width),
            positions.Count == 0 ? 0 : positions.Values.Max(position => position.Y + position.Height));
        layoutStopwatch.Stop();

        Topology = loaded.Topology;
        Layout = layout;
        ServerProfile = loaded.Profile;
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M07_REPEATABLE_TOPOLOGY_DELTA " +
            $"generated={loaded.RawTemplateCount}; nodes={Topology.Nodes.Count}; edges={Topology.Edges.Count}; " +
            $"fetchMs={loaded.ElapsedMilliseconds:F2}; layoutPatchMs={layoutStopwatch.Elapsed.TotalMilliseconds:F2}; " +
            "staticNodesReused=True; edgesReused=True; fullTopologyNormalized=False; fullLayoutRebuilt=False");
    }

    public async Task<bool> RefreshServerProfileAsync()
    {
        var loaded = await _topologySource.LoadAsync().ConfigureAwait(false);
        var topologyChanged = Topology is null || !string.Equals(Topology.Version, loaded.Topology.Version, StringComparison.Ordinal);
        if (topologyChanged)
        {
            Topology = loaded.Topology;
            Layout = DeterministicGraphLayout.Build(loaded.Topology);
        }
        ServerProfile = loaded.Profile;
        return topologyChanged;
    }

    public QuestProfileOverlay RefreshOverlay(IEnumerable<QuestClass> liveQuests, Profile profile, bool logDiagnostics = true)
    {
        var topology = Topology ?? throw new InvalidOperationException("QuestMap topology must be loaded before the live overlay.");
        var stopwatch = Stopwatch.StartNew();
        var snapshot = EftLiveSnapshotAdapter.Capture(liveQuests, profile);
        var overlay = QuestOverlayBuilder.Build(topology, snapshot);
        if (ServerProfile is { } server)
        {
            overlay = overlay with
            {
                AuthoritativeDisplayStates = server.DisplayStates,
                AuthoritativeProgressPercentages = server.ProgressPercentages,
                RepeatableEndTimes = server.RepeatableEndTimes,
                PrerequisiteBlockerIds = server.PrerequisiteBlockerIds,
                DefaultVisibleQuestIds = server.DefaultVisibleQuestIds,
                ApplicableQuestIds = server.ApplicableQuestIds,
            };
        }
        else
        {
            overlay = overlay with
            {
                DefaultVisibleQuestIds = topology.DefaultVisibleQuestIds,
                ApplicableQuestIds = topology.ApplicableQuestIds,
            };
        }
        stopwatch.Stop();
        _liveQuestStates = new Dictionary<string, QuestLiveState>(overlay.QuestsById, StringComparer.Ordinal);
        overlay = overlay with
        {
            QuestsById = new ReadOnlyDictionary<string, QuestLiveState>(_liveQuestStates),
        };
        Overlay = overlay;
        if (logDiagnostics)
        {
        QuestMapDebugLog.Info(_log,
                "QUESTMAP_M02_OVERLAY " +
                $"liveQuests={snapshot.Quests.Count}; missingLiveQuests={overlay.MissingLiveQuestIds.Count}; " +
                $"overlayMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
        }
        return overlay;
    }

    public QuestProfileOverlay RefreshLiveQuests(
        IEnumerable<QuestClass> liveQuests,
        out IReadOnlyDictionary<string, QuestLiveState?> previousStates)
    {
        var overlay = Overlay ?? throw new InvalidOperationException("QuestMap overlay must be loaded before a targeted live-quest refresh.");
        var topology = Topology ?? throw new InvalidOperationException("QuestMap topology must be loaded before a targeted live-quest refresh.");
        var states = _liveQuestStates ?? throw new InvalidOperationException("QuestMap live-state cache must be loaded before a targeted live-quest refresh.");
        var replacements = liveQuests
            .Select(EftLiveSnapshotAdapter.CaptureQuest)
            .GroupBy(quest => quest.QuestId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        if (replacements.Count == 0)
        {
            previousStates = new Dictionary<string, QuestLiveState?>(StringComparer.Ordinal);
            return overlay;
        }

        var prior = new Dictionary<string, QuestLiveState?>(StringComparer.Ordinal);
        foreach (var live in replacements.Values)
        {
            if (!topology.NodesById.ContainsKey(live.QuestId))
                throw new InvalidOperationException($"Targeted live quest '{live.QuestId}' is absent from the loaded topology.");
            prior[live.QuestId] = states.GetValueOrDefault(live.QuestId);
            states[live.QuestId] = new QuestLiveState(
                live.QuestId,
                live.ExactStatus,
                true,
                live.Visible,
                QuestOverlayBuilder.NormalizeObjectiveProgress(topology.NodesById[live.QuestId], live.Objectives),
                live.ExpirationTime,
                live.HandoverReady);
        }

        previousStates = new ReadOnlyDictionary<string, QuestLiveState?>(prior);
        return overlay;
    }
}
