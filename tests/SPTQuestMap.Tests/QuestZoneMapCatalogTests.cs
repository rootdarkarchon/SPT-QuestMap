using System.Text.Json;
using NUnit.Framework;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;
using SPTQuestMap.Presentation;
using SPTQuestMap.Services;

namespace SPTQuestMap.Tests;

public sealed class QuestZoneMapCatalogTests
{
    [Test]
    public void CatalogResolvesSharedZonesToEveryMapAndDeduplicatesEntries()
    {
        using var fixture = new CatalogFixture(new Dictionary<string, string[]>
        {
            ["bigmap"] = ["zone-customs", "zone-shared", "zone-shared"],
            ["Woods"] = ["zone-shared"],
        });
        var catalog = new QuestZoneMapCatalog(fixture.Path);

        var result = catalog.Resolve(["zone-shared", "zone-missing", "zone-shared"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.MapIds, Is.EqualTo(new[] { "Woods", "bigmap" }));
            Assert.That(result.UnresolvedZoneIds, Is.EqualTo(new[] { "zone-missing" }));
        });
    }

    [Test]
    public void ActualMapsIgnoreNonSpatialTasksButRemainAnyForUnknownSpatialZones()
    {
        var nativeAny = new QuestLocationDto("any", null, true, null);
        ObjectiveDefinitionDto mapped = Objective("mapped", ["zone-customs"]) with
        {
            MapIds = ["bigmap"],
        };
        var nonSpatial = Objective("handover", []);
        ObjectiveDefinitionDto unknown = Objective("unknown", ["zone-unknown"]) with
        {
            UnresolvedZoneIds = ["zone-unknown"],
        };

        var locations = new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>(StringComparer.Ordinal);
        var complete = QuestTemplateMapper.BuildActualMaps(nativeAny, [mapped, nonSpatial], [], locations);
        var incomplete = QuestTemplateMapper.BuildActualMaps(nativeAny, [mapped, unknown], [], locations);
        var nativeTransition = new QuestLocationDto("marathon", "Transition", false, null);
        var transition = QuestTemplateMapper.BuildActualMaps(nativeTransition, [mapped, nonSpatial], [], locations);

        Assert.Multiple(() =>
        {
            Assert.That(complete.Complete, Is.True);
            Assert.That(complete.Maps.Select(map => map.Id), Is.EqualTo(new[] { "bigmap" }));
            Assert.That(incomplete.Complete, Is.False);
            Assert.That(incomplete.Maps.Select(map => map.Id), Is.EqualTo(new[] { "bigmap" }));
            Assert.That(transition.Complete, Is.True,
                "Transition quests must expose the server-computed objective maps while retaining their native location.");
            Assert.That(transition.Maps.Select(map => map.Id), Is.EqualTo(new[] { "bigmap" }));
        });
    }

    [Test]
    public void WebPresentation_PutsTransitionBeforeDerivedMapsOnlyForMultiMapTransitionQuest()
    {
        var transition = new QuestNodeDto(
            "transition", "Transition quest", "", "trader", "Trader", null, "", "Any",
            new QuestLocationDto("marathon", "Transition", false, null), null, false,
            [], [], [], [], [])
        {
            ActualMapsComplete = true,
            ActualMaps =
            [
                new QuestMapReferenceDto("bigmap", "Customs", "/customs.jpg"),
                new QuestMapReferenceDto("Woods", "Woods", "/woods.jpg"),
            ],
        };
        var any = transition with
        {
            Id = "any",
            Location = new QuestLocationDto("any", null, true, null),
        };
        var singleMap = transition with
        {
            Id = "single",
            ActualMaps = [new QuestMapReferenceDto("bigmap", "Customs", "/customs.jpg")],
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                QuestMapLocationPresentation.DisplayMaps(transition, "Any").Select(map => map.Name),
                Is.EqualTo(new[] { "Transition", "Customs", "Woods" }));
            Assert.That(
                QuestMapLocationPresentation.DisplayMaps(any, "Any").Select(map => map.Name),
                Is.EqualTo(new[] { "Customs", "Woods" }));
            Assert.That(
                QuestMapLocationPresentation.DisplayMaps(singleMap, "Any").Select(map => map.Name),
                Is.EqualTo(new[] { "Customs" }));
        });
    }

    [Test]
    public void ShippedCatalogIsCopiedAndUsesCanonicalVariantMapIds()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "triggerIds.json");
        var source = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))!;

        Assert.Multiple(() =>
        {
            Assert.That(source.Keys, Does.Contain("factory4_day"));
            Assert.That(source.Keys, Does.Not.Contain("factory4_night"));
            Assert.That(source.Keys, Does.Contain("Sandbox"));
            Assert.That(source.Keys, Does.Not.Contain("Sandbox_high"));
            Assert.That(new QuestZoneMapCatalog(path).Resolve(["catwalk01"]).MapIds, Does.Contain("factory4_day"));
            Assert.That(QuestTemplateMapper.CanonicalizeVariantMapId("factory4_night"), Is.EqualTo("factory4_day"));
            Assert.That(QuestTemplateMapper.CanonicalizeVariantMapId("Sandbox_high"), Is.EqualTo("Sandbox"));
        });
    }

    [Test]
    public void MapAliasesBridgeQuestMongoIdsToCanonicalInternalMapIds()
    {
        var aliases = QuestTemplateMapper.BuildMapAliases(
        [
            Location("Woods", "5704e3c2d2720bac5b8b4567"),
            Location("factory4_day", "55f2d3fd4bdc2d5f408b4567"),
            Location("factory4_night", "59fc81d786f774390775787e"),
            Location("Sandbox", "653e6760052c01c1c805532f"),
            Location("Sandbox_high", "65e5a9d6e41e5f0d19b17a5f"),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(aliases["Woods"], Is.EquivalentTo(new[] { "Woods", "5704e3c2d2720bac5b8b4567" }));
            Assert.That(aliases["factory4_day"], Is.EquivalentTo(new[]
            {
                "factory4_day",
                "factory4_night",
                "55f2d3fd4bdc2d5f408b4567",
                "59fc81d786f774390775787e",
            }));
            Assert.That(aliases.Keys, Does.Not.Contain("factory4_night"));
            Assert.That(aliases["Sandbox"], Is.EquivalentTo(new[]
            {
                "Sandbox",
                "Sandbox_high",
                "653e6760052c01c1c805532f",
                "65e5a9d6e41e5f0d19b17a5f",
            }));
            Assert.That(aliases.Keys, Does.Not.Contain("Sandbox_high"));
        });
    }

    private static Location Location(string internalId, string mongoId) => new()
    {
        Base = new LocationBase { Id = internalId, IdField = new MongoId(mongoId) },
    };

    [Test]
    public void ExplicitLocationsAndForcedQuestItemSpawnsSupplementObjectiveMaps()
    {
        using var fixture = new CatalogFixture(new Dictionary<string, string[]>());
        var catalog = new QuestZoneMapCatalog(fixture.Path);
        var findQuestItem = Condition("000000000000000000000031", "FindItem") with
        {
            Target = new ListOrT<string>(null, "quest-item"),
        };
        var streets = Condition("000000000000000000000032", "CounterCreator") with
        {
            Counter = new QuestConditionCounter
            {
                Conditions =
                [
                    new QuestConditionCounterCondition
                    {
                        ConditionType = "Location",
                        Target = new ListOrT<string>(null, "TarkovStreets"),
                    },
                ],
            },
        };
        var source = new[] { findQuestItem, streets };
        var definitions = QuestTemplateMapper.OrderObjectives(source, []);
        var resolved = QuestTemplateMapper.ResolveObjectiveMaps(
            definitions,
            catalog,
            source.ToDictionary(condition => condition.Id.ToString(), StringComparer.Ordinal),
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["quest-item"] = ["bigmap"] },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TarkovStreets"] = "TarkovStreets" });

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Single(objective => objective.Id == findQuestItem.Id.ToString()).MapIds,
                Is.EqualTo(new[] { "bigmap" }));
            Assert.That(resolved.Single(objective => objective.Id == streets.Id.ToString()).MapIds,
                Is.EqualTo(new[] { "TarkovStreets" }));
            var actual = QuestTemplateMapper.BuildActualMaps(
                new QuestLocationDto("marathon", "Transition", false, null),
                resolved,
                [],
                new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>());
            Assert.That(actual.Complete, Is.True);
            Assert.That(actual.Maps.Select(map => map.Id), Is.EquivalentTo(new[] { "bigmap", "TarkovStreets" }));
        });
    }

    [Test]
    public void ForcedQuestItemSpawnMapsUseCanonicalVariantIds()
    {
        var questItemId = new MongoId("000000000000000000000041");
        var locations = new[]
        {
            ForcedSpawnLocation("factory4_night", questItemId, "000000000000000000000042"),
            ForcedSpawnLocation("Sandbox_high", questItemId, "000000000000000000000043"),
        };
        var items = new Dictionary<MongoId, TemplateItem>
        {
            [questItemId] = new TemplateItem
            {
                Id = questItemId,
                Properties = new TemplateItemProperties { QuestItem = true },
            },
        };

        var lookup = QuestTemplateMapper.BuildQuestItemSpawnMapLookup(locations, items);

        Assert.That(lookup[questItemId.ToString()], Is.EqualTo(new[] { "Sandbox", "factory4_day" }));
    }

    [Test]
    public void MissingCatalogFailsFast()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "triggerIds.json");
        Assert.Throws<FileNotFoundException>(() => new QuestZoneMapCatalog(path));
    }

    private static ObjectiveDefinitionDto Objective(string id, string[] zoneIds) => new(
        id, id, "CounterCreator", 0, null, 1, ">=", [], zoneIds);

    private static QuestCondition Condition(string id, string type) => new()
    {
        Id = new MongoId(id),
        DynamicLocale = false,
        ConditionType = type,
    };

    private static Location ForcedSpawnLocation(string mapId, MongoId itemTemplate, string itemId) => new()
    {
        Base = new LocationBase { Id = mapId },
        LooseLoot = new LazyLoad<LooseLoot>(() => new LooseLoot
        {
            SpawnpointsForced =
            [
                new Spawnpoint
                {
                    Template = new SpawnpointTemplate
                    {
                        Items = [new SptLootItem { Id = new MongoId(itemId), Template = itemTemplate }],
                    },
                },
            ],
        }),
    };

    private sealed class CatalogFixture : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("spt-questmap-zones-");

        public CatalogFixture(IReadOnlyDictionary<string, string[]> entries)
        {
            Path = System.IO.Path.Combine(_directory.FullName, "triggerIds.json");
            File.WriteAllText(Path, JsonSerializer.Serialize(entries));
        }

        public string Path { get; }

        public void Dispose() => _directory.Delete(true);
    }
}
