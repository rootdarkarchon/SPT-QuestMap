using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SPTQuestMap.Client.Patches;

internal sealed class PatchTargetResolution
{
    public PatchTargetResolution(
        int expectedCount,
        IReadOnlyList<MethodInfo> resolvedMethods,
        IReadOnlyList<string> unresolvedTargets)
    {
        ExpectedCount = expectedCount;
        ResolvedMethods = resolvedMethods;
        UnresolvedTargets = unresolvedTargets;
    }

    public int ExpectedCount { get; }

    public IReadOnlyList<MethodInfo> ResolvedMethods { get; }

    public IReadOnlyList<string> UnresolvedTargets { get; }

    public bool AllResolved => ResolvedMethods.Count == ExpectedCount && UnresolvedTargets.Count == 0;

    public static PatchTargetResolution Skipped(IEnumerable<string> targetNames, string reason)
    {
        var unresolved = targetNames.Select(name => $"{name} (not evaluated: {reason})").ToArray();
        return new PatchTargetResolution(unresolved.Length, new MethodInfo[0], unresolved);
    }
}
