using System;
using System.Collections.Generic;

namespace SPTQuestMap.Client.Compatibility;

// Runtime admission is broader than the build used for validation, but never broader than its EFT ABI.
internal static class ClientBuildPolicy
{
    public const int EftBuild = 40743;
    public const string EftVersion = "0.16.9.40743";
    public const string SptRange = ">=4.1.6 <4.2.0";
    public const string ValidatedSptVersion = "4.1.6";
    public const string AssemblySha256 = "EE25CEE1259777B38ED8B3E7841FDC2DB3C98540B1469FA539B1FF183476E436";

    public static IReadOnlyList<string> Validate(int eftBuild, string sptVersion, string assemblyHash)
    {
        var failures = new List<string>();
        if (eftBuild != EftBuild) failures.Add($"EFT private build part {eftBuild} does not match {EftBuild}.");
        if (!Version.TryParse(sptVersion, out var version)
            || version.Major != 4 || version.Minor != 1 || version.Build < 6)
            failures.Add($"SPT client version {sptVersion} is outside {SptRange}.");
        if (!string.Equals(assemblyHash, AssemblySha256, StringComparison.OrdinalIgnoreCase))
            failures.Add("Assembly-CSharp.dll has an unknown ABI fingerprint; the native replacement remains disabled.");
        return failures;
    }
}
