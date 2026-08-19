using System;
using BepInEx.Logging;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class RaidPerformanceTelemetry
{
    private const float LogIntervalSeconds = 30f;
    private readonly ManualLogSource _log;
    private float _nextLogAt;
    private int _pollCount;
    private int _polledCheckerCount;
    private int _pollChangeCount;
    private int _overlayRefreshCount;
    private int _questPatchCount;
    private double _pollTotalMilliseconds;
    private double _pollMaximumMilliseconds;
    private double _overlayTotalMilliseconds;
    private double _overlayMaximumMilliseconds;
    private double _questPatchTotalMilliseconds;
    private double _questPatchMaximumMilliseconds;
    private double _questUiTotalMilliseconds;
    private double _questUiMaximumMilliseconds;

    public RaidPerformanceTelemetry(ManualLogSource log)
    {
        _log = log;
    }

    public void Begin()
    {
        _pollCount = 0;
        _polledCheckerCount = 0;
        _pollChangeCount = 0;
        _overlayRefreshCount = 0;
        _questPatchCount = 0;
        _pollTotalMilliseconds = 0;
        _pollMaximumMilliseconds = 0;
        _overlayTotalMilliseconds = 0;
        _overlayMaximumMilliseconds = 0;
        _questPatchTotalMilliseconds = 0;
        _questPatchMaximumMilliseconds = 0;
        _questUiTotalMilliseconds = 0;
        _questUiMaximumMilliseconds = 0;
        _nextLogAt = Time.unscaledTime + LogIntervalSeconds;
    }

    public void RecordPoll(double elapsedMilliseconds, RaidQuestPollResult result)
    {
        _pollCount++;
        _polledCheckerCount += result.ScannedCheckers;
        _pollChangeCount += result.Changes;
        _pollTotalMilliseconds += elapsedMilliseconds;
        _pollMaximumMilliseconds = Math.Max(_pollMaximumMilliseconds, elapsedMilliseconds);
    }

    public void RecordOverlayRefresh(double elapsedMilliseconds)
    {
        _overlayRefreshCount++;
        _overlayTotalMilliseconds += elapsedMilliseconds;
        _overlayMaximumMilliseconds = Math.Max(_overlayMaximumMilliseconds, elapsedMilliseconds);
    }

    public void RecordQuestPatch(double modelMilliseconds, double uiMilliseconds)
    {
        _questPatchCount++;
        _questPatchTotalMilliseconds += modelMilliseconds;
        _questPatchMaximumMilliseconds = Math.Max(_questPatchMaximumMilliseconds, modelMilliseconds);
        _questUiTotalMilliseconds += uiMilliseconds;
        _questUiMaximumMilliseconds = Math.Max(_questUiMaximumMilliseconds, uiMilliseconds);
    }

    public void LogIfDue()
    {
        if (Time.unscaledTime < _nextLogAt) return;
        Log(false);
        _nextLogAt = Time.unscaledTime + LogIntervalSeconds;
    }

    public void LogFinal() => Log(true);

    private void Log(bool final)
    {
        if (_pollCount == 0) return;
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_RAID_PERF " +
            $"final={final}; polls={_pollCount}; scannedCheckers={_polledCheckerCount}; " +
            $"pollChanges={_pollChangeCount}; " +
            $"pollAvgMs={_pollTotalMilliseconds / _pollCount:0.###}; pollMaxMs={_pollMaximumMilliseconds:0.###}; " +
            $"overlayRefreshes={_overlayRefreshCount}; " +
            $"overlayAvgMs={(_overlayRefreshCount == 0 ? 0 : _overlayTotalMilliseconds / _overlayRefreshCount):0.###}; " +
            $"overlayMaxMs={_overlayMaximumMilliseconds:0.###}; questPatches={_questPatchCount}; " +
            $"patchAvgMs={(_questPatchCount == 0 ? 0 : _questPatchTotalMilliseconds / _questPatchCount):0.###}; " +
            $"patchMaxMs={_questPatchMaximumMilliseconds:0.###}; " +
            $"patchUiAvgMs={(_questPatchCount == 0 ? 0 : _questUiTotalMilliseconds / _questPatchCount):0.###}; " +
            $"patchUiMaxMs={_questUiMaximumMilliseconds:0.###}; broadReactiveEvents=False");
    }
}
