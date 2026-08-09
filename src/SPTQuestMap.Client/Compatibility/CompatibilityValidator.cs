using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using SPTQuestMap.Client.Patches;

namespace SPTQuestMap.Client.Compatibility;

internal static class CompatibilityValidator
{
    public const int SupportedEftPrivatePart = 40087;
    public const string SupportedEftVersion = "0.16.9.40087";
    public const string SupportedSptVersion = "4.0.13.0";
    public const string SupportedAssemblyCSharpSha256 =
        "FAEF6F0B9F142F9D047495EC3DCCFD5D6974AC048368DC7045955CF54B117982";

    public static CompatibilityReport Validate()
    {
        var failures = new List<string>();
        var detectedEftVersion = "unavailable";
        var detectedEftPrivatePart = -1;
        var detectedSptVersion = "unavailable";
        var assemblyIdentity = "unavailable";
        var assemblyHash = "unavailable";

        try
        {
            var executableVersion = FileVersionInfo.GetVersionInfo(BepInEx.Paths.ExecutablePath);
            detectedEftVersion = executableVersion.ProductVersion ?? executableVersion.FileVersion ?? "unknown";
            detectedEftPrivatePart = executableVersion.FilePrivatePart;

            if (detectedEftPrivatePart != SupportedEftPrivatePart)
            {
                failures.Add(
                    $"EFT private build part {detectedEftPrivatePart} does not match {SupportedEftPrivatePart}.");
            }

            var sptCorePath = Path.Combine(BepInEx.Paths.PluginPath, "spt", "spt-core.dll");
            detectedSptVersion = FileVersionInfo.GetVersionInfo(sptCorePath).FileVersion ?? "unknown";
            if (!string.Equals(detectedSptVersion, SupportedSptVersion, StringComparison.Ordinal))
            {
                failures.Add($"SPT client version {detectedSptVersion} does not match {SupportedSptVersion}.");
            }

            var assemblyCSharpPath = Path.Combine(BepInEx.Paths.ManagedPath, "Assembly-CSharp.dll");
            assemblyIdentity = AssemblyName.GetAssemblyName(assemblyCSharpPath).FullName;
            assemblyHash = ComputeSha256(assemblyCSharpPath);
            if (!string.Equals(
                    assemblyHash,
                    SupportedAssemblyCSharpSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Assembly-CSharp.dll does not match the supported EFT build hash.");
            }
        }
        catch (Exception exception)
        {
            failures.Add($"Environment inspection failed: {exception.GetType().Name}: {exception.Message}");
        }

        PatchTargetResolution targets;
        if (failures.Count == 0)
        {
            try
            {
                targets = PatchTargetCatalog.ResolveExactTargets();
            }
            catch (Exception exception)
            {
                failures.Add($"Patch target resolution failed: {exception.GetType().Name}: {exception.Message}");
                targets = PatchTargetResolution.Skipped(PatchTargetCatalog.TargetNames, "target resolution exception");
            }
        }
        else
        {
            targets = PatchTargetResolution.Skipped(PatchTargetCatalog.TargetNames, "pre-target compatibility failure");
        }

        if (!targets.AllResolved)
        {
            failures.Add("One or more exact patch targets could not be resolved.");
        }

        return new CompatibilityReport(
            detectedEftVersion,
            detectedEftPrivatePart,
            detectedSptVersion,
            assemblyIdentity,
            assemblyHash,
            failures,
            targets);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
    }
}
