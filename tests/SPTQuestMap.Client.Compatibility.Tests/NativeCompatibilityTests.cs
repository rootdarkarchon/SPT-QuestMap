using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using SPTQuestMap.Client.Compatibility;

namespace SPTQuestMap.Client.Compatibility.Tests;

public sealed class NativeCompatibilityTests
{
    private MetadataLoadContext _context = null!;
    private Assembly _eft = null!;
    private string _root = null!;

    [OneTimeSetUp]
    public void LoadMetadataOnly()
    {
        _root = typeof(NativeCompatibilityTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "EftInstallRoot").Value!;
        Assert.That(Directory.Exists(_root), Is.True, "Pass -p:EftInstallRoot=<Tarkov root>.");
        var managed = Path.Combine(_root, "EscapeFromTarkov_Data", "Managed");
        _context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(managed, "*.dll")), "mscorlib");
        _eft = _context.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
    }

    [OneTimeTearDown]
    public void UnloadMetadata() => _context?.Dispose();

    [Test]
    public void EveryNativeContractMatchesAndOnlyFourLifecycleMethodsArePatched()
    {
        var failures = NativeTargetValidation.Validate(_eft.GetType, NativeTargetContract.Requirements, out var patches);
        Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        Assert.That(patches.Select(p => $"{p.DeclaringType!.FullName}.{p.Name}"), Is.EquivalentTo(new[]
        {
            "EFT.UI.QuestsScreen.Show", "EFT.UI.QuestsScreen.Close",
            "EFT.UI.TasksScreen.Show", "EFT.UI.TasksScreen.Close"
        }));
    }

    [Test]
    public void ReferenceInstallationHasAdmittedIdentity()
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_eft.Location)));
        var eft = FileVersionInfo.GetVersionInfo(Path.Combine(_root, "EscapeFromTarkov.exe"));
        var spt = FileVersionInfo.GetVersionInfo(Path.Combine(_root, "BepInEx", "plugins", "spt", "spt-core.dll"));
        Assert.That(ClientBuildPolicy.Validate(eft.FilePrivatePart, spt.FileVersion!, hash), Is.Empty);
    }

    [TestCase("4.1.6", true)]
    [TestCase("4.1.6.0", true)]
    [TestCase("4.1.7", true)]
    [TestCase("4.1.99.0", true)]
    [TestCase("4.1.5", false)]
    [TestCase("4.0.13.0", false)]
    [TestCase("4.2.0", false)]
    [TestCase("5.1.6", false)]
    [TestCase("4.1.6-preview", false)]
    [TestCase("invalid", false)]
    public void VersionRangeRetainsTheEftAbiGate(string version, bool accepted) =>
        Assert.That(ClientBuildPolicy.Validate(40743, version, ClientBuildPolicy.AssemblySha256).Count == 0, Is.EqualTo(accepted));

    [TestCase(40087, false)]
    [TestCase(40743, true)]
    [TestCase(40744, false)]
    public void EftBuildMustMatch(int build, bool accepted) =>
        Assert.That(ClientBuildPolicy.Validate(build, "4.1.6", ClientBuildPolicy.AssemblySha256).Count == 0, Is.EqualTo(accepted));

    [Test]
    public void UnknownFingerprintFailsEvenWithMatchingVersion() =>
        Assert.That(ClientBuildPolicy.Validate(40743, "4.1.6", new string('0', 64)), Is.Not.Empty);

    [TestCase("M", "StartQuest")]
    [TestCase("F", "_favoriteQuests")]
    [TestCase("P", "CurrentValue")]
    [TestCase("E", "OnReset")]
    public void MissingRequiredMemberFailsClosed(string kind, string name)
    {
        var contract = NativeTargetContract.Requirements.First(r => r.StartsWith(kind + "|") && r.Split('|')[2] == name);
        var missing = contract.Replace("|" + name + "|", "|MissingMember|");
        Assert.That(NativeTargetValidation.Validate(_eft.GetType, [missing], out _), Has.Count.EqualTo(1));
    }

    [Test]
    public void SameNameWithChangedSignatureFailsClosed()
    {
        var show = NativeTargetContract.Requirements.First(r => r.StartsWith("M|EFT.UI.QuestsScreen|Show|"));
        var changed = show.Replace("EFT.IEftSession;", "System.String;");
        Assert.That(NativeTargetValidation.Validate(_eft.GetType, [changed], out var patches), Has.Count.EqualTo(1));
        Assert.That(patches, Is.Empty);
    }

    [Test]
    public void MissingNativeTypeFailsClosed() =>
        Assert.That(NativeTargetValidation.Validate(_ => null, NativeTargetContract.Requirements, out _),
            Has.Count.EqualTo(NativeTargetContract.Requirements.Length));
}
