using System.Reflection;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Web;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace SPTQuestMap;

public sealed record QuestMapModMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.sptquestmap.server";
    public string Name { get; init; } = "SPT-QuestMap";
    public string Author { get; init; } = "SPT-QuestMap contributors";
    public List<string>? Contributors { get; init; }
    public Version Version { get; init; } = new(GetModVersion());
    public Range SptVersion { get; init; } = new(">=4.1.6 <4.2.0");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public bool HasPrepatcher { get; init; } = false;
    public string? WWWRootUrl { get; init; }
    public string? HomePage { get; init; } = "/questmap";
    public string? HomePageDescription { get; init; } = "Read-only quest planning and profile comparison.";
    public string License { get; init; } = "MIT";

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
