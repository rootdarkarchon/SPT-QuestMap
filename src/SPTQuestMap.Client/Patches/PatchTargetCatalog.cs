using System.Collections.Generic;
using EFT.UI;
using SPTQuestMap.Client.Compatibility;

namespace SPTQuestMap.Client.Patches;

internal static class PatchTargetCatalog
{
    private static readonly string[] Names =
    {
        "EFT.UI.QuestsScreen.Show",
        "EFT.UI.QuestsScreen.Close",
        "EFT.UI.TasksScreen.Show",
        "EFT.UI.TasksScreen.Close"
    };

    public static IReadOnlyList<string> TargetNames => Names;

    public static PatchTargetResolution ResolveExactTargets()
    {
        var assembly = typeof(QuestsScreen).Assembly;
        var failures = NativeTargetValidation.Validate(assembly.GetType, NativeTargetContract.Requirements, out var patches);
        return new PatchTargetResolution(Names.Length, patches, failures);
    }
}