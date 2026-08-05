using System;
using HarmonyLib;
using EFT.UI;
using SPTQuestMap.Client.Compatibility;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Client.Data;

namespace SPTQuestMap.Client.Patches;

internal sealed class PatchRegistration : IDisposable
{
    public static int InstalledPatchCount { get; private set; }

    private readonly string _harmonyId;
    private Harmony? _harmony;

    public PatchRegistration(string harmonyId)
    {
        _harmonyId = harmonyId;
    }

    public PatchRegistrationResult Register(
        CompatibilityReport compatibility,
        QuestMapClientConfiguration configuration,
        QuestMapDataRuntime dataRuntime)
    {
        if (!compatibility.IsCompatible)
        {
            return new PatchRegistrationResult(false, true, "incompatible environment");
        }

        if (configuration.EnableGlobalTasksGraph.Value || configuration.EnableCustomQuestDetails.Value)
        {
            return new PatchRegistrationResult(
                false,
                true,
                "a post-Milestone-3 replacement feature was requested before its UI milestone is available");
        }

        _harmony = new Harmony(_harmonyId);
        QuestDataLifecyclePatch.Configure(dataRuntime.ObserveQuestController);
        TraderGraphLifecyclePatch.Configure(dataRuntime);
        foreach (var target in compatibility.PatchTargets.ResolvedMethods)
        {
            if (target.DeclaringType == typeof(QuestsScreen) && target.Name == nameof(QuestsScreen.Show))
            {
                _harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(TraderGraphLifecyclePatch), nameof(TraderGraphLifecyclePatch.ShowPostfix)));
                InstalledPatchCount++;
                continue;
            }

            if (target.DeclaringType == typeof(QuestsScreen) && target.Name == nameof(QuestsScreen.Close))
            {
                _harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(TraderGraphLifecyclePatch), nameof(TraderGraphLifecyclePatch.ClosePrefix)));
                InstalledPatchCount++;
                continue;
            }

            if (target.DeclaringType == typeof(TasksScreen) && target.Name == nameof(TasksScreen.Show))
            {
                _harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(QuestDataLifecyclePatch), nameof(QuestDataLifecyclePatch.Postfix)));
                InstalledPatchCount++;
            }
        }

        var mode = configuration.EnableTraderQuestGraph.Value
            ? "Milestone 3 trader graph enabled"
            : "Milestone 3 trader graph disabled; read-only data hooks active";
        return new PatchRegistrationResult(true, false, mode);
    }

    public void Dispose()
    {
        QuestDataLifecyclePatch.Configure(null);
        TraderGraphLifecyclePatch.Configure(null);
        _harmony?.UnpatchSelf();
        _harmony = null;
        InstalledPatchCount = 0;
    }
}

internal sealed class PatchRegistrationResult
{
    public PatchRegistrationResult(bool active, bool safelyDisabled, string reason)
    {
        Active = active;
        SafelyDisabled = safelyDisabled;
        Reason = reason;
    }

    public bool Active { get; }

    public bool SafelyDisabled { get; }

    public string Reason { get; }
}
