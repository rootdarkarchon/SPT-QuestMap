using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        _log.LogInfo(
            "QUESTMAP_M02_TOPOLOGY " +
            $"rawTemplates={loaded.RawTemplateCount}; nodes={Topology.Nodes.Count}; edges={Topology.Edges.Count}; " +
            $"serverStates={loaded.Profile.DisplayStates.Count}; repeatableTimers={loaded.Profile.RepeatableEndTimes.Count}; " +
            $"missingPredecessors={Topology.Diagnostics.MissingPredecessorIds.Count}; " +
            $"missingTargets={Topology.Diagnostics.MissingTargetIds.Count}; " +
            $"unsupportedConditions={Topology.Diagnostics.UnknownConditionCount}; " +
            $"topologyMs={loaded.ElapsedMilliseconds:F2}; layoutMs={layoutStopwatch.Elapsed.TotalMilliseconds:F2}");
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
        Overlay = overlay;
        if (logDiagnostics)
        {
            _log.LogInfo(
                "QUESTMAP_M02_OVERLAY " +
                $"liveQuests={snapshot.Quests.Count}; missingLiveQuests={overlay.MissingLiveQuestIds.Count}; " +
                $"overlayMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
        }
        return overlay;
    }
}
