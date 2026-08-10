using System.Text.Json;
using NUnit.Framework;
using SPTQuestMap.Services;

namespace SPTQuestMap.Tests;

public sealed class QuestMetaInfoCatalogTests
{
    [Test]
    public void ResolvesLocalizedNamesAndFleaEligibilityOnceIntoQuestMetadata()
    {
        using var fixture = new CatalogFixture(new Dictionary<string, object>
        {
            ["quest"] = new
            {
                wiki = "https://example.test/wiki/quest",
                relevantItems = new[] { "eligible", "blocked" },
            },
        });
        var resolutions = 0;
        var catalog = new QuestMetaInfoCatalog(
            fixture.Path,
            itemId =>
            {
                resolutions++;
                return new QuestMetaInfoCatalog.CachedRelevantItem(
                    itemId,
                    $"fallback-{itemId}",
                    itemId == "eligible");
            });

        var first = catalog.Get("quest", new Dictionary<string, string>
        {
            ["eligible Name"] = "Localized eligible",
            ["blocked Name"] = "Localized blocked",
        });
        var second = catalog.Get("quest", new Dictionary<string, string>());

        Assert.Multiple(() =>
        {
            Assert.That(resolutions, Is.EqualTo(2), "Item IDs must be resolved only while the static catalog is loaded.");
            Assert.That(first?.WikiUrl, Is.EqualTo("https://example.test/wiki/quest"));
            Assert.That(first?.RelevantItems.Select(item => item.Name), Is.EqualTo(new[] { "Localized eligible", "Localized blocked" }));
            Assert.That(first?.RelevantItems.Select(item => item.FleaEligible), Is.EqualTo(new[] { true, false }));
            Assert.That(second?.RelevantItems.Select(item => item.Name), Is.EqualTo(new[] { "fallback-eligible", "fallback-blocked" }));
        });
    }

    [Test]
    public void UnresolvedItemIsWarnedAndOmittedFromCachedEntry()
    {
        using var fixture = new CatalogFixture(new Dictionary<string, object>
        {
            ["quest"] = new
            {
                wiki = "https://example.test/wiki/quest",
                relevantItems = new[] { "known", "missing" },
            },
        });
        var warnings = new List<string>();
        var catalog = new QuestMetaInfoCatalog(
            fixture.Path,
            itemId => itemId == "known"
                ? new QuestMetaInfoCatalog.CachedRelevantItem(itemId, "Known item", true)
                : null,
            warnings.Add);

        var entry = catalog.Get("quest", new Dictionary<string, string>());

        Assert.Multiple(() =>
        {
            Assert.That(entry?.RelevantItems.Select(item => item.TemplateId), Is.EqualTo(new[] { "known" }));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("missing").And.Contain("quest").And.Contain("omitted"));
        });
    }

    [Test]
    public void MissingAuthoritativeCatalogFailsFast()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "metainfo.json");

        Assert.Throws<FileNotFoundException>(() => new QuestMetaInfoCatalog(
            path,
            _ => null));
    }

    [Test]
    public void FileCanLoadBeforeDatabaseAndResolveExactlyOnceAfterward()
    {
        using var fixture = new CatalogFixture(new Dictionary<string, object>
        {
            ["quest"] = new
            {
                wiki = "https://example.test/wiki/quest",
                relevantItems = new[] { "item" },
            },
        });
        var catalog = new QuestMetaInfoCatalog(fixture.Path);
        var resolutions = 0;

        Assert.Throws<InvalidOperationException>(() => catalog.Get("quest", new Dictionary<string, string>()));
        catalog.Resolve(itemId =>
        {
            resolutions++;
            return new QuestMetaInfoCatalog.CachedRelevantItem(itemId, "Item", true);
        });
        catalog.Resolve(_ => throw new AssertionException("The resolved startup cache must not run twice."));

        Assert.Multiple(() =>
        {
            Assert.That(resolutions, Is.EqualTo(1));
            Assert.That(catalog.Get("quest", new Dictionary<string, string>())?.RelevantItems, Has.Length.EqualTo(1));
        });
    }

    private sealed class CatalogFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("spt-questmap-metainfo-");

        public CatalogFixture(IReadOnlyDictionary<string, object> entries)
        {
            Path = System.IO.Path.Combine(_directory.FullName, "metainfo.json");
            File.WriteAllText(Path, JsonSerializer.Serialize(entries));
        }

        public string Path { get; }

        public void Dispose() => _directory.Delete(true);
    }
}
