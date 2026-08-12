using System;
using System.Linq;
using BepInEx.Logging;
using SPTQuestMap.Client.Compatibility;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.Patches;

namespace SPTQuestMap.Client.Diagnostics;

internal static class StartupDiagnostics
{
    public static void Log(
        ManualLogSource log,
        string pluginVersion,
        QuestMapClientConfiguration configuration,
        CompatibilityReport compatibility,
        PatchRegistrationResult registration)
    {
        QuestMapDebugLog.Info(log,
            "QUESTMAP_M01_STARTUP " +
            $"pluginVersion={pluginVersion}; " +
            $"supportedEFT={CompatibilityValidator.SupportedEftVersion}; " +
            $"supportedSPT={CompatibilityValidator.SupportedSptVersion}");

        QuestMapDebugLog.Info(log,
            "QUESTMAP_M01_ENVIRONMENT " +
            $"detectedEFT={compatibility.DetectedEftVersion}; " +
            $"privatePart={compatibility.DetectedEftPrivatePart}; " +
            $"detectedSPT={compatibility.DetectedSptVersion}; " +
            $"Assembly-CSharp={compatibility.AssemblyCSharpIdentity}; " +
            $"sha256={compatibility.AssemblyCSharpSha256}");

        var unresolved = compatibility.PatchTargets.UnresolvedTargets.Count == 0
            ? "none"
            : string.Join(" | ", compatibility.PatchTargets.UnresolvedTargets);
        QuestMapDebugLog.Info(log,
            "QUESTMAP_M01_TARGETS " +
            $"resolved={compatibility.PatchTargets.ResolvedMethods.Count}/{compatibility.PatchTargets.ExpectedCount}; " +
            $"unresolved={unresolved}; installedPatches={PatchRegistration.InstalledPatchCount}");

        QuestMapDebugLog.Info(log,
            "QUESTMAP_M01_CONFIG " +
            $"traderGraph={configuration.EnableTraderQuestGraph.Value}; " +
            $"globalGraph={configuration.EnableGlobalTasksGraph.Value}; " +
            "customDetails=required-with-replacement; " +
            $"defaultDetailsSummary={configuration.DefaultQuestDetailsToSummary.Value}; " +
            $"taskSkipping={configuration.EnableTaskSkipping.Value}; " +
            $"taskSkipModifier={configuration.TaskSkipModifier.Value}; " +
            $"autoTrackNew={configuration.AutoTrackNewQuests.Value}; " +
            $"trackFavorites={configuration.TrackFavoriteQuests.Value}; " +
            $"autoTrackMap={configuration.AutoTrackMapRelatedQuests.Value}; " +
            $"smartInRaidTracking={configuration.SmartInRaidTracking.Value}; " +
            $"raidNotificationBackgroundOpacity={configuration.RaidNotificationOpacity.Value:0.##}; " +
            $"raidNotificationMinimal={configuration.RaidNotificationMinimal.Value}; " +
            $"raidOverlayFadeSeconds={configuration.RaidOverlayFadeDurationSeconds.Value:0.##}; " +
            $"raidNotificationDisplaySeconds={configuration.RaidNotificationDisplayDurationSeconds.Value:0.##}; " +
            $"trackedQuestListDisplaySeconds={configuration.TrackedQuestListDisplayDurationSeconds.Value:0.##}; " +
            $"trackedQuestListHotkey={configuration.TrackedQuestListHotkey.Value}; " +
            $"debug={configuration.EnableDebugLogging.Value}");

        if (configuration.EnableDebugLogging.Value && compatibility.Failures.Count > 0)
        {
            log.LogInfo($"QUESTMAP_M01_FAILURES {string.Join(" | ", compatibility.Failures)}");
        }

        var stateMessage =
            "QUESTMAP_M01_STATE " +
            $"compatible={compatibility.IsCompatible}; active={registration.Active}; " +
            $"safelyDisabled={registration.SafelyDisabled}; reason={registration.Reason}";

        if (compatibility.IsCompatible && !registration.SafelyDisabled)
        {
            log.LogInfo(stateMessage);
        }
        else
        {
            var failures = compatibility.Failures.Count == 0
                ? string.Empty
                : $"; failures={string.Join(" | ", compatibility.Failures.Select(value => value.Trim()))}";
            log.LogWarning(stateMessage + failures);
        }
    }
}
