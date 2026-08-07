using NUnit.Framework;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTQuestMap.Services;

namespace SPTQuestMap.Tests;

public sealed class QuestMapDataServiceTests
{
    [Test]
    public void BuildLocation_UsesSptLocaleNameForSpecificMap()
    {
        const string locationId = "56f40101d2720b2a4d8b45d6";
        var result = QuestTemplateMapper.BuildLocation(locationId, new Dictionary<string, string> { [$"{locationId} Name"] = "Customs localized" }, new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>());

        Assert.Multiple(() =>
        {
            Assert.That(result.Id, Is.EqualTo(locationId));
            Assert.That(result.Name, Is.EqualTo("Customs localized"));
            Assert.That(result.Any, Is.False);
            Assert.That(result.BannerImageUrl, Is.Null);
        });
    }

    [Test]
    public void BuildLocation_RepresentsAnyWithoutInventingAName()
    {
        var result = QuestTemplateMapper.BuildLocation("any", [], new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>());

        Assert.Multiple(() =>
        {
            Assert.That(result.Id, Is.EqualTo("any"));
            Assert.That(result.Name, Is.Null);
            Assert.That(result.Any, Is.True);
            Assert.That(result.BannerImageUrl, Is.Null);
        });
    }

    [Test]
    public void BuildLocation_UsesFirstSptBannerThroughFilesRoute()
    {
        const string locationId = "bigmap";
        var location = new SPTarkov.Server.Core.Models.Eft.Common.Location
        {
            Base = new LocationBase
            {
                Id = locationId,
                Banners = [new Banner { Picture = new Pic { Path = "banners/customs.png" } }],
            },
        };

        var result = QuestTemplateMapper.BuildLocation(
            locationId,
            [],
            new Dictionary<string, SPTarkov.Server.Core.Models.Eft.Common.Location>(StringComparer.OrdinalIgnoreCase) { [locationId] = location }
        );

        Assert.That(result.BannerImageUrl, Is.EqualTo("/files/banners/customs.png"));
    }

    [Test]
    public void BuildLocationLookup_MapsQuestMongoIdToInternalLocation()
    {
        const string questLocationId = "56f40101d2720b2a4d8b45d6";
        var customs = new SPTarkov.Server.Core.Models.Eft.Common.Location { Base = new LocationBase { Id = "bigmap" } };

        var result = QuestTemplateMapper.BuildLocationLookup(
            [customs],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["bigmap"] = questLocationId }
        );

        Assert.That(result[questLocationId], Is.SameAs(customs));
    }

    [Test]
    public void BuildRewards_IncludesUnknownAndHiddenSuccessItemsAndAggregatesRootStacks()
    {
        var templateId = new MongoId("5449016a4bdc2d6f028b456f");
        var rootOneId = new MongoId("68a9695194f6582e5914052b");
        var rootTwoId = new MongoId("68a9695194f6582e5914052c");
        var childId = new MongoId("68a9695194f6582e5914052d");
        var reward = new Reward
        {
            Id = new MongoId("60c8abe52238043a5267862f"),
            Type = RewardType.Item,
            Unknown = true,
            IsHidden = true,
            Items =
            [
                new Item { Id = rootOneId, Template = templateId, Upd = new Upd { StackObjectsCount = 2 } },
                new Item { Id = rootTwoId, Template = templateId, Upd = new Upd { StackObjectsCount = 3 } },
                new Item { Id = childId, Template = templateId, ParentId = rootOneId.ToString(), Upd = new Upd { StackObjectsCount = 99 } },
            ],
        };

        var result = QuestTemplateMapper.BuildRewards(
            [reward],
            new Dictionary<string, string> { [$"{templateId} Name"] = "Localized roubles" },
            new Dictionary<MongoId, TemplateItem> { [templateId] = new() { Id = templateId, Name = "Roubles" } },
            new Dictionary<MongoId, Trader>()
        ).Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.Unknown, Is.True);
            Assert.That(result.Hidden, Is.True);
            Assert.That(result.Items, Has.Length.EqualTo(1));
            Assert.That(result.Items[0].Name, Is.EqualTo("Localized roubles"));
            Assert.That(result.Items[0].Count, Is.EqualTo(5));
        });
    }

    [TestCase("ge", "de")]
    [TestCase("jp", "ja")]
    [TestCase("ch", "zh-CN")]
    [TestCase("es-mx", "es-mx")]
    public void ToBrowserLocale_NormalizesSptClientCodes(string sptCode, string expected)
    {
        Assert.That(QuestMapLocalizationService.ToBrowserLocale(sptCode), Is.EqualTo(expected));
    }

    [TestCase("ch")]
    [TestCase("cz")]
    [TestCase("es")]
    [TestCase("es-mx")]
    [TestCase("fr")]
    [TestCase("ge")]
    [TestCase("hu")]
    [TestCase("it")]
    [TestCase("jp")]
    [TestCase("kr")]
    [TestCase("pl")]
    [TestCase("po")]
    [TestCase("ro")]
    [TestCase("ru")]
    [TestCase("sk")]
    [TestCase("tu")]
    public void UiCatalog_HasLocalizedCoreChromeForEveryInstalledNonEnglishLocale(string language)
    {
        var catalog = QuestMapUiCatalog.For(language, new Dictionary<string, string>());
        var requiredExtendedKeys = new[]
        {
            "legend.priorGate", "legend.levelGate", "legend.traderRequirement", "legend.failedExcluded",
            "legend.collectorRoute", "legend.lightkeeperRoute", "legend.requiresSuccess", "legend.requiresFailure",
            "legend.requiresStarted", "legend.requiresOutcome",
            "state.PrerequisiteGated", "state.LevelGated", "state.TraderGated", "state.TraderUnavailable",
            "state.InProgress", "state.ReadyToFinish", "state.Excluded", "state.RestartableFailure", "state.Expired",
            "state.Pending", "state.FailRestartable", "state.AvailableAfter", "details.effectiveGates",
            "details.availableAfter", "details.currentBlockers", "details.mutualExclusion", "details.branchAlternatives",
            "details.prerequisites", "details.successors", "inProgress.title", "showFinished", "levelEligible", "trader.allShort",
            "compare.toggle", "compare.exit", "compare.swap", "compare.profileA", "compare.profileB",
            "compare.differencesOnly", "compare.repeatablesUnavailable", "compare.splitLegend", "compare.different",
            "compare.differencesShort", "compare.progress", "compare.status",
            "compare.filter.aria", "compare.filter.AllQuests", "compare.filter.AllChanges", "compare.filter.Objectives",
            "compare.filter.AvailableAfter", "compare.category.PrimaryOnlyNamed", "compare.changedQuest",
            "compare.matchingQuest", "compare.changeCount", "compare.noChanges", "compare.matchingFields",
            "compare.matchingObjectives", "compare.objectiveDelta",
        };
        var untranslatedExtended = requiredExtendedKeys
            .Where(key => catalog[key] == QuestMapUiCatalog.English[key])
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(catalog["app.heading"], Is.EqualTo("QuestMap"));
            Assert.That(catalog["app.title"], Is.EqualTo("SPT QuestMap"));
            Assert.That(catalog["profile.aria"], Is.Not.EqualTo(QuestMapUiCatalog.English["profile.aria"]));
            Assert.That(catalog["showAllFuture"], Is.Not.EqualTo(QuestMapUiCatalog.English["showAllFuture"]));
            Assert.That(untranslatedExtended, Is.Empty, $"Locale {language} must translate every gated, legend, and detail relationship label.");
        });
    }

    [Test]
    public void UiCatalog_UsesSelectedSptLocaleBeforeCustomOverrides()
    {
        var catalog = QuestMapUiCatalog.For("ge", new Dictionary<string, string>
        {
            ["Arena/CustomGames/toggle/Refresh"] = "Aktualisieren",
            ["QuestStatusSuccess"] = "Abgeschlossen",
        });

        Assert.Multiple(() =>
        {
            Assert.That(catalog["refresh"], Is.EqualTo("Aktualisieren"));
            Assert.That(catalog["state.Completed"], Is.EqualTo("Abgeschlossen"));
            Assert.That(catalog["profile.aria"], Is.EqualTo("Ausgewähltes Profil"));
        });
    }

    [TestCase(new[] { "Success" }, "Success")]
    [TestCase(new[] { "Fail" }, "Failure")]
    [TestCase(new[] { "Success", "Fail" }, "AnyOutcome")]
    [TestCase(new[] { "Started", "Success" }, "Started")]
    [TestCase(new[] { "Started", "Success", "Fail" }, "Started")]
    public void EdgeRequirementClassificationPreservesMixedStatusMeaning(string[] statuses, string expected)
    {
        Assert.That(QuestGraphRules.ClassifyEdgeRequirement(statuses), Is.EqualTo(expected));
    }

    [Test]
    public void GermanUiCatalog_TranslatesEveryNonInvariantDisplayString()
    {
        var catalog = QuestMapUiCatalog.For("ge", new Dictionary<string, string>());
        var intentionallyInvariant = new HashSet<string>(StringComparer.Ordinal)
        {
            "app.title",
            "app.heading",
            "badge.event",
            "badge.branch",
            "route.lightkeeper",
            "gates.loyaltyShort",
            "gates.reputation",
        };
        var missing = QuestMapUiCatalog.English.Keys.Where(key => !catalog.ContainsKey(key)).ToArray();
        var untranslated = QuestMapUiCatalog.English
            .Where(pair => !intentionallyInvariant.Contains(pair.Key) && catalog[pair.Key] == pair.Value)
            .Select(pair => pair.Key)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Empty, "Every English fallback key must exist in the German catalog.");
            Assert.That(untranslated, Is.Empty, "Every non-invariant German display string must differ from its English fallback.");
        });
    }

    [Test]
    public void MergeRequirementsKeepsStrongestLowerAndUpperBounds()
    {
        RequirementDto[] input =
        [
            new("Level", null, ">=", 10),
            new("Level", null, ">=", 20),
            new("TraderStanding", "trader", "<=", -1),
            new("TraderStanding", "trader", "<=", -2),
        ];

        var result = QuestGraphRules.MergeRequirements(input);

        Assert.That(result, Has.Length.EqualTo(2));
        Assert.That(result.Single(item => item.Kind == "Level").Value, Is.EqualTo(20));
        Assert.That(result.Single(item => item.Kind == "TraderStanding").Value, Is.EqualTo(-2));
    }

    [TestCase(5, 5, ">=", true)]
    [TestCase(5, 5, ">", false)]
    [TestCase(-2, -1, "<=", true)]
    [TestCase(2, 1, "<", false)]
    [TestCase(3, 3, "=", true)]
    public void CompareMatchesSptConditionOperators(double actual, double required, string compare, bool expected)
    {
        Assert.That(QuestGraphRules.Compare(actual, required, compare), Is.EqualTo(expected));
    }

    [Test]
    public void NoneEventRemovalCascadesOnlyThroughExclusivelyRemovedParents()
    {
        var topology = Topology(
            [Node("none", "None"), Node("child", null), Node("shared", null), Node("normal", null)],
            [new("none", "child", ["Success"], 0), new("none", "shared", ["Success"], 0), new("normal", "shared", ["Success"], 0)]
        );

        var result = QuestGraphRules.BuildNoneEventExclusionSet(topology);

        Assert.That(result, Does.Contain("none"));
        Assert.That(result, Does.Contain("child"));
        Assert.That(result, Does.Not.Contain("shared"));
        Assert.That(result, Does.Not.Contain("normal"));
    }

    [Test]
    public void SeasonalEventTypePropagatesThroughQuestChainDescendants()
    {
        var nodes = new[] { Node("halloween-root", "Halloween"), Node("middle", null), Node("darkest-hour", null), Node("normal", null) };
        var edges = new[]
        {
            new QuestEdgeDto("halloween-root", "middle", ["Success"], 0),
            new QuestEdgeDto("middle", "darkest-hour", ["Success"], 0),
        };

        var result = QuestGraphRules.PropagateSeasonalEventTypes(nodes, edges).ToDictionary(node => node.Id);

        Assert.Multiple(() =>
        {
            Assert.That(result["middle"].EventSeason, Is.EqualTo("Halloween"));
            Assert.That(result["darkest-hour"].EventSeason, Is.EqualTo("Halloween"));
            Assert.That(result["normal"].EventSeason, Is.Null);
        });
    }

    [Test]
    public void DefaultFrontierStopsAfterOneDirectSuccessor()
    {
        QuestEdgeDto[] edges =
        [
            new("known", "next", ["Success"], 0),
            new("next", "deep", ["Success"], 0),
        ];
        var known = Set("known");
        var boundary = Set("known");
        var applicable = Set("known", "next", "deep");

        var visible = QuestGraphRules.BuildDefaultVisible(known, boundary, edges, applicable);

        Assert.That(visible, Is.EquivalentTo(new[] { "known", "next" }));
    }

    [Test]
    public void CompletedQuestSeedsItsDirectFutureSuccessor()
    {
        QuestEdgeDto[] edges =
        [
            new("fertilizers", "collector", ["Success"], 0),
            new("collector", "beyond", ["Success"], 0),
        ];
        var known = Set("fertilizers");
        var boundary = Set("fertilizers");
        var applicable = Set("fertilizers", "collector", "beyond");

        var visible = QuestGraphRules.BuildDefaultVisible(known, boundary, edges, applicable);

        Assert.That(visible, Is.EquivalentTo(new[] { "fertilizers", "collector" }));
    }

    [Test]
    public void LevelGatedQuestDoesNotSeedItsFutureChain()
    {
        QuestEdgeDto[] edges =
        [
            new("grenadier", "test-drive-1", ["Success"], 0),
            new("test-drive-1", "test-drive-2", ["Success"], 0),
            new("test-drive-2", "test-drive-3", ["Success"], 0),
        ];
        var known = Set("grenadier");
        var applicable = Set("grenadier", "test-drive-1", "test-drive-2", "test-drive-3");

        var visible = QuestGraphRules.BuildDefaultVisible(known, [], edges, applicable);

        Assert.That(visible, Is.EquivalentTo(new[] { "grenadier" }));
    }

    [Test]
    public void DirectSuccessorAppearsWhenAnotherPrerequisiteBranchIsLocked()
    {
        QuestEdgeDto[] edges =
        [
            new("active", "merge", ["Success"], 0),
            new("gated", "merge", ["Success"], 0),
        ];
        var known = Set("active", "gated");
        var boundary = Set("active");
        var applicable = Set("active", "gated", "merge");

        var visible = QuestGraphRules.BuildDefaultVisible(known, boundary, edges, applicable);

        Assert.That(visible, Does.Contain("merge"));
    }

    [Test]
    public void ObjectiveOrderUsesIndexThenStableSourceOrder()
    {
        QuestCondition[] conditions =
        [
            Condition("000000000000000000000001", 4),
            Condition("000000000000000000000002", 1),
            Condition("000000000000000000000003", 1),
        ];

        var result = QuestTemplateMapper.OrderObjectives(conditions, []).Select(item => item.Id).ToArray();

        Assert.That(result, Is.EqualTo(new[] { "000000000000000000000002", "000000000000000000000003", "000000000000000000000001" }));
    }

    [Test]
    public void ObjectiveDependenciesOverrideConflictingIndices()
    {
        var parent = Condition("000000000000000000000010", 5);
        var child = Condition("000000000000000000000011", 1) with { ParentId = parent.Id.ToString() };

        var result = QuestTemplateMapper.OrderObjectives([child, parent], []).Select(item => item.Id).ToArray();

        Assert.That(result, Is.EqualTo(new[] { parent.Id.ToString(), child.Id.ToString() }));
    }

    [Test]
    public void ObjectiveTextUsesBareConditionIdLocaleKey()
    {
        var condition = Condition("000000000000000000000020", 0);
        var locale = new Dictionary<string, string>
        {
            [condition.Id.ToString()] = "Eliminate Scavs on any location",
        };

        var result = QuestTemplateMapper.OrderObjectives([condition], locale).Single();

        Assert.That(result.Text, Is.EqualTo("Eliminate Scavs on any location"));
    }

    [Test]
    public void DuplicateObjectiveIdsUseFirstDefinitionWithoutThrowing()
    {
        var first = Condition("6917c82760dbbed68c3cc90f", 2) with { Value = 10 };
        var duplicate = Condition("6917c82760dbbed68c3cc90f", 0) with { Value = 20 };
        var duplicateIds = new List<string>();

        var result = QuestTemplateMapper.OrderObjectives([first, duplicate], [], duplicateIds.Add).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].Id, Is.EqualTo(first.Id.ToString()));
            Assert.That(result[0].Index, Is.EqualTo(first.Index));
            Assert.That(result[0].RequiredValue, Is.EqualTo(first.Value));
            Assert.That(duplicateIds, Is.EqualTo(new[] { first.Id.ToString() }));
        });
    }

    [TestCase(QuestStatusEnum.Success, "Completed")]
    [TestCase(QuestStatusEnum.AvailableForFinish, "ReadyToFinish")]
    [TestCase(QuestStatusEnum.Started, "InProgress")]
    [TestCase(QuestStatusEnum.AvailableAfter, "Pending")]
    [TestCase(QuestStatusEnum.FailRestartable, "RestartableFailure")]
    [TestCase(QuestStatusEnum.Expired, "Expired")]
    public void ExactStatusesHaveDistinctDisplayStates(QuestStatusEnum status, string expected)
    {
        Assert.That(QuestProfileRules.Classify(Node("q", null), status, false, null, []), Is.EqualTo(expected));
    }

    [Test]
    public void PermanentExclusionTakesPrecedenceOverOrdinaryFailure()
    {
        var exclusion = new QuestExclusionDto("branch", "Success", true);

        var result = QuestProfileRules.Classify(Node("q", null), QuestStatusEnum.Fail, false, exclusion, []);

        Assert.That(result, Is.EqualTo("Excluded"));
    }

    [Test]
    public void TraderRequirementTakesPrecedenceOverPrerequisiteGate()
    {
        QuestBlockerDto[] blockers =
        [
            new("TraderLoyalty", "trader", ">=", 2, []),
            new("Prerequisite", "prior-quest", null, null, ["Success"]),
        ];

        var result = QuestProfileRules.Classify(Node("q", null), QuestStatusEnum.Locked, false, null, blockers);

        Assert.That(result, Is.EqualTo("TraderGated"));
    }

    [Test]
    public void GatePriorityIsTraderAvailabilityThenLevelThenTraderRequirementThenPrerequisite()
    {
        QuestBlockerDto[] blockers =
        [
            new("Prerequisite", "prior", null, null, ["Success"]),
            new("TraderStanding", "trader", ">=", 0.2, []),
            new("Level", null, ">=", 20, []),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(QuestProfileRules.Classify(Node("q", null), QuestStatusEnum.Locked, false, null, blockers), Is.EqualTo("LevelGated"));
            Assert.That(QuestProfileRules.Classify(Node("q", null), QuestStatusEnum.Locked, false, null, blockers.Where(item => item.Kind != "Level").ToArray()), Is.EqualTo("TraderGated"));
            Assert.That(QuestProfileRules.Classify(Node("q", null), QuestStatusEnum.Locked, false, null, blockers.Where(item => item.Kind == "Prerequisite").ToArray()), Is.EqualTo("PrerequisiteGated"));
        });
    }

    [Test]
    public void InheritedEffectiveGatesOverrideUnmetPrerequisiteForLowLevelProfile()
    {
        var quest = Node("reconnaissance", null) with
        {
            TraderId = Traders.PRAPOR.ToString(),
            DirectRequirements = [],
            EffectiveRequirements =
            [
                new RequirementDto("Level", null, ">=", 18),
                new RequirementDto("TraderLoyalty", Traders.PRAPOR.ToString(), ">=", 2),
            ],
        };
        var traders = new Dictionary<MongoId, TraderInfo>
        {
            [Traders.PRAPOR] = new() { Unlocked = true, Disabled = false, LoyaltyLevel = 1, Standing = 0 },
        };
        QuestEdgeDto[] edges = [new("prior-quest", quest.Id, [nameof(QuestStatusEnum.Success)], 0)];

        var blockers = QuestProfileRules.GetBlockers(quest, 9, traders, [], edges, Availability(traders, new Dictionary<string, string>()));

        Assert.Multiple(() =>
        {
            Assert.That(blockers.Select(blocker => blocker.Kind), Is.EqualTo(new[] { "Level", "TraderLoyalty", "Prerequisite" }));
            Assert.That(QuestProfileRules.Classify(quest, QuestStatusEnum.Locked, false, null, blockers), Is.EqualTo("LevelGated"));
        });
    }

    [Test]
    public void ActualTraderRequirementRemainsTraderGated()
    {
        QuestBlockerDto[] blockers = [new("TraderStanding", "trader", ">=", 0.2, [])];

        var result = QuestProfileRules.Classify(Node("q", null), QuestStatusEnum.Locked, false, null, blockers);

        Assert.That(result, Is.EqualTo("TraderGated"));
    }

    [Test]
    public void UnavailableTraderOverridesAuthoritativeAvailableStateAndVisibility()
    {
        QuestBlockerDto[] blockers = [new("TraderUnavailable", "lightkeeper", null, null, [])];

        Assert.Multiple(() =>
        {
            Assert.That(QuestProfileRules.Classify(Node("q", null), QuestStatusEnum.AvailableForStart, true, null, blockers), Is.EqualTo("TraderUnavailable"));
            Assert.That(QuestProfileRules.Classify(Node("q", null), null, true, null, blockers), Is.EqualTo("TraderUnavailable"));
            Assert.That(QuestProfileRules.IsEffectivelyVisible(true, blockers), Is.False);
        });
    }

    [Test]
    public void UnavailableTraderTakesPriorityOverEveryOrdinaryGate()
    {
        QuestBlockerDto[] blockers =
        [
            new("Prerequisite", "prior", null, null, ["Success"]),
            new("Level", null, ">=", 20, []),
            new("TraderUnavailable", "trader", null, null, []),
        ];

        Assert.That(QuestProfileRules.Classify(Node("q", null), QuestStatusEnum.Locked, false, null, blockers), Is.EqualTo("TraderUnavailable"));
    }

    [Test]
    public void UnavailableTraderQuestIsHiddenFromDefaultButStartedQuestRemainsVisible()
    {
        var hidden = State("TraderUnavailable");
        var started = State("InProgress");

        Assert.Multiple(() =>
        {
            Assert.That(QuestProfileRules.ShouldShowInDefaultGraph(hidden), Is.False);
            Assert.That(QuestProfileRules.ShouldShowInDefaultGraph(started), Is.True);
        });
    }

    [Test]
    public void JaegerAndRefRequireTheirUnlockQuestSuccess()
    {
        var traders = new Dictionary<MongoId, TraderInfo>
        {
            [Traders.JAEGER] = new() { Unlocked = true, Disabled = false },
            [Traders.REF] = new() { Unlocked = true, Disabled = false },
        };
        var completed = new Dictionary<string, string>
        {
            ["5d2495a886f77425cd51e403"] = nameof(QuestStatusEnum.Success),
            ["6834145ebc1f443d7603c8a7"] = nameof(QuestStatusEnum.Success),
        };

        var unavailable = Availability(traders, new Dictionary<string, string>());
        var available = Availability(traders, completed);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.IsAvailable(Traders.JAEGER), Is.False);
            Assert.That(unavailable.IsAvailable(Traders.REF), Is.False);
            Assert.That(available.IsAvailable(Traders.JAEGER), Is.True);
            Assert.That(available.IsAvailable(Traders.REF), Is.True);
        });
    }

    [Test]
    public void LightkeeperAvailabilityRequiresKnockKnockSuccessDirectly()
    {
        var traders = new Dictionary<MongoId, TraderInfo>
        {
            [Traders.LIGHTHOUSEKEEPER] = new() { Unlocked = true, Disabled = false },
        };
        var incomplete = Availability(traders, new Dictionary<string, string>());
        var complete = Availability(traders, new Dictionary<string, string>
        {
            [QuestMapQuestIds.KnockKnock] = nameof(QuestStatusEnum.Success),
        });

        Assert.Multiple(() =>
        {
            Assert.That(incomplete.IsAvailable(Traders.LIGHTHOUSEKEEPER), Is.False);
            Assert.That(complete.IsAvailable(Traders.LIGHTHOUSEKEEPER), Is.True);
        });
    }

    [Test]
    public void CollectorPathContainsCollectorAndAllRecursivePrerequisitesOnly()
    {
        QuestEdgeDto[] edges =
        [
            new("root", "required", ["Success"], 0),
            new("required", "collector", ["Success"], 0),
            new("other", "collector", ["Success"], 0),
            new("collector", "later", ["Success"], 0),
        ];

        var result = QuestGraphRules.BuildPrerequisiteClosure("collector", edges, Set("root", "required", "other", "collector", "later"));

        Assert.That(result, Is.EquivalentTo(new[] { "root", "required", "other", "collector" }));
    }

    [Test]
    public void ObjectiveProgressWeightsStagesEquallyAndUsesInternalCounts()
    {
        ObjectiveProgressDto[] objectives =
        [
            new("scavs", false, 1, 10, true),
            new("bears", false, 0, 5, true),
            new("tushonka", false, 0, 2, true),
        ];

        var partial = QuestProfileRules.CalculateObjectiveProgress(objectives);
        ObjectiveProgressDto[] completedFirstStage =
        [
            objectives[0] with { Complete = true, Current = 10 },
            objectives[1],
            objectives[2],
        ];
        var firstStageComplete = QuestProfileRules.CalculateObjectiveProgress(completedFirstStage);

        Assert.Multiple(() =>
        {
            Assert.That(partial, Is.EqualTo(3.3));
            Assert.That(firstStageComplete, Is.EqualTo(33.3));
        });
    }

    [Test]
    public void ObjectiveProgressClampsOverCompletionAndHandlesNoObjectives()
    {
        var clamped = QuestProfileRules.CalculateObjectiveProgress([new("over", false, 15, 10, true)]);

        Assert.Multiple(() =>
        {
            Assert.That(clamped, Is.EqualTo(100));
            Assert.That(QuestProfileRules.CalculateObjectiveProgress([]), Is.Null);
        });
    }

    [Test]
    public void ObjectiveCurrentCapsAtRequirementWithoutChangingOtherBoundaries()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestProfileRules.CapObjectiveCurrent(0, 1), Is.EqualTo(0));
            Assert.That(QuestProfileRules.CapObjectiveCurrent(1, 1), Is.EqualTo(1));
            Assert.That(QuestProfileRules.CapObjectiveCurrent(2, 1), Is.EqualTo(1));
            Assert.That(QuestProfileRules.CapObjectiveCurrent(1.25, 1), Is.EqualTo(1));
            Assert.That(QuestProfileRules.CapObjectiveCurrent(2, 0), Is.EqualTo(0));
            Assert.That(QuestProfileRules.CapObjectiveCurrent(-1, 1), Is.EqualTo(-1));
            Assert.That(QuestProfileRules.CapObjectiveCurrent(null, 1), Is.Null);
            Assert.That(QuestProfileRules.CapObjectiveCurrent(2, null), Is.EqualTo(2));
        });
    }

    [Test]
    public void ObjectiveCounterSatisfactionCompletesConditionWhenCompletedConditionsOmitsIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestProfileRules.ObjectiveIsComplete(false, 3, 3, null), Is.True);
            Assert.That(QuestProfileRules.ObjectiveIsComplete(false, 2, 3, null), Is.False);
            Assert.That(QuestProfileRules.ObjectiveIsComplete(false, 3, 3, ">"), Is.False);
            Assert.That(QuestProfileRules.ObjectiveIsComplete(false, 2, 1, "=="), Is.False);
            Assert.That(QuestProfileRules.ObjectiveIsComplete(true, null, 3, null), Is.True);
        });
    }

    private static QuestCondition Condition(string id, int index) => new()
    {
        Id = new MongoId(id),
        Index = index,
        DynamicLocale = false,
        ConditionType = "CounterCreator",
    };

    private static QuestTopologyDto Topology(QuestNodeDto[] nodes, QuestEdgeDto[] edges) => new("test", nodes, edges, [], [], []);

    private static TraderAvailabilityEvaluator Availability(
        Dictionary<MongoId, TraderInfo> traders,
        IReadOnlyDictionary<string, string> statuses
    ) => new(traders, statuses);

    private static QuestStateDto State(string displayState) => new("q", null, displayState, false, false, null, [], null, [], null);

    private static QuestNodeDto Node(string id, string? season) => new(id, id, string.Empty, "trader", "Trader", null, string.Empty, "Any", new QuestLocationDto("any", null, true, null), season, false, [], [], [], [], []);

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

}
