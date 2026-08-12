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

        Assert.That(catalog.Get("5936d90786f7742b1420ba5b", "en"), Does.Contain("kill five Scavs"));
    }

    [Test]
    public void CustomCatalogsApplyPerQuestLanguageOverridesAndLastFileWins()
    {
        var directory = Directory.CreateTempSubdirectory("spt-questmap-summaries-");
        try
        {
            Write(directory, "10-first.json", new Dictionary<string, string>
            {
                ["default"] = "First English",
                ["english-only"] = "English fallback",
            });
            Write(directory, "20-second.json", new Dictionary<string, string>
            {
                ["default"] = "Second English",
            });
            Write(directory, "30-local.ru.json", new Dictionary<string, string>
            {
                ["default"] = "Russian override",
                ["russian-only"] = "Russian fallback",
                ["bundled"] = "Custom Russian replacement",
            });
            var embedded = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new Dictionary<string, string>
                {
                    ["bundled"] = "Bundled English",
                    ["bundled-only"] = "Bundled fallback",
                },
            };
            var catalog = new QuestSummaryCatalog(directory.FullName, embedded);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Get("default", "en"), Is.EqualTo("Second English"), "Files without a language suffix are English and later filenames win.");
                Assert.That(catalog.Get("default", "ru"), Is.EqualTo("Russian override"));
                Assert.That(catalog.Get("default", "fr"), Is.EqualTo("Second English"), "Custom English is preferred when the requested custom language is absent.");
                Assert.That(catalog.Get("english-only", "fr"), Is.EqualTo("English fallback"));
                Assert.That(catalog.Get("russian-only", "en"), Is.EqualTo("Russian fallback"), "A sole non-English summary fills every other language.");
                Assert.That(catalog.Get("russian-only", "fr"), Is.EqualTo("Russian fallback"));
                Assert.That(catalog.Get("bundled", "fr"), Is.EqualTo("Custom Russian replacement"), "Any custom language may replace the bundled summary when it is the only custom value.");
                Assert.That(catalog.Get("bundled-only", "fr"), Is.EqualTo("Bundled fallback"));
                Assert.That(catalog.Get("missing", "en"), Is.Null);
            });
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public void MalformedCustomCatalogIsLoggedAndDoesNotBlockOtherFiles()
    {
        var directory = Directory.CreateTempSubdirectory("spt-questmap-summaries-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "10-broken.json"), "{");
            Write(directory, "20-valid.ge.json", new Dictionary<string, string> { ["quest"] = "Zusammenfassung" });
            var warnings = new List<string>();
            var catalog = new QuestSummaryCatalog(directory.FullName, new Dictionary<string, IReadOnlyDictionary<string, string>>(), warnings.Add);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Get("quest", "ge"), Is.EqualTo("Zusammenfassung"));
                Assert.That(warnings, Has.Count.EqualTo(1));
                Assert.That(warnings[0], Does.Contain("10-broken.json"));
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
