using System;
using System.Linq;
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

        _harmony = new Harmony(_harmonyId);
        QuestDataLifecyclePatch.Configure(dataRuntime.ObserveQuestController);
        TraderGraphLifecyclePatch.Configure(dataRuntime);
        GlobalTasksLifecyclePatch.Configure(dataRuntime);
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
                    postfix: new HarmonyMethod(typeof(GlobalTasksLifecyclePatch), nameof(GlobalTasksLifecyclePatch.ShowPostfix)));
                InstalledPatchCount++;
                continue;
            }

            if (target.DeclaringType == typeof(TasksScreen) && target.Name == nameof(TasksScreen.Close))
            {
                _harmony.Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(GlobalTasksLifecyclePatch), nameof(GlobalTasksLifecyclePatch.ClosePrefix)));
                InstalledPatchCount++;
            }
        }

        var enabledFeatures = new[]
        {
            configuration.EnableTraderQuestGraph.Value ? "trader graph" : null,
            configuration.EnableGlobalTasksGraph.Value ? "global Tasks graph" : null,
        }.Where(value => value is not null);
        var mode = enabledFeatures.Any()
            ? $"enabled: {string.Join(", ", enabledFeatures)}"
            : "replacement graphs disabled; read-only data hooks active";
        return new PatchRegistrationResult(true, false, mode);
    }

    public void Dispose()
    {
        QuestDataLifecyclePatch.Configure(null);
        TraderGraphLifecyclePatch.Configure(null);
        GlobalTasksLifecyclePatch.Configure(null);
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
