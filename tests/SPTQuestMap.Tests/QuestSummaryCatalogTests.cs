using System.Text.Json;
using NUnit.Framework;
using SPTQuestMap.Services;

namespace SPTQuestMap.Tests;

public sealed class QuestSummaryCatalogTests
{
    [Test]
    public void EmbeddedEnglishCatalogContainsKnownQuestSummary()
    {
        var catalog = new QuestSummaryCatalog(externalDirectory: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.That(catalog.Get("5936d90786f7742b1420ba5b", "54cb50c76803fa8b248b4571", "en"), Does.Contain("kill five Scavs"));
    }

    [Test]
    public void TraderCatalogUsesLanguageThenDefaultThenEnglishPerQuest()
    {
        var directory = Directory.CreateTempSubdirectory("spt-questmap-summaries-");
        try
        {
            Write(directory, "trader.en.json", new Dictionary<string, string> { ["localized"] = "English", ["english-only"] = "English fallback" });
            Write(directory, "english-trader.en.json", new Dictionary<string, string> { ["english-only"] = "English fallback" });
            Write(directory, "trader.json", new Dictionary<string, string> { ["localized"] = "Default", ["default-only"] = "Default fallback" });
            Write(directory, "trader.fr.json", new Dictionary<string, string> { ["localized"] = "Français" });
            var embedded = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new Dictionary<string, string> { ["bundled-only"] = "Bundled fallback" },
            };
            var catalog = new QuestSummaryCatalog(directory.FullName, embedded);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Get("localized", "trader", "fr"), Is.EqualTo("Français"));
                Assert.That(catalog.Get("default-only", "trader", "fr"), Is.EqualTo("Default fallback"));
                Assert.That(catalog.Get("english-only", "trader", "fr"), Is.Null, "The .en file is not a second fallback when the default file exists.");
                Assert.That(catalog.Get("english-only", "english-trader", "fr"), Is.EqualTo("English fallback"));
                Assert.That(catalog.Get("bundled-only", "trader", "fr"), Is.EqualTo("Bundled fallback"));
                Assert.That(catalog.Get("missing", "trader", "fr"), Is.Null);
            });
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static void Write(DirectoryInfo directory, string name, IReadOnlyDictionary<string, string> values) =>
        File.WriteAllText(Path.Combine(directory.FullName, name), JsonSerializer.Serialize(values));
}
