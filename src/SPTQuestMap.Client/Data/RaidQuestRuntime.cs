using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Logging;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.UI;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class RaidQuestRuntime : IDisposable
{
    private const float PollIntervalSeconds = 0.1f;
    private const int PollCheckerBudget = 16;

    private readonly ManualLogSource _log;
    private readonly QuestMapClientConfiguration _configuration;
    private readonly QuestMapDataAdapter _adapter;
    private readonly QuestTrackingService _tracking;
    private readonly RaidQuestProgressNotificationStack _notification;
    private readonly RaidTrackedQuestListView _trackedList;
    private readonly RaidPerformanceTelemetry _telemetry;
    private readonly Action<AbstractQuestControllerClass> _observeQuestController;
    private readonly Action _clearObservedQuestController;
    private readonly Action<string, string?, string?> _requestRefresh;
    private readonly Action _clearRefreshSignals;
    private readonly HashSet<string> _currentMapIds = new(StringComparer.OrdinalIgnoreCase);
    private AbstractQuestControllerClass? _questController;
    private RaidQuestProgressMonitor? _progressMonitor;
    private string? _locationId;
    private float _nextPollAt;
    private bool _active;
    private bool _disposed;

    public RaidQuestRuntime(
        MonoBehaviour owner,
        ManualLogSource log,
        QuestMapClientConfiguration configuration,
        QuestMapDataAdapter adapter,
        QuestAssetSpriteCache assetCache,
        QuestTrackingService tracking,
        Action<AbstractQuestControllerClass> observeQuestController,
        Action clearObservedQuestController,
        Action<string, string?, string?> requestRefresh,
        Action clearRefreshSignals)
    {
        _log = log;
        _configuration = configuration;
        _adapter = adapter;
        _tracking = tracking;
        _observeQuestController = observeQuestController;
        _clearObservedQuestController = clearObservedQuestController;
        _requestRefresh = requestRefresh;
        _clearRefreshSignals = clearRefreshSignals;
        _telemetry = new RaidPerformanceTelemetry(log);
        _notification = RaidQuestProgressNotificationStack.Create(
            owner.transform,
            log,
            () => _configuration.RaidNotificationOpacity.Value,
            () => _configuration.RaidNotificationMinimal.Value,
            () => _configuration.InRaidQuestTaskTextSize.Value,
            () => _configuration.RaidOverlayFadeDurationSeconds.Value,
            () => _configuration.RaidNotificationDisplayDurationSeconds.Value,
            assetCache);
        _trackedList = RaidTrackedQuestListView.Create(
            owner.transform,
            log,
            () => _configuration.TrackedQuestListHotkey.Value,
            () => _configuration.RaidNotificationOpacity.Value,
            () => _configuration.InRaidQuestTaskTextSize.Value,
            () => _configuration.RaidOverlayFadeDurationSeconds.Value,
            () => _configuration.TrackedQuestListDisplayDurationSeconds.Value,
            BuildTrackedQuestListProjection,
            assetCache);
        _tracking.TrackingChanged += OnTrackingChanged;
        _configuration.SmartInRaidTracking.SettingChanged += OnSmartTrackingChanged;
    }

    public bool Active => _active;

    public void Update()
    {
        if (_disposed || Time.unscaledTime < _nextPollAt) return;
        _nextPollAt = Time.unscaledTime + PollIntervalSeconds;
        var startedAt = Stopwatch.GetTimestamp();

        if (!InRaidQuestContext.TryCapture(out var raid))
        {
            End();
            return;
        }

        var enteringRaid = !_active
            || !ReferenceEquals(_questController, raid.QuestController)
            || !string.Equals(_locationId, raid.LocationId, StringComparison.OrdinalIgnoreCase);
        if (enteringRaid) _telemetry.Begin();

        if (enteringRaid)
        {
            _progressMonitor?.Dispose();
            _active = true;
            _questController = raid.QuestController;
            _locationId = raid.LocationId;
            _currentMapIds.Clear();
            var currentMapIds = _adapter.Topology is null
                ? new[] { raid.LocationId }
                : QuestObjectiveMapRules.ExpandMapIds([raid.LocationId], _adapter.Topology.MapAliases);
            _currentMapIds.UnionWith(currentMapIds);
            _tracking.BeginRaid(_currentMapIds);
            if (_adapter.Overlay is not null) _tracking.ReloadFavorites(_adapter.Overlay.ProfileId);
            _progressMonitor = new RaidQuestProgressMonitor(
                raid.QuestController,
                RequestProgressRefresh);
            _observeQuestController(raid.QuestController);
            QuestMapDebugLog.Info(_log,
                "QUESTMAP_M06_RAID_MONITOR active=True; " +
                $"location={raid.LocationId}; intervalMs={PollIntervalSeconds * 1000:0}; " +
                $"checkerBudget={PollCheckerBudget}; screenIndependent=True; " +
                "broadReactiveEvents=False; signal=checker-events+incremental-poll");
            return;
        }

        var pollResult = _progressMonitor?.Poll(PollCheckerBudget) ?? default;
        _telemetry.RecordPoll(ElapsedMilliseconds(startedAt), pollResult);
        _telemetry.LogIfDue();
    }

    public void PublishProgressChanges(
        QuestGraphTopology topology,
        QuestProfileOverlay oldOverlay,
        QuestProfileOverlay newOverlay,
        IReadOnlyCollection<string> changedQuestIds,
        IReadOnlyDictionary<string, string> preferredObjectiveIds)
    {
        foreach (var questId in changedQuestIds)
        {
            if (!topology.NodesById.TryGetValue(questId, out var node)
                || !newOverlay.QuestsById.TryGetValue(questId, out var current)
                || !current.HasLiveQuest) continue;

            var tracking = _tracking.Resolve(newOverlay.ProfileId, node);
            if (!tracking.Tracked)
            {
                if (_configuration.EnableDebugLogging.Value)
                    QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_RAID_NOTIFICATION_SUPPRESSED quest={questId}; reason=untracked");
                continue;
            }

            oldOverlay.QuestsById.TryGetValue(questId, out var previous);
            var previousById = previous?.Objectives
                .GroupBy(objective => objective.ObjectiveId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
                ?? new Dictionary<string, QuestObjectiveProgress>(StringComparer.Ordinal);
            var changedObjectives = current.Objectives
                .Where(objective => !previousById.TryGetValue(objective.ObjectiveId, out var prior) || prior != objective)
                .ToArray();
            var notificationObjectives = changedObjectives
                .Where(objective => IsActiveObjective(node, objective.ObjectiveId))
                .Where(objective => RaidProgressNotificationRules.HasEffectiveIncrease(objective, previousById))
                .ToArray();

            if (notificationObjectives.Length == 0)
            {
                if (HasActiveObjective(node)
                    && !string.Equals(previous?.ExactStatus, current.ExactStatus, StringComparison.Ordinal))
                {
                    _notification.Show(new InRaidQuestProgressChange(
                        node,
                        null,
                        null,
                        current.ExactStatus ?? "Unknown"));
                }
                continue;
            }

            preferredObjectiveIds.TryGetValue(questId, out var preferredObjectiveId);
            var progress = RaidProgressNotificationRules.Select(
                node,
                notificationObjectives,
                previousById,
                preferredObjectiveId);
            var definition = node.Objectives.FirstOrDefault(objective =>
                string.Equals(objective.Id, progress.ObjectiveId, StringComparison.Ordinal));
            QuestMapDebugLog.Info(_log,
                "QUESTMAP_M06_RAID_NOTIFICATION " +
                $"quest={questId}; objective={progress.ObjectiveId}; candidates={notificationObjectives.Length}; " +
                $"suppressed={changedObjectives.Length - notificationObjectives.Length}; " +
                $"leaf={RaidProgressNotificationRules.IsLeafObjective(node, progress.ObjectiveId)}; complete={progress.Complete}; " +
                $"preferred={string.Equals(progress.ObjectiveId, preferredObjectiveId, StringComparison.Ordinal)}; " +
                $"numericDelta={RaidProgressNotificationRules.NumericDelta(progress, previousById):0.##}; " +
                $"effectiveDelta={RaidProgressNotificationRules.EffectivePositiveProgressDelta(progress, previousById):0.##}; " +
                $"candidateDetail={DescribeCandidates(node, notificationObjectives, previousById, preferredObjectiveId)}");
            _notification.Show(new InRaidQuestProgressChange(
                node,
                definition,
                progress,
                current.ExactStatus ?? "Unknown"));
        }
    }

    public void RefreshTrackedListIfVisible() => _trackedList.RefreshIfVisible();

    public void RecordOverlayRefresh(double elapsedMilliseconds)
        => _telemetry.RecordOverlayRefresh(elapsedMilliseconds);

    public void RecordQuestPatch(double modelMilliseconds, double uiMilliseconds)
        => _telemetry.RecordQuestPatch(modelMilliseconds, uiMilliseconds);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _questController = null;
        _progressMonitor?.Dispose();
        _progressMonitor = null;
        _locationId = null;
        _currentMapIds.Clear();
        _active = false;
        _notification.Dispose();
        _tracking.TrackingChanged -= OnTrackingChanged;
        _configuration.SmartInRaidTracking.SettingChanged -= OnSmartTrackingChanged;
        _trackedList.Dispose();
    }

    private void End()
    {
        if (!_active) return;
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_RAID_MONITOR active=False; " +
            $"location={_locationId}; reason=raid-ended");
        _active = false;
        _questController = null;
        _locationId = null;
        _currentMapIds.Clear();
        _progressMonitor?.Dispose();
        _progressMonitor = null;
        _clearRefreshSignals();
        _telemetry.LogFinal();
        _notification.Hide();
        _trackedList.Hide();
        _tracking.EndRaid();
        _clearObservedQuestController();
    }

    private RaidTrackedQuestListProjection BuildTrackedQuestListProjection()
    {
        var topology = _adapter.Topology;
        var overlay = _adapter.Overlay;
        if (!_active || topology is null || overlay is null
            || !InRaidQuestContext.TryCapture(out var raid))
            return RaidTrackedQuestListProjection.Empty;

        var trackedQuestIds = topology.Nodes
            .Where(node => _tracking.Resolve(overlay.ProfileId, node).Tracked)
            .Select(node => node.Id)
            .ToArray();
        return RaidTrackedQuestListProjectionBuilder.Build(
            topology,
            overlay,
            trackedQuestIds,
            [raid.LocationId],
            _configuration.SmartInRaidTracking.Value);
    }

    private void OnTrackingChanged(string? profileId, string? questId) =>
        _trackedList.RefreshIfVisible();

    private void OnSmartTrackingChanged(object? sender, EventArgs args) =>
        _trackedList.RefreshIfVisible();

    private void RequestProgressRefresh(
        string questId,
        string? objectiveId,
        RaidQuestChangeKind changeKind)
    {
        if (!string.IsNullOrWhiteSpace(questId)
            && !string.IsNullOrWhiteSpace(objectiveId)
            && _adapter.Topology?.NodesById.TryGetValue(questId, out var node) == true
            && !IsActiveObjective(node, objectiveId!))
        {
            QuestMapDebugLog.Info(_log,
                $"QUESTMAP_M06_RAID_OBJECTIVE_SUPPRESSED quest={questId}; objective={objectiveId}; reason=task-map-not-current");
            return;
        }

        _requestRefresh(
            changeKind switch
            {
                RaidQuestChangeKind.Objective => "raid-objective-event",
                RaidQuestChangeKind.Status => "raid-status-event",
                RaidQuestChangeKind.Book => "raid-book-event",
                _ => throw new ArgumentOutOfRangeException(nameof(changeKind), changeKind, null),
            },
            string.IsNullOrWhiteSpace(questId) ? null : questId,
            string.IsNullOrWhiteSpace(questId) || string.IsNullOrWhiteSpace(objectiveId) ? null : objectiveId);
    }

    private bool IsActiveObjective(QuestGraphNode node, string objectiveId)
    {
        var definition = node.Objectives.FirstOrDefault(objective =>
            string.Equals(objective.Id, objectiveId, StringComparison.Ordinal));
        return definition is null || QuestObjectiveMapRules.IsObjectiveActiveInRaid(
            node, definition, _currentMapIds, smartTracking: false);
    }

    private bool HasActiveObjective(QuestGraphNode node) =>
        node.Objectives.Count == 0
        || node.Objectives.Any(objective => QuestObjectiveMapRules.IsObjectiveActiveInRaid(
            node, objective, _currentMapIds, smartTracking: false));

    private static string DescribeCandidates(
        QuestGraphNode node,
        IReadOnlyCollection<QuestObjectiveProgress> candidates,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById,
        string? preferredObjectiveId) =>
        string.Join(",", candidates.Select(candidate =>
            $"{candidate.ObjectiveId}:{RaidProgressNotificationRules.ObjectiveIndex(node, candidate.ObjectiveId)}:" +
            $"{RaidProgressNotificationRules.NumericDelta(candidate, previousById):0.##}:{candidate.Complete}:" +
            $"{string.Equals(candidate.ObjectiveId, preferredObjectiveId, StringComparison.Ordinal)}"));

    private static double ElapsedMilliseconds(long startedAt) =>
        (Stopwatch.GetTimestamp() - startedAt) * 1000d / Stopwatch.Frequency;
}

internal readonly struct InRaidQuestProgressChange
{
    public InRaidQuestProgressChange(
        QuestGraphNode quest,
        QuestObjectiveDefinition? objective,
        QuestObjectiveProgress? progress,
        string exactStatus)
    {
        Quest = quest;
        Objective = objective;
        Progress = progress;
        ExactStatus = exactStatus;
    }

    public QuestGraphNode Quest { get; }

    public QuestObjectiveDefinition? Objective { get; }

    public QuestObjectiveProgress? Progress { get; }

    public string ExactStatus { get; }
}
