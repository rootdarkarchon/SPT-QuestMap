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
        log.LogInfo(
            "QUESTMAP_M01_STARTUP " +
            $"pluginVersion={pluginVersion}; " +
            $"supportedEFT={CompatibilityValidator.SupportedEftVersion}; " +
            $"supportedSPT={CompatibilityValidator.SupportedSptVersion}");

        log.LogInfo(
            "QUESTMAP_M01_ENVIRONMENT " +
            $"detectedEFT={compatibility.DetectedEftVersion}; " +
            $"privatePart={compatibility.DetectedEftPrivatePart}; " +
            $"detectedSPT={compatibility.DetectedSptVersion}; " +
            $"Assembly-CSharp={compatibility.AssemblyCSharpIdentity}; " +
            $"sha256={compatibility.AssemblyCSharpSha256}");

        var unresolved = compatibility.PatchTargets.UnresolvedTargets.Count == 0
            ? "none"
            : string.Join(" | ", compatibility.PatchTargets.UnresolvedTargets);
        log.LogInfo(
            "QUESTMAP_M01_TARGETS " +
            $"resolved={compatibility.PatchTargets.ResolvedMethods.Count}/{compatibility.PatchTargets.ExpectedCount}; " +
            $"unresolved={unresolved}; installedPatches={PatchRegistration.InstalledPatchCount}");

        log.LogInfo(
            "QUESTMAP_M01_CONFIG " +
            $"traderGraph={configuration.EnableTraderQuestGraph.Value}; " +
            $"globalGraph={configuration.EnableGlobalTasksGraph.Value}; " +
            $"customDetails={configuration.EnableCustomQuestDetails.Value}; " +
            $"debug={configuration.EnableDebugLogging.Value}; " +
            $"forceCompatibilityFailure={configuration.ForceCompatibilityFailure.Value}; " +
            $"forceTraderGraphInitializationFailure={configuration.ForceTraderGraphInitializationFailure.Value}");

        if (configuration.EnableDebugLogging.Value && compatibility.Failures.Count > 0)
        {
            log.LogDebug($"QUESTMAP_M01_FAILURES {string.Join(" | ", compatibility.Failures)}");
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
