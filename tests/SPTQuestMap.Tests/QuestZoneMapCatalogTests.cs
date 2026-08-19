using System.Text.Json;
using NUnit.Framework;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;
using SPTQuestMap.Presentation;
using SPTQuestMap.Core.Rules;
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
    public void NativeLocationDisambiguatesOnlySharedZoneIds()
    {
        using var fixture = new CatalogFixture(new Dictionary<string, string[]>
        {
            ["bigmap"] = ["exit777"],
            ["Labyrinth"] = ["exit777"],
            ["Shoreline"] = ["shoreline-zone"],
        });
        var catalog = new QuestZoneMapCatalog(fixture.Path);

        var customs = catalog.Resolve(["exit777", "shoreline-zone"], "bigmap");
        var labyrinth = catalog.Resolve(["exit777"], "Labyrinth");
        var any = catalog.Resolve(["exit777"]);

        Assert.Multiple(() =>
        {
            Assert.That(customs.MapIds, Is.EqualTo(new[] { "Shoreline", "bigmap" }),
                "A distinct zone on the same objective must retain its own map.");
            Assert.That(labyrinth.MapIds, Is.EqualTo(new[] { "Labyrinth" }));
            Assert.That(any.MapIds, Is.EqualTo(new[] { "Labyrinth", "bigmap" }),
                "Any/Transition quests have no preferred map and must retain every candidate.");
        });
    }

    [Test]
    public void WorkSmarterSharedExitUsesItsNativeCustomsContext()
    {
        const string customsMongoId = "56f40101d2720b2a4d8b45d6";
        using var fixture = new CatalogFixture(new Dictionary<string, string[]>
        {
            ["bigmap"] = ["exit777"],
            ["Labyrinth"] = ["exit777"],
        });
        var locationsById = QuestTemplateMapper.BuildLocationLookup(
        [
            Location("bigmap", customsMongoId, "Customs"),
            Location("Labyrinth", "6733700029c367a3d40b02af", "The Labyrinth"),
        ]);
        var nativeLocation = QuestTemplateMapper.BuildLocation(customsMongoId, [], locationsById);
        var canonicalMapIdsByAlias = QuestTemplateMapper.BuildCanonicalMapIdLookup(locationsById);
        var resolved = QuestTemplateMapper.ResolveObjectiveMaps(
            [Objective("work-smarter-exit", ["exit777"]) with { ConditionType = "CounterCreator" }],
            new QuestZoneMapCatalog(fixture.Path),
            canonicalMapIdsByAlias: canonicalMapIdsByAlias,
            preferredZoneMapId: QuestTemplateMapper.ResolvePreferredZoneMapId(
                nativeLocation, canonicalMapIdsByAlias));
        var classified = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            nativeLocation,
            resolved,
            new Dictionary<string, string>(),
            [],
            locationsById);
        var actualMaps = QuestTemplateMapper.BuildActualMaps(classified);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Single().MapIds, Is.EqualTo(new[] { "bigmap" }));
            Assert.That(classified.Single().TaskLocations.Select(map => map.Id), Is.EqualTo(new[] { "bigmap" }));
            Assert.That(actualMaps.Maps.Select(map => map.Id), Is.EqualTo(new[] { "bigmap" }));
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
        var nonSpatial = Objective("handover", []) with { ConditionType = "HandoverItem" };
        ObjectiveDefinitionDto unknown = Objective("unknown", ["zone-unknown"]) with
        {
            UnresolvedZoneIds = ["zone-unknown"],
        };

        var locations = new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>(StringComparer.Ordinal);
        var ui = new Dictionary<string, string>();
        var completeObjectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            nativeAny, [mapped, nonSpatial], ui, [], locations);
        var incompleteObjectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            nativeAny, [mapped, unknown], ui, [], locations);
        var complete = QuestTemplateMapper.BuildActualMaps(completeObjectives);
        var incomplete = QuestTemplateMapper.BuildActualMaps(incompleteObjectives);
        var nativeTransition = new QuestLocationDto("marathon", "Transition", false, null);
        var transitionObjectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            nativeTransition, [mapped, nonSpatial], ui, [], locations);
        var transition = QuestTemplateMapper.BuildActualMaps(transitionObjectives);

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
    public void WebPresentation_PutsTransitionBeforeEveryDerivedMapSet()
    {
        var transition = new QuestNodeDto(
            "transition", "Transition quest", "", "trader", "Trader", null, "", "Any",
            new QuestLocationDto("marathon", "Transition", false, null), null, false,
            [], [], [], [], [])
        {
            TaskLocation = new QuestMapReferenceDto(
                QuestObjectiveMapRules.TransitionFilterId,
                "Transition",
                QuestObjectiveMapRules.TransitionBannerUrl),
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
            TaskLocation = new QuestMapReferenceDto(
                QuestObjectiveMapRules.AnyFilterId,
                "Any location",
                QuestObjectiveMapRules.AnyBannerUrl),
        };
        var singleMap = transition with
        {
            Id = "single",
            ActualMaps = [new QuestMapReferenceDto("bigmap", "Customs", "/customs.jpg")],
        };

        Assert.Multiple(() =>
        {
            Assert.That(
                QuestMapLocationPresentation.DisplayMaps(transition).Select(map => map.Name),
                Is.EqualTo(new[] { "Transition", "Customs", "Woods" }));
            Assert.That(
                QuestMapLocationPresentation.DisplayMaps(any).Select(map => map.Name),
                Is.EqualTo(new[] { "Customs", "Woods" }));
            Assert.That(
                QuestMapLocationPresentation.DisplayMaps(singleMap).Select(map => map.Name),
                Is.EqualTo(new[] { "Transition", "Customs" }));
        });
    }

    [Test]
    public void ServerAssignsAuthoritativeTaskLocationScopeAndUniqueBanners()
    {
        var ui = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["location.none"] = "Out of Raid",
            ["location.any"] = "Any location",
        };
        var passive = Objective("passive", []) with { ConditionType = "HandoverItem" };
        var inRaid = Objective("visit", []) with { ConditionType = "VisitPlace" };
        var mapped = inRaid with { Id = "mapped", MapIds = ["bigmap"] };

        var locations = new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>();
        var noLocationObjectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            new QuestLocationDto("any", null, true, null), [passive], ui, [], locations);
        var anyObjectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            new QuestLocationDto("any", null, true, null), [inRaid], ui, [], locations);
        var transitionObjectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            new QuestLocationDto("marathon", "Transition", false, "/old.jpg"), [mapped], ui, [], locations);
        var customsObjectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            new QuestLocationDto("bigmap", "Customs", false, "/customs.jpg"), [mapped], ui, [], locations);

        var noLocation = QuestTemplateMapper.BuildTaskLocation(
            new QuestLocationDto("any", null, true, null), noLocationObjectives, ui);
        var any = QuestTemplateMapper.BuildTaskLocation(
            new QuestLocationDto("any", null, true, null), anyObjectives, ui);
        var transition = QuestTemplateMapper.BuildTaskLocation(
            new QuestLocationDto("marathon", "Transition", false, "/old.jpg"), transitionObjectives, ui);
        var customs = QuestTemplateMapper.BuildTaskLocation(
            new QuestLocationDto("bigmap", "Customs", false, "/customs.jpg"), customsObjectives, ui);

        Assert.Multiple(() =>
        {
            Assert.That(noLocation, Is.EqualTo(new QuestMapReferenceDto(
                QuestObjectiveMapRules.NoLocationFilterId, "Out of Raid", QuestObjectiveMapRules.NoLocationBannerUrl)));
            Assert.That(any, Is.EqualTo(new QuestMapReferenceDto(
                QuestObjectiveMapRules.AnyFilterId, "Any location", QuestObjectiveMapRules.AnyBannerUrl)));
            Assert.That(transition, Is.EqualTo(new QuestMapReferenceDto(
                QuestObjectiveMapRules.TransitionFilterId, "Transition", QuestObjectiveMapRules.TransitionBannerUrl)));
            Assert.That(customs.Id, Is.EqualTo("bigmap"));
        });
    }

    [Test]
    public void MixedObjectiveScopes_DriveQuestLocationPrecedenceAndRemainExactPerTask()
    {
        var ui = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["location.none"] = "Out of Raid",
            ["location.any"] = "Any location",
        };
        var nativeAny = new QuestLocationDto("any", null, true, null);
        var locations = new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>();
        var objectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            nativeAny,
            [
                Objective("customs", []) with { ConditionType = "VisitPlace", MapIds = ["bigmap"] },
                Objective("any", []) with { ConditionType = "FindItem" },
                Objective("passive", []) with { ConditionType = "HandoverItem" },
            ],
            ui,
            [],
            locations);
        var actualMaps = QuestTemplateMapper.BuildActualMaps(objectives);
        var taskLocation = QuestTemplateMapper.BuildTaskLocation(nativeAny, objectives, ui);

        Assert.Multiple(() =>
        {
            Assert.That(objectives.Single(objective => objective.Id == "customs").TaskLocations.Select(map => map.Id),
                Is.EqualTo(new[] { "bigmap" }));
            Assert.That(objectives.Single(objective => objective.Id == "any").TaskLocations.Select(map => map.Id),
                Is.EqualTo(new[] { QuestObjectiveMapRules.AnyFilterId }));
            Assert.That(objectives.Single(objective => objective.Id == "passive").TaskLocations.Select(map => map.Id),
                Is.EqualTo(new[] { QuestObjectiveMapRules.NoLocationFilterId }));
            Assert.That(objectives.Single(objective => objective.Id == "passive").InRaidRelevant, Is.False);
            Assert.That(taskLocation.Id, Is.EqualTo("bigmap"), "Concrete task maps beat Any and Out of Raid.");
            Assert.That(actualMaps.Maps.Select(map => map.Id), Is.EqualTo(new[] { "bigmap" }));
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

    [Test]
    public void NativeMongoAndDerivedInternalIdsCollapseToOneActualMap()
    {
        const string customsMongoId = "56f40101d2720b2a4d8b45d6";
        var customs = Location("bigmap", customsMongoId, "Customs");
        var shoreline = Location("Shoreline", "5704e554d2720bac5b8b456e", "Shoreline");
        var locationsById = QuestTemplateMapper.BuildLocationLookup([customs, shoreline]);
        var nativeLocation = QuestTemplateMapper.BuildLocation(customsMongoId, [], locationsById);
        var objectives = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            nativeLocation,
            [
                Objective("native-fallback", []) with { ConditionType = "FindItem" },
                Objective("derived", []) with
                {
                    ConditionType = "FindItem",
                    MapIds = ["bigmap", customsMongoId, "Shoreline"],
                },
            ],
            new Dictionary<string, string>(),
            [],
            locationsById);
        var actualMaps = QuestTemplateMapper.BuildActualMaps(objectives);

        Assert.Multiple(() =>
        {
            Assert.That(
                objectives.Single(objective => objective.Id == "native-fallback").TaskLocations.Select(map => map.Id),
                Is.EqualTo(new[] { "bigmap" }));
            Assert.That(
                objectives.Single(objective => objective.Id == "derived").TaskLocations.Select(map => map.Id),
                Is.EqualTo(new[] { "bigmap", "Shoreline" }));
            Assert.That(actualMaps.Maps.Select(map => map.Id), Is.EqualTo(new[] { "bigmap", "Shoreline" }));
            Assert.That(actualMaps.Maps.Select(map => map.Name), Is.EqualTo(new[] { "Customs", "Shoreline" }));
        });
    }

    [Test]
    public void SafeCorridorLocationConditionExcludesArenaButKeepsReserve()
    {
        const string reserveMongoId = "5704e5fad2720bc05b8b4567";
        const string arenaMongoId = "56db0b3bd2720bb0678b4567";
        var reserve = Location("RezervBase", reserveMongoId, "Reserve");
        var arena = Location("develop", arenaMongoId, "Arena");
        var locations = new[] { reserve, arena };
        var locationsById = QuestTemplateMapper.BuildLocationLookup(locations);
        var canonicalMapIdsByAlias = QuestTemplateMapper.BuildCanonicalMapIdLookup(locationsById);
        var aliases = QuestTemplateMapper.BuildMapAliases(locations);
        var safeCorridor = Condition("000000000000000000000051", "CounterCreator") with
        {
            Counter = new QuestConditionCounter
            {
                Conditions =
                [
                    new QuestConditionCounterCondition
                    {
                        ConditionType = "Location",
                        Target = new ListOrT<string>(["RezervBase", "develop"], null),
                    },
                ],
            },
        };
        using var fixture = new CatalogFixture(new Dictionary<string, string[]>());
        var resolved = QuestTemplateMapper.ResolveObjectiveMaps(
            QuestTemplateMapper.OrderObjectives([safeCorridor], []),
            new QuestZoneMapCatalog(fixture.Path),
            new Dictionary<string, QuestCondition>(StringComparer.Ordinal)
            {
                [safeCorridor.Id.ToString()] = safeCorridor,
            },
            new Dictionary<MongoId, string[]>(),
            canonicalMapIdsByAlias);
        var classified = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            QuestTemplateMapper.BuildLocation(reserveMongoId, [], locationsById),
            resolved,
            new Dictionary<string, string>(),
            [],
            locationsById);
        var taskLocation = QuestTemplateMapper.BuildTaskLocation(
            QuestTemplateMapper.BuildLocation(reserveMongoId, [], locationsById),
            classified,
            new Dictionary<string, string>());

        Assert.Multiple(() =>
        {
            Assert.That(canonicalMapIdsByAlias.ContainsKey("RezervBase"), Is.True);
            Assert.That(canonicalMapIdsByAlias.ContainsKey(reserveMongoId), Is.True);
            Assert.That(canonicalMapIdsByAlias.ContainsKey("develop"), Is.False);
            Assert.That(canonicalMapIdsByAlias.ContainsKey(arenaMongoId), Is.False);
            Assert.That(aliases.Keys, Does.Contain("RezervBase"));
            Assert.That(aliases.Keys, Does.Not.Contain("develop"));
            Assert.That(resolved.Single().MapIds, Is.EqualTo(new[] { "RezervBase" }));
            Assert.That(classified.Single().InRaidRelevant, Is.True);
            Assert.That(classified.Single().TaskLocations.Select(map => map.Id),
                Is.EqualTo(new[] { "RezervBase" }));
            Assert.That(taskLocation.Id, Is.EqualTo("RezervBase"));
        });
    }

    [Test]
    public void ArenaOnlyObjectiveDoesNotBecomeAnOutOfRaidFilterScope()
    {
        var arena = Location("develop", "56db0b3bd2720bb0678b4567", "Arena");
        var locationsById = QuestTemplateMapper.BuildLocationLookup([arena]);
        var objective = Objective("arena", []) with
        {
            ConditionType = "Location",
            MapIds = ["develop"],
        };

        var classified = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
            QuestTemplateMapper.BuildLocation("develop", [], locationsById),
            [objective],
            new Dictionary<string, string> { ["location.none"] = "Out of Raid" },
            [],
            locationsById);

        Assert.Multiple(() =>
        {
            Assert.That(classified.Single().InRaidRelevant, Is.False);
            Assert.That(classified.Single().TaskLocations, Is.Empty);
            Assert.That(QuestTemplateMapper.BuildActualMaps(classified).Maps, Is.Empty);
        });
    }

    private static Location Location(string internalId, string mongoId, string? name = null) => new()
    {
        Base = new LocationBase { Id = internalId, IdField = new MongoId(mongoId), Name = name },
    };

    [Test]
    public void ExplicitLocationsAndForcedQuestItemSpawnsSupplementObjectiveMaps()
    {
        using var fixture = new CatalogFixture(new Dictionary<string, string[]>());
        var catalog = new QuestZoneMapCatalog(fixture.Path);
        var questItemId = new MongoId("000000000000000000000030");
        var findQuestItem = Condition("000000000000000000000031", "FindItem") with
        {
            Target = new ListOrT<string>(null, questItemId.ToString()),
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
            new Dictionary<MongoId, string[]> { [questItemId] = ["bigmap"] },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TarkovStreets"] = "TarkovStreets" });

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Single(objective => objective.Id == findQuestItem.Id.ToString()).MapIds,
                Is.EqualTo(new[] { "bigmap" }));
            Assert.That(resolved.Single(objective => objective.Id == streets.Id.ToString()).MapIds,
                Is.EqualTo(new[] { "TarkovStreets" }));
            var classified = QuestTemplateMapper.ClassifyObjectiveTaskLocations(
                new QuestLocationDto("marathon", "Transition", false, null),
                resolved,
                new Dictionary<string, string>(),
                [],
                new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>());
            var actual = QuestTemplateMapper.BuildActualMaps(classified);
            Assert.That(actual.Complete, Is.True);
            Assert.That(actual.Maps.Select(map => map.Id), Is.EquivalentTo(new[] { "bigmap", "TarkovStreets" }));
        });
    }

    [Test]
    public void ForcedQuestItemSpawnMapsUseCanonicalVariantIds()
    {
        var questItemId = new MongoId("000000000000000000000041");
        var ordinaryItemId = new MongoId("000000000000000000000044");
        var locations = new[]
        {
            ForcedSpawnLocation("factory4_night", questItemId, "000000000000000000000042"),
            ForcedSpawnLocation("Sandbox_high", questItemId, "000000000000000000000043"),
            ForcedSpawnLocation("Woods", ordinaryItemId, "000000000000000000000045"),
        };
        var items = new Dictionary<MongoId, TemplateItem>
        {
            [questItemId] = new TemplateItem
            {
                Id = questItemId,
                Properties = new TemplateItemProperties { QuestItem = true },
            },
            [ordinaryItemId] = new TemplateItem
            {
                Id = ordinaryItemId,
                Properties = new TemplateItemProperties { QuestItem = false },
            },
        };

        var lookup = QuestTemplateMapper.BuildQuestItemSpawnMapLookup(
            locations,
            QuestTemplateMapper.BuildQuestItemIdSet(items));

        Assert.Multiple(() =>
        {
            Assert.That(lookup[questItemId], Is.EqualTo(new[] { "Sandbox", "factory4_day" }));
            Assert.That(lookup.ContainsKey(ordinaryItemId), Is.False);
        });
    }

    [Test]
    public void ForcedQuestItemSpawnLookupHandlesNoApplicableLocations()
    {
        var lookup = QuestTemplateMapper.BuildQuestItemSpawnMapLookup(
            [],
            new HashSet<MongoId>());

        Assert.That(lookup, Is.Empty);
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
