using System;
using HarmonyLib;
using EFT.UI;
using SPTQuestMap.Client.Compatibility;
using SPTQuestMap.Client.Configuration;

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
        Action<AbstractQuestControllerClass> observeQuestController)
    {
        if (!compatibility.IsCompatible)
        {
            return new PatchRegistrationResult(false, true, "incompatible environment");
        }

        if (configuration.AnyReplacementFeatureRequested)
        {
            return new PatchRegistrationResult(
                false,
                true,
                "replacement feature requested before its UI milestone is available");
        }

        _harmony = new Harmony(_harmonyId);
        QuestDataLifecyclePatch.Configure(observeQuestController);
        var postfix = new HarmonyMethod(typeof(QuestDataLifecyclePatch), nameof(QuestDataLifecyclePatch.Postfix));
        foreach (var target in compatibility.PatchTargets.ResolvedMethods)
        {
            if (target.Name != nameof(QuestsScreen.Show) || (target.DeclaringType != typeof(QuestsScreen) && target.DeclaringType != typeof(TasksScreen)))
            {
                continue;
            }

            _harmony.Patch(target, postfix: postfix);
            InstalledPatchCount++;
        }

        return new PatchRegistrationResult(true, false, "compatible read-only Milestone 2 data hooks active; replacement features disabled");
    }

    public void Dispose()
    {
        QuestDataLifecyclePatch.Configure(null);
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
