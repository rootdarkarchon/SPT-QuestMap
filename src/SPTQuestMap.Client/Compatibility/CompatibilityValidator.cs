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
    public const int SupportedEftPrivatePart = ClientBuildPolicy.EftBuild;
    public const string SupportedEftVersion = ClientBuildPolicy.EftVersion;
    public const string SupportedSptVersion = ClientBuildPolicy.SptRange;
    public const string SupportedAssemblyCSharpSha256 = ClientBuildPolicy.AssemblySha256;

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

            var sptCorePath = Path.Combine(BepInEx.Paths.PluginPath, "spt", "spt-core.dll");
            detectedSptVersion = FileVersionInfo.GetVersionInfo(sptCorePath).FileVersion ?? "unknown";
            var assemblyCSharpPath = Path.Combine(BepInEx.Paths.ManagedPath, "Assembly-CSharp.dll");
            assemblyIdentity = AssemblyName.GetAssemblyName(assemblyCSharpPath).FullName;
            assemblyHash = ComputeSha256(assemblyCSharpPath);
            failures.AddRange(ClientBuildPolicy.Validate(detectedEftPrivatePart, detectedSptVersion, assemblyHash));
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
            failures.AddRange(targets.UnresolvedTargets);
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
