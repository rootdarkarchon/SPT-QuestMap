using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace SPTQuestMap;

public sealed record QuestMapModMetadata : AbstractModMetadata, IModWebMetadata
{
    public override string ModGuid { get; init; } = "com.sptquestmap.server";
    public override string Name { get; init; } = "SPT-QuestMap";
    public override string Author { get; init; } = "SPT-QuestMap contributors";
    public override List<string>? Contributors { get; init; }
    public override Version Version { get; init; } = new(GetModVersion());
    public override Range SptVersion { get; init; } = new("4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";

    private static string GetModVersion()
    {
        var informationalVersion = typeof(QuestMapModMetadata).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            throw new InvalidOperationException("SPTQuestMap assembly informational version is missing.");
        }

        return informationalVersion.Split('+', 2)[0];
    }
}
