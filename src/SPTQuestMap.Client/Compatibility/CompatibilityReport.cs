using System.Collections.Generic;
using SPTQuestMap.Client.Patches;

namespace SPTQuestMap.Client.Compatibility;

internal sealed class CompatibilityReport
{
    public CompatibilityReport(
        string detectedEftVersion,
        int detectedEftPrivatePart,
        string detectedSptVersion,
        string assemblyCSharpIdentity,
        string assemblyCSharpSha256,
        IReadOnlyList<string> failures,
        PatchTargetResolution patchTargets)
    {
        DetectedEftVersion = detectedEftVersion;
        DetectedEftPrivatePart = detectedEftPrivatePart;
        DetectedSptVersion = detectedSptVersion;
        AssemblyCSharpIdentity = assemblyCSharpIdentity;
        AssemblyCSharpSha256 = assemblyCSharpSha256;
        Failures = failures;
        PatchTargets = patchTargets;
    }

    public string DetectedEftVersion { get; }

    public int DetectedEftPrivatePart { get; }

    public string DetectedSptVersion { get; }

    public string AssemblyCSharpIdentity { get; }

    public string AssemblyCSharpSha256 { get; }

    public IReadOnlyList<string> Failures { get; }

    public PatchTargetResolution PatchTargets { get; }

    public bool IsCompatible => Failures.Count == 0 && PatchTargets.AllResolved;
}
