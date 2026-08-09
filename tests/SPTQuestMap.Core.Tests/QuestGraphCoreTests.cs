using NUnit.Framework;
using SPTQuestMap.Core.Adapters;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using SPTQuestMap.Core.Transport;

namespace SPTQuestMap.Core.Tests;

[TestFixture]
public sealed class QuestGraphCoreTests
{
    [Test]
    public void ProgressIncrease_UsesRequiredValueAsNotificationCap()
    {
        var beforeCap = new QuestObjectiveProgress("objective", false, 49, 50, true);
        var reachesCap = new QuestObjectiveProgress("objective", true, 50, 50, true);
        var beyondCap = new QuestObjectiveProgress("objective", true, 51, 50, true);

        Assert.Multiple(() =>
        {
            Assert.That(QuestProgressRules.HasEffectiveIncrease(reachesCap, beforeCap), Is.True);
            Assert.That(QuestProgressRules.HasEffectiveIncrease(beyondCap, reachesCap), Is.False);
            Assert.That(QuestProgressRules.EffectiveValue(beyondCap), Is.EqualTo(50));
        });
    }

    [TestCase(new[] { "Success" }, QuestEdgeRequirementKind.Success)]
    [TestCase(new[] { "Fail" }, QuestEdgeRequirementKind.Failure)]
    [TestCase(new[] { "FailRestartable" }, QuestEdgeRequirementKind.Failure)]
    [TestCase(new[] { "Started" }, QuestEdgeRequirementKind.Started)]
    [TestCase(new[] { "Success", "Fail" }, QuestEdgeRequirementKind.AnyOutcome)]
    [TestCase(new[] { "AvailableForStart" }, QuestEdgeRequirementKind.Unknown)]
    public void ClassifyEdgeRequirement_IsExplicit(string[] statuses, QuestEdgeRequirementKind expected)
    {
        Assert.That(QuestGraphRules.ClassifyEdgeRequirement(statuses), Is.EqualTo(expected));
    }

    [Test]
    public void Normalize_BuildsStableLookupsAndProfileGeneratedNodes()
    {
        var feed = Feed(
            [Node("b", "Prapor", "Any"), Node("a", "Therapist", "USEC")],
            [Edge("a", "b", "Success")]);
        feed.ProfileGeneratedQuests = [Node("daily", "Prapor", "Any")];

        var topology = QuestTopologyNormalizer.Normalize(feed);

        Assert.Multiple(() =>
        {
            Assert.That(topology.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "a", "b", "daily" }));
            Assert.That(topology.NodesById["daily"].ProfileGenerated, Is.True);
            Assert.That(topology.IncomingEdgesByTarget["b"].Single().SourceId, Is.EqualTo("a"));
            Assert.That(topology.OutgoingEdgesBySource["a"].Single().TargetId, Is.EqualTo("b"));
            Assert.That(topology.TradersById.ContainsKey("prapor"), Is.True);
            Assert.That(topology.Version, Does.StartWith("test:generated:"));
        });
    }

    [Test]
    public void Normalize_ProfileGeneratedMembershipScopesTopologyVersion()
    {
        var first = Feed([Node("static", "Prapor", "Any")], []);
        first.ProfileGeneratedQuests = [Node("daily-a", "Prapor", "Any")];
        var second = Feed([Node("static", "Prapor", "Any")], []);
        second.ProfileGeneratedQuests = [Node("daily-b", "Prapor", "Any")];

        Assert.That(QuestTopologyNormalizer.Normalize(first).Version,
            Is.Not.EqualTo(QuestTopologyNormalizer.Normalize(second).Version));
    }

    [Test]
    public void Normalize_RetainsMissingReferencesAndUnknownConditions()
    {
        var node = Node("target", "Prapor", "Any");
        node.UnknownConditions = [new QuestUnknownConditionPayload { Stage = "AvailableForStart", ConditionType = "Edition", ConditionId = "c1" }];
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [node],
            [Edge("missing", "target", "Success"), Edge("target", "missing-target", "Success")]));

        Assert.Multiple(() =>
        {
            Assert.That(topology.Edges, Has.Count.EqualTo(2));
            Assert.That(topology.Diagnostics.MissingPredecessorIds, Is.EqualTo(new[] { "missing" }));
            Assert.That(topology.Diagnostics.MissingTargetIds, Is.EqualTo(new[] { "missing-target" }));
            Assert.That(topology.Diagnostics.UnknownConditionCount, Is.EqualTo(1));
            Assert.That(topology.NodesById["target"].UnknownConditions.Single().ConditionType, Is.EqualTo("Edition"));
        });
    }

    [Test]
    public void Traversal_IsCycleSafeAndReturnsDirectSuccessorsOnly()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [Node("a", "Prapor", "Any"), Node("b", "Prapor", "Any"), Node("c", "Prapor", "Any")],
            [Edge("a", "b", "Success"), Edge("b", "a", "Success"), Edge("b", "c", "Success")]));

        Assert.Multiple(() =>
        {
            Assert.That(QuestGraphRules.BuildPrerequisiteClosure(topology, "c"), Is.EquivalentTo(new[] { "a", "b", "c" }));
            Assert.That(QuestGraphRules.GetDirectSuccessors(topology, "b"), Is.EqualTo(new[] { "a", "c" }));
        });
    }

    [Test]
    public void Filters_RespectTraderFactionAndEvent()
    {
        var ordinary = Node("ordinary", "Prapor", "Any");
        var usec = Node("usec", "Therapist", "USEC");
        var bear = Node("bear", "Prapor", "BEAR");
        var christmas = Node("christmas", "Prapor", "Any");
        christmas.EventSeason = "Christmas";
        var removed = Node("removed", "Prapor", "Any");
        removed.EventSeason = "None";
        var removedChild = Node("removed-child", "Prapor", "Any");
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [ordinary, usec, bear, christmas, removed, removedChild],
            [Edge("removed", "removed-child", "Success")]));

        Assert.Multiple(() =>
        {
            Assert.That(QuestGraphRules.FilterByTrader(topology.Nodes, "prapor").Select(node => node.Id), Is.EquivalentTo(new[] { "ordinary", "bear", "christmas", "removed", "removed-child" }));
            Assert.That(QuestGraphRules.FilterByFaction(topology.Nodes, "USEC").Select(node => node.Id), Is.EquivalentTo(new[] { "ordinary", "usec", "christmas", "removed", "removed-child" }));
            Assert.That(QuestGraphRules.FilterByEvent(topology, Set("Christmas")).Select(node => node.Id), Is.EquivalentTo(new[] { "ordinary", "usec", "bear", "christmas" }));
        });
    }

    [Test]
    public void Frontier_AddsOnlyImmediateApplicableSuccessors()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [Node("known", "Prapor", "Any"), Node("next", "Prapor", "Any"), Node("later", "Prapor", "Any"), Node("hidden", "Prapor", "Any")],
            [Edge("known", "next", "Success"), Edge("next", "later", "Success"), Edge("known", "hidden", "Success")]));
        var visible = QuestGraphRules.BuildDefaultFrontier(
            topology,
            Set("known"),
            Set("known"),
            Set("known", "next", "later"));

        Assert.That(visible, Is.EquivalentTo(new[] { "known", "next" }));
    }

    [Test]
    public void ActiveAndFinishedFilters_PreserveRestartableFailure()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [Node("active", "Prapor", "Any"), Node("done", "Prapor", "Any"), Node("retry", "Prapor", "Any")], []));
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("active", "Started"),
            Quest("done", "Success"),
            Quest("retry", "FailRestartable")));

        Assert.Multiple(() =>
        {
            Assert.That(QuestGraphRules.FilterActive(topology, overlay).Select(node => node.Id), Is.EqualTo(new[] { "active", "retry" }));
            Assert.That(QuestGraphRules.FilterFinishedAndFailed(topology, overlay, true).Select(node => node.Id), Is.EquivalentTo(new[] { "active", "retry" }));
        });
    }

    [Test]
    public void Layout_IsDeterministicAndCycleSafe()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [Node("c", "Prapor", "Any"), Node("a", "Prapor", "Any"), Node("b", "Prapor", "Any")],
            [Edge("a", "b", "Success"), Edge("b", "a", "Success"), Edge("b", "c", "Success")]));

        var first = DeterministicGraphLayout.Build(topology);
        var second = DeterministicGraphLayout.Build(topology);

        Assert.Multiple(() =>
        {
            Assert.That(first.NodesById, Is.EqualTo(second.NodesById));
            Assert.That(first.NodesById.Keys, Is.EquivalentTo(new[] { "a", "b", "c" }));
            Assert.That(first.Width, Is.GreaterThan(0));
            Assert.That(first.Height, Is.GreaterThan(0));
        });
    }

    [Test]
    public void Layout_RankTracksAcyclicPrerequisiteDepth()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [Node("root", "Prapor", "Any"), Node("middle", "Prapor", "Any"), Node("leaf", "Prapor", "Any")],
            [Edge("root", "middle", "Success"), Edge("middle", "leaf", "Success")]));

        var layout = DeterministicGraphLayout.Build(topology);

        Assert.Multiple(() =>
        {
            Assert.That(layout.NodesById["root"].Rank, Is.EqualTo(0));
            Assert.That(layout.NodesById["middle"].Rank, Is.EqualTo(1));
            Assert.That(layout.NodesById["leaf"].Rank, Is.EqualTo(2));
        });
    }

    [Test]
    public void GlobalProjection_UsesAuthoritativeFrontierAndApplicableSets()
    {
        var feed = Feed(
            [Node("known", "Prapor", "Any"), Node("frontier", "Prapor", "Any"), Node("future", "Therapist", "Any")],
            [Edge("known", "frontier", "Success"), Edge("frontier", "future", "Success")]);
        feed.DefaultVisibleQuestIds = ["known", "frontier"];
        feed.AllApplicableQuestIds = ["known", "frontier", "future"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(Quest("known", "Started")));

        var frontier = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, false, false, false, null, null, null, null, QuestRouteFilter.None));
        var allFuture = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, true, false, false, null, null, null, null, QuestRouteFilter.None));

        Assert.Multiple(() =>
        {
            Assert.That(frontier.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "known", "frontier" }));
            Assert.That(allFuture.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "known", "frontier", "future" }));
            Assert.That(frontier.Edges.Select(edge => (edge.SourceId, edge.TargetId)), Is.EqualTo(new[] { ("known", "frontier") }));
        });
    }

    [Test]
    public void GlobalProjection_AppliesActiveSearchTraderRouteAndFocusFilters()
    {
        var root = Node("root", "Prapor", "Any");
        var selected = Node("selected", "Prapor", "Any");
        var direct = Node("direct", "Therapist", "Any");
        var unrelated = Node("unrelated", "Therapist", "Any");
        var feed = Feed(
            [root, selected, direct, unrelated],
            [Edge("root", "selected", "Success"), Edge("selected", "direct", "Success")]);
        feed.DefaultVisibleQuestIds = ["root", "selected", "direct", "unrelated"];
        feed.AllApplicableQuestIds = ["root", "selected", "direct", "unrelated"];
        feed.Topology.CollectorPathQuestIds = ["root", "selected", "direct"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("root", "Success"),
            Quest("selected", "Started"),
            Quest("direct", "AvailableForFinish"),
            Quest("unrelated", "Started")));
        var layout = DeterministicGraphLayout.Build(topology);

        var active = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            layout,
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, null, "therapist", "unrelated", null, QuestRouteFilter.None));
        var focusedRoute = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            layout,
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, true, false, false, null, null, null, "selected", QuestRouteFilter.Collector));

        Assert.Multiple(() =>
        {
            Assert.That(active.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "unrelated" }));
            Assert.That(focusedRoute.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "root", "selected", "direct" }));
            Assert.That(focusedRoute.Edges, Has.Count.EqualTo(2));
            Assert.That(topology.CollectorPathQuestIds, Is.EquivalentTo(new[] { "root", "selected", "direct" }));
        });
    }

    [Test]
    public void ApplyProfileGeneratedDelta_ReusesStaticTopologyAndSwapsOnlyGeneratedNodes()
    {
        var feed = Feed(
            [Node("root", "Prapor", "Any"), Node("next", "Prapor", "Any")],
            [Edge("root", "next", "Success")]);
        feed.ProfileGeneratedQuests = [Node("old-daily", "Prapor", "Any")];
        feed.RepeatableKinds["old-daily"] = "Daily";
        var current = QuestTopologyNormalizer.Normalize(feed);
        var delta = new QuestRepeatableFeed
        {
            ProfileGeneratedQuests = [Node("new-daily", "Therapist", "Any")],
            RepeatableKinds = new Dictionary<string, string>(StringComparer.Ordinal) { ["new-daily"] = "Daily" },
            DefaultVisibleQuestIds = ["root", "next", "new-daily"],
            AllApplicableQuestIds = ["root", "next", "new-daily"],
        };

        var updated = QuestTopologyNormalizer.ApplyProfileGeneratedDelta(current, delta);

        Assert.Multiple(() =>
        {
            Assert.That(updated.NodesById.Keys, Is.EquivalentTo(new[] { "root", "next", "new-daily" }));
            Assert.That(updated.NodesById["new-daily"].ProfileGenerated, Is.True);
            Assert.That(updated.NodesById["new-daily"].RepeatableKind, Is.EqualTo("Daily"));
            Assert.That(updated.Edges, Is.SameAs(current.Edges));
            Assert.That(updated.Traders, Is.SameAs(current.Traders));
            Assert.That(updated.Version, Is.Not.EqualTo(current.Version));
        });
    }

    [Test]
    public void Normalize_AttachesClientSummaryWithoutChangingNodeTransportShape()
    {
        var feed = Feed([Node("quest", "Prapor", "Any")], []);
        feed.QuestSummaries["quest"] = "  Condensed quest summary.  ";

        var topology = QuestTopologyNormalizer.Normalize(feed);

        Assert.That(topology.NodesById["quest"].Summary, Is.EqualTo("Condensed quest summary."));
    }

    [Test]
    public void Overlay_CounterSatisfactionMarksBinaryChildObjectiveComplete()
    {
        var node = Node("quest", "Prapor", "Any");
        node.Objectives =
        [
            new QuestObjectivePayload
            {
                Id = "binary-child",
                Text = "Binary child",
                RequiredValue = 1,
                Compare = ">=",
            },
        ];
        var topology = QuestTopologyNormalizer.Normalize(Feed([node], []));
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            new LiveQuestSnapshot("quest", "Started", true,
            [
                new QuestObjectiveProgress("binary-child", false, 1, 1, true),
            ], null, false)));

        Assert.That(overlay.QuestsById["quest"].Objectives.Single().Complete, Is.True);
    }

    [Test]
    public void GlobalProjection_InProgressLocationTogglesUseOrSemanticsIncludingAnyLocation()
    {
        var woods = Node("woods", "Prapor", "Woods");
        woods.Location = new QuestLocationPayload { Id = "woods", Name = "Woods", Any = false, BannerImageUrl = "/woods.png" };
        var customs = Node("customs", "Prapor", "Customs");
        customs.Location = new QuestLocationPayload { Id = "customs", Name = "Customs", Any = false, BannerImageUrl = "/customs.png" };
        var anywhere = Node("anywhere", "Prapor", "Any");
        var feed = Feed([woods, customs, anywhere], []);
        feed.DefaultVisibleQuestIds = ["woods", "customs", "anywhere"];
        feed.AllApplicableQuestIds = ["woods", "customs", "anywhere"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("woods", "Started"), Quest("customs", "Started"), Quest("anywhere", "Started")));

        var projection = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, null, null, null, null,
                QuestRouteFilter.None, null, ["woods", "customs", "any"]));

        var noneSelected = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, null, null, null, null,
                QuestRouteFilter.None, null, []));

        Assert.Multiple(() =>
        {
            Assert.That(projection.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "woods", "customs", "anywhere" }));
            Assert.That(noneSelected.Nodes, Is.Empty);
        });
    }

    [Test]
    public void GlobalProjection_InRaidExcludesReadyToFinishQuests()
    {
        var feed = Feed([Node("started", "Prapor", "Any"), Node("ready", "Prapor", "Any")], []);
        feed.DefaultVisibleQuestIds = ["started", "ready"];
        feed.AllApplicableQuestIds = ["started", "ready"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("started", "Started"), Quest("ready", "AvailableForFinish")));

        var projection = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false,
                null, null, null, null, QuestRouteFilter.None, ExcludeReadyToFinish: true));

        Assert.That(projection.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "started" }));
    }

    [Test]
    public void GlobalProjection_InProgressRepeatableAllAddsOnlyAvailableRepeatables()
    {
        var active = Node("active", "Prapor", "Any");
        var availableDaily = Node("available-daily", "Prapor", "Any");
        var availableOrdinary = Node("available-ordinary", "Prapor", "Any");
        var feed = Feed([active, availableOrdinary], []);
        feed.ProfileGeneratedQuests = [availableDaily];
        feed.RepeatableKinds[availableDaily.Id] = "Daily";
        feed.DefaultVisibleQuestIds = [active.Id, availableDaily.Id, availableOrdinary.Id];
        feed.AllApplicableQuestIds = [active.Id, availableDaily.Id, availableOrdinary.Id];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest(active.Id, "Started"),
            Quest(availableDaily.Id, "AvailableForStart"),
            Quest(availableOrdinary.Id, "AvailableForStart")));
        var layout = DeterministicGraphLayout.Build(topology);

        var activeOnly = GlobalQuestGraphProjectionBuilder.Build(topology, layout, overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, null, null, null, null,
                QuestRouteFilter.None));
        var allRepeatables = GlobalQuestGraphProjectionBuilder.Build(topology, layout, overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, null, null, null, null,
                QuestRouteFilter.None, IncludeAvailableRepeatables: true));

        Assert.Multiple(() =>
        {
            Assert.That(activeOnly.Nodes.Select(node => node.Id), Is.EqualTo(new[] { active.Id }));
            Assert.That(allRepeatables.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { active.Id, availableDaily.Id }));
        });
    }

    [Test]
    public void InProgressTableSort_KeepsRepeatableSectionsFirstAndSupportsOrderedCriteria()
    {
        var daily = Node("daily", "Therapist", "Any");
        daily.Name = "Daily task";
        var weekly = Node("weekly", "Prapor", "Any");
        weekly.Name = "Weekly task";
        var customs = Node("customs", "Prapor", "Any");
        customs.Name = "Alpha";
        customs.Location = new QuestLocationPayload { Id = "customs", Name = "Customs" };
        var woods = Node("woods", "Prapor", "Any");
        woods.Name = "Zulu";
        woods.Location = new QuestLocationPayload { Id = "woods", Name = "Woods" };
        var therapist = Node("therapist", "Therapist", "Any");
        therapist.Name = "Bravo";
        therapist.Location = new QuestLocationPayload { Id = "woods", Name = "Woods" };
        var feed = Feed([customs, woods, therapist], []);
        feed.ProfileGeneratedQuests = [daily, weekly];
        feed.RepeatableKinds["daily"] = "Daily";
        feed.RepeatableKinds["weekly"] = "Weekly";
        feed.DefaultVisibleQuestIds = ["daily", "weekly", "customs", "woods", "therapist"];
        feed.AllApplicableQuestIds = ["daily", "weekly", "customs", "woods", "therapist"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("daily", "Started"), Quest("weekly", "Started"), Quest("customs", "Started"),
            Quest("woods", "Started"), Quest("therapist", "Started"))) with
        {
            AuthoritativeProgressPercentages = new Dictionary<string, double?>(StringComparer.Ordinal)
            {
                ["daily"] = 50,
                ["weekly"] = 50,
                ["customs"] = 10,
                ["woods"] = 90,
                ["therapist"] = 80,
            },
        };

        var defaultOrder = InProgressQuestTableSorter.Sort(topology.Nodes, topology, overlay, []);
        var manualOrder = InProgressQuestTableSorter.Sort(topology.Nodes, topology, overlay,
        [
            new QuestTableSortCriterion(QuestTableSortColumn.Trader, QuestTableSortDirection.Ascending),
            new QuestTableSortCriterion(QuestTableSortColumn.Progress, QuestTableSortDirection.Descending),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(defaultOrder.Select(node => node.Id),
                Is.EqualTo(new[] { "daily", "weekly", "customs", "woods", "therapist" }));
            Assert.That(manualOrder.Select(node => node.Id),
                Is.EqualTo(new[] { "daily", "weekly", "woods", "customs", "therapist" }));
        });
    }

    [Test]
    public void InProgressTableSortToggle_CyclesAndRetainsCriterionPriority()
    {
        IReadOnlyList<QuestTableSortCriterion> criteria = [];
        criteria = InProgressQuestTableSorter.Toggle(criteria, QuestTableSortColumn.Trader);
        criteria = InProgressQuestTableSorter.Toggle(criteria, QuestTableSortColumn.Progress);
        criteria = InProgressQuestTableSorter.Toggle(criteria, QuestTableSortColumn.Progress);

        Assert.Multiple(() =>
        {
            Assert.That(criteria.Select(criterion => criterion.Column),
                Is.EqualTo(new[] { QuestTableSortColumn.Trader, QuestTableSortColumn.Progress }));
            Assert.That(criteria[0].Direction, Is.EqualTo(QuestTableSortDirection.Ascending));
            Assert.That(criteria[1].Direction, Is.EqualTo(QuestTableSortDirection.Descending));
        });

        criteria = InProgressQuestTableSorter.Toggle(criteria, QuestTableSortColumn.Progress);
        Assert.That(criteria.Select(criterion => criterion.Column), Is.EqualTo(new[] { QuestTableSortColumn.Trader }));
    }

    [Test]
    public void InProgressTableDefaultSort_UsesCanonicalTraderThenServerQuestOrder()
    {
        var therapistSecond = Node("therapist-second", "Therapist", "Any");
        therapistSecond.TraderId = "54cb57776803fa99248b456e";
        therapistSecond.Name = "Alpha";
        var praporSecond = Node("prapor-second", "Prapor", "Any");
        praporSecond.TraderId = "54cb50c76803fa8b248b4571";
        praporSecond.Name = "Alpha";
        var praporFirst = Node("prapor-first", "Prapor", "Any");
        praporFirst.TraderId = "54cb50c76803fa8b248b4571";
        praporFirst.Name = "Zulu";
        var feed = Feed([therapistSecond, praporFirst, praporSecond], []);
        feed.DefaultVisibleQuestIds = ["therapist-second", "prapor-first", "prapor-second"];
        feed.AllApplicableQuestIds = ["therapist-second", "prapor-first", "prapor-second"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("therapist-second", "Started"), Quest("prapor-first", "Started"), Quest("prapor-second", "Started")));

        var ordered = InProgressQuestTableSorter.Sort(topology.Nodes, topology, overlay, []);

        Assert.That(ordered.Select(node => node.Id),
            Is.EqualTo(new[] { "prapor-first", "prapor-second", "therapist-second" }));
    }

    [Test]
    public void InProgressTableSortWithinSection_AppliesSelectedSortAcrossRepeatableKinds()
    {
        var daily = Node("daily", "Therapist", "Any");
        daily.Name = "Zulu";
        var ordinary = Node("ordinary", "Prapor", "Any");
        ordinary.Name = "Alpha";
        var feed = Feed([daily, ordinary], []);
        feed.ProfileGeneratedQuests = [daily];
        feed.RepeatableKinds["daily"] = "Daily";
        feed.DefaultVisibleQuestIds = ["daily", "ordinary"];
        feed.AllApplicableQuestIds = ["daily", "ordinary"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(Quest("daily", "Started"), Quest("ordinary", "Started")));

        var ordered = InProgressQuestTableSorter.SortWithinSection(topology.Nodes, topology, overlay,
        [
            new QuestTableSortCriterion(QuestTableSortColumn.Quest, QuestTableSortDirection.Ascending),
        ]);

        Assert.That(ordered.Select(node => node.Id), Is.EqualTo(new[] { "ordinary", "daily" }));
    }

    [Test]
    public void GlobalProjection_InProgressTraderFilterDoesNotAddFutureGraphContext()
    {
        var feed = Feed(
            [Node("active", "Prapor", "Any"), Node("future", "Prapor", "Any"), Node("other", "Therapist", "Any")],
            [Edge("active", "future", "Success")]);
        feed.DefaultVisibleQuestIds = ["active", "future", "other"];
        feed.AllApplicableQuestIds = ["active", "future", "other"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("active", "Started"), Quest("future", "AvailableForStart"), Quest("other", "Started")));

        var projection = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, null, "prapor",
                null, null, QuestRouteFilter.None));

        Assert.That(projection.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "active" }));
    }

    [Test]
    public void GlobalProjection_SelectedQuestRestoresApplicablePredecessorChainAcrossFilters()
    {
        var feed = Feed(
            [Node("root", "Prapor", "Any"), Node("middle", "Therapist", "Any"), Node("selected", "Skier", "Any"), Node("other", "Skier", "Any")],
            [Edge("root", "middle", "Success"), Edge("middle", "selected", "Success")]);
        feed.DefaultVisibleQuestIds = ["selected", "other"];
        feed.AllApplicableQuestIds = ["root", "middle", "selected", "other"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("root", "Success"),
            Quest("middle", "Success"),
            Quest("selected", "Started"),
            Quest("other", "Started")));

        var projection = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(
                GlobalQuestGraphMode.Full,
                false,
                true,
                false,
                null,
                "skier",
                "selected",
                null,
                QuestRouteFilter.None,
                "selected"));

        Assert.Multiple(() =>
        {
            Assert.That(projection.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "root", "middle", "selected" }));
            Assert.That(projection.Edges.Select(edge => (edge.SourceId, edge.TargetId)),
                Is.EqualTo(new[] { ("middle", "selected"), ("root", "middle") }));
        });
    }

    [Test]
    public void GlobalProjection_HideFinishedPreservesRestartableFailure()
    {
        var feed = Feed(
            [Node("done", "Prapor", "Any"), Node("retry", "Prapor", "Any"), Node("active", "Prapor", "Any")],
            []);
        feed.DefaultVisibleQuestIds = ["done", "retry", "active"];
        feed.AllApplicableQuestIds = ["done", "retry", "active"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("done", "Success"),
            Quest("retry", "FailRestartable"),
            Quest("active", "Started")));

        var projection = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, false, true, false, null, null, null, null, QuestRouteFilter.None));

        Assert.That(projection.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "retry", "active" }));
    }

    [Test]
    public void GlobalProjection_MatchesLevelFocusActiveAndRepeatableBandContract()
    {
        var root = Node("root", "Prapor", "Any");
        var selected = Node("selected", "Prapor", "Any");
        var gated = Node("gated", "Therapist", "Any");
        gated.EffectiveRequirements = [new QuestRequirementPayload { Kind = "Level", Compare = ">=", Value = 30 }];
        var daily = Node("daily", "Prapor", "Any");
        var feed = Feed([root, selected, gated], [Edge("root", "selected", "Success"), Edge("selected", "gated", "Success")]);
        feed.ProfileGeneratedQuests = [daily];
        feed.RepeatableKinds["daily"] = "Daily";
        feed.DefaultVisibleQuestIds = ["root", "selected", "gated", "daily"];
        feed.AllApplicableQuestIds = ["root", "selected", "gated", "daily"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, new LiveProfileSnapshot("profile", "USEC", 20,
            [Quest("root", "Success"), Quest("selected", "Started"), Quest("gated", "FailRestartable"), Quest("daily", "Started")], []));
        var layout = DeterministicGraphLayout.Build(topology);

        var level = GlobalQuestGraphProjectionBuilder.Build(topology, layout, overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, false, false, true, null, null, null, null, QuestRouteFilter.None));
        var focus = GlobalQuestGraphProjectionBuilder.Build(topology, layout, overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, false, true, true, null, "therapist", "no-match", "selected", QuestRouteFilter.None));
        var retry = GlobalQuestGraphProjectionBuilder.Build(topology, layout, overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, "RETRY", null, null, null, QuestRouteFilter.None));

        Assert.Multiple(() =>
        {
            Assert.That(level.Nodes.Select(node => node.Id), Does.Contain("gated"),
                "An active restartable failure is not a level-gated presentation state and must remain visible.");
            Assert.That(focus.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "root", "selected", "gated" }));
            Assert.That(retry.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "gated" }));
            Assert.That(level.NodesById["daily"].Y, Is.LessThan(level.NodesById["root"].Y));
            Assert.That(topology.NodesById["daily"].RepeatableKind, Is.EqualTo("Daily"));
        });
    }

    [TestCase(null, false, false, QuestDisplayStateKind.LockedFuture)]
    [TestCase("Started", true, false, QuestDisplayStateKind.Started)]
    [TestCase("Fail", true, true, QuestDisplayStateKind.FailRestartable)]
    [TestCase("AvailableAfter", true, false, QuestDisplayStateKind.AvailableAfter)]
    public void DisplayStateClassification_IsRuntimeNeutral(
        string? status,
        bool hasLiveQuest,
        bool restartable,
        QuestDisplayStateKind expected)
    {
        Assert.That(QuestGraphRules.ClassifyDisplayState(status, hasLiveQuest, restartable), Is.EqualTo(expected));
    }

    [Test]
    public void ProfileDisplayState_MatchesBrowserBlockerPriorityAndProgress()
    {
        var prerequisite = Node("gunsmith-1", "Mechanic", "Any");
        var gunsmith = Node("gunsmith-2", "Mechanic", "Any");
        gunsmith.EffectiveRequirements = [new QuestRequirementPayload { Kind = "Level", Compare = ">=", Value = 5 }];
        var lightkeeper = Node("lightkeeper", "Lightkeeper", "Any");
        var feed = Feed([prerequisite, gunsmith, lightkeeper], [Edge("gunsmith-1", "gunsmith-2", "Success")]);
        feed.DefaultVisibleQuestIds = ["gunsmith-1", "gunsmith-2", "lightkeeper"];
        feed.AllApplicableQuestIds = ["gunsmith-1", "gunsmith-2", "lightkeeper"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var progress = new[]
        {
            new QuestObjectiveProgress("one", true, 1, 1, true),
            new QuestObjectiveProgress("two", false, 1, 2, true),
        };
        var snapshot = new LiveProfileSnapshot("profile", "USEC", 20,
            [Quest("gunsmith-1", "Started"), Quest("gunsmith-2", "AvailableForStart"),
                new LiveQuestSnapshot("lightkeeper", "AvailableForStart", true, progress, null, false)],
            [new LiveTraderSnapshot("mechanic", true, 4, 1, 0),
                new LiveTraderSnapshot("lightkeeper", false, 1, 0, 0)]);
        var overlay = QuestOverlayBuilder.Build(topology, snapshot);

        Assert.Multiple(() =>
        {
            Assert.That(QuestGraphRules.ClassifyProfileDisplayState(topology, topology.NodesById["gunsmith-2"], overlay),
                Is.EqualTo(QuestMapDisplayStateKind.PrerequisiteGated));
            Assert.That(QuestGraphRules.ClassifyProfileDisplayState(topology, topology.NodesById["lightkeeper"], overlay),
                Is.EqualTo(QuestMapDisplayStateKind.TraderUnavailable));
            Assert.That(QuestGraphRules.CalculateObjectiveProgressPercent(progress), Is.EqualTo(75));
            Assert.That(QuestGraphRules.IsTerminalQuest(topology, "gunsmith-2"), Is.True);
            Assert.That(QuestGraphRules.IsTerminalQuest(topology, "gunsmith-1"), Is.False);
        });
    }

    [Test]
    public void LiveRaidState_OverridesStaleDynamicServerStateButRetainsServerGateAuthority()
    {
        var active = Node("active", "Prapor", "Any");
        var gated = Node("gated", "Lightkeeper", "Any");
        var feed = Feed([active, gated], []);
        feed.DefaultVisibleQuestIds = ["active", "gated"];
        feed.AllApplicableQuestIds = ["active", "gated"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var progress = new[]
        {
            new QuestObjectiveProgress("one", false, 1, 4, true),
            new QuestObjectiveProgress("two", true, 1, 1, true),
        };
        var overlay = QuestOverlayBuilder.Build(topology, new LiveProfileSnapshot(
            "profile",
            "USEC",
            60,
            [
                new LiveQuestSnapshot("active", "Started", true, progress, null, false),
                Quest("gated", "AvailableForStart"),
            ],
            [])) with
        {
            AuthoritativeDisplayStates = new Dictionary<string, QuestMapDisplayStateKind>(StringComparer.Ordinal)
            {
                ["active"] = QuestMapDisplayStateKind.Available,
                ["gated"] = QuestMapDisplayStateKind.TraderUnavailable,
            },
            AuthoritativeProgressPercentages = new Dictionary<string, double?>(StringComparer.Ordinal)
            {
                ["active"] = 0,
            },
        };

        Assert.Multiple(() =>
        {
            Assert.That(QuestGraphRules.ClassifyProfileDisplayState(topology, topology.NodesById["active"], overlay),
                Is.EqualTo(QuestMapDisplayStateKind.InProgress));
            Assert.That(QuestGraphRules.ResolveProfileProgressPercent("active", overlay), Is.EqualTo(62.5));
            Assert.That(QuestGraphRules.ClassifyProfileDisplayState(topology, topology.NodesById["gated"], overlay),
                Is.EqualTo(QuestMapDisplayStateKind.TraderUnavailable));
        });
    }

    [Test]
    public void ServerProfileProjection_IsAuthoritativeForStateVisibilityAndRepeatableLayout()
    {
        var lightkeeper = Node("lightkeeper", "Lightkeeper", "Any");
        var ordinary = Node("ordinary", "Prapor", "Any");
        var daily = Node("daily", "Prapor", "Any");
        var weekly = Node("weekly", "Therapist", "Any");
        var feed = Feed([lightkeeper, ordinary], []);
        feed.ProfileGeneratedQuests = [daily, weekly];
        feed.RepeatableKinds["daily"] = "Daily";
        feed.RepeatableKinds["weekly"] = "Weekly";
        feed.DefaultVisibleQuestIds = ["lightkeeper", "ordinary", "daily", "weekly"];
        feed.AllApplicableQuestIds = ["lightkeeper", "ordinary", "daily", "weekly"];
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, new LiveProfileSnapshot("profile", "USEC", 60,
            [Quest("lightkeeper", "AvailableForStart"), Quest("ordinary", "Started"), Quest("daily", "Started"), Quest("weekly", "Started")],
            [new LiveTraderSnapshot("lightkeeper", true, 4, 1, 0)])) with
        {
            AuthoritativeDisplayStates = new Dictionary<string, QuestMapDisplayStateKind>(StringComparer.Ordinal)
            {
                ["lightkeeper"] = QuestMapDisplayStateKind.TraderUnavailable,
                ["ordinary"] = QuestMapDisplayStateKind.InProgress,
                ["daily"] = QuestMapDisplayStateKind.InProgress,
                ["weekly"] = QuestMapDisplayStateKind.InProgress,
            },
            DefaultVisibleQuestIds = ["ordinary", "daily", "weekly"],
            ApplicableQuestIds = ["lightkeeper", "ordinary", "daily", "weekly"],
            RepeatableEndTimes = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["daily"] = 1000,
                ["weekly"] = 2000,
            },
        };

        var projection = GlobalQuestGraphProjectionBuilder.Build(topology, DeterministicGraphLayout.Build(topology), overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, false, false, false, null, null, null, null, QuestRouteFilter.None));

        Assert.Multiple(() =>
        {
            Assert.That(QuestGraphRules.ClassifyProfileDisplayState(topology, topology.NodesById["lightkeeper"], overlay),
                Is.EqualTo(QuestMapDisplayStateKind.TraderUnavailable));
            Assert.That(projection.Nodes.Select(node => node.Id), Does.Not.Contain("lightkeeper"));
            Assert.That(projection.NodesById["weekly"].X, Is.GreaterThan(projection.NodesById["daily"].X));
            Assert.That(overlay.RepeatableEndTimes["weekly"], Is.EqualTo(2000));
        });
    }

    [Test]
    public void TraderTasksProjection_IncludesActionableAndNonPrerequisiteGatesOnly()
    {
        var nodes = new[]
        {
            Node("ready", "Prapor", "Any"), Node("available", "Prapor", "Any"),
            Node("started", "Prapor", "Any"), Node("level", "Prapor", "Any"),
            Node("trader", "Prapor", "Any"), Node("prerequisite", "Prapor", "Any"),
            Node("multi-level", "Prapor", "Any"), Node("multi-unavailable", "Prapor", "Any"),
            Node("completed", "Prapor", "Any"), Node("other", "Therapist", "Any"),
        };
        var feed = Feed(nodes, []);
        feed.DefaultVisibleQuestIds = nodes.Select(node => node.Id).ToArray();
        feed.AllApplicableQuestIds = nodes.Select(node => node.Id).ToArray();
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("ready", "AvailableForFinish"), Quest("available", "AvailableForStart"),
            Quest("started", "Started"), Quest("completed", "Success"), Quest("other", "AvailableForStart"))) with
        {
            AuthoritativeDisplayStates = new Dictionary<string, QuestMapDisplayStateKind>(StringComparer.Ordinal)
            {
                ["level"] = QuestMapDisplayStateKind.LevelGated,
                ["trader"] = QuestMapDisplayStateKind.TraderGated,
                ["prerequisite"] = QuestMapDisplayStateKind.PrerequisiteGated,
                ["multi-level"] = QuestMapDisplayStateKind.LevelGated,
                ["multi-unavailable"] = QuestMapDisplayStateKind.TraderUnavailable,
            },
            PrerequisiteBlockerIds = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
            {
                ["prerequisite"] = ["blocked-by"],
                ["multi-level"] = ["blocked-by"],
                ["multi-unavailable"] = ["blocked-by"],
            },
            ApplicableQuestIds = nodes.Select(node => node.Id).ToArray(),
        };
        var layout = DeterministicGraphLayout.Build(topology);
        var projection = GlobalQuestGraphProjectionBuilder.Build(topology, layout, overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, null, "prapor", null, null,
                QuestRouteFilter.None, TraderTasksContext: true));
        var levelFiltered = GlobalQuestGraphProjectionBuilder.Build(topology, layout, overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, true, null, "prapor", null, null,
                QuestRouteFilter.None, TraderTasksContext: true));
        var unavailableHidden = GlobalQuestGraphProjectionBuilder.Build(topology, layout, overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.InProgress, false, false, false, null, "prapor", null, null,
                QuestRouteFilter.None, TraderTasksContext: true, HideUnavailableTraderTasks: true));

        Assert.Multiple(() =>
        {
            Assert.That(projection.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "ready", "available", "started", "level", "trader" }));
            Assert.That(levelFiltered.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "ready", "available", "started", "trader" }));
            Assert.That(unavailableHidden.Nodes.Select(node => node.Id), Is.EquivalentTo(new[] { "ready", "available", "started" }));
        });
    }

    [Test]
    public void TraderGraphContext_AddsOnlyDirectSuccessorsOfInProgressTraderQuests()
    {
        var nodes = new[]
        {
            Node("active", "Prapor", "Any"), Node("available", "Prapor", "Any"),
            Node("active-successor", "Therapist", "Any"), Node("available-successor", "Therapist", "Any"),
        };
        var feed = Feed(nodes,
        [
            Edge("active", "active-successor", "Success"),
            Edge("available", "available-successor", "Success"),
        ]);
        feed.DefaultVisibleQuestIds = nodes.Select(node => node.Id).ToArray();
        feed.AllApplicableQuestIds = nodes.Select(node => node.Id).ToArray();
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("active", "Started"), Quest("available", "AvailableForStart"))) with
        {
            ApplicableQuestIds = nodes.Select(node => node.Id).ToArray(),
            DefaultVisibleQuestIds = nodes.Select(node => node.Id).ToArray(),
        };

        var projection = GlobalQuestGraphProjectionBuilder.Build(topology, DeterministicGraphLayout.Build(topology), overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, false, false, false, null, "prapor", null, null,
                QuestRouteFilter.None, TraderGraphContext: true));

        Assert.That(projection.Nodes.Select(node => node.Id),
            Is.EquivalentTo(new[] { "active", "available", "active-successor" }));
    }

    [Test]
    public void RaidTrackedList_ScopesLocationsAndUsesNaturalTraderThenQuestOrder()
    {
        const string praporId = "54cb50c76803fa8b248b4571";
        const string therapistId = "54cb57776803fa99248b456e";
        var currentZulu = Node("current-zulu", "Prapor", "Any");
        currentZulu.Name = "Zulu";
        currentZulu.Location = new QuestLocationPayload { Id = "customs", Name = "Customs" };
        currentZulu.Objectives =
        [
            new QuestObjectivePayload { Id = "open", Text = "Open", Index = 0, RequiredValue = 5 },
            new QuestObjectivePayload { Id = "done", Text = "Done", Index = 1, RequiredValue = 1 },
        ];
        var transitAlpha = Node("transit-alpha", "Prapor", "Any");
        transitAlpha.Name = "Alpha";
        transitAlpha.Location = new QuestLocationPayload { Id = "marathon", Name = "Transition" };
        var anyBravo = Node("any-bravo", "Therapist", "Any");
        anyBravo.Name = "Bravo";
        var otherMap = Node("other-map", "Prapor", "Any");
        otherMap.Location = new QuestLocationPayload { Id = "woods", Name = "Woods" };
        var untracked = Node("untracked", "Prapor", "Any");
        untracked.Location = new QuestLocationPayload { Id = "customs", Name = "Customs" };
        var nodes = new[] { currentZulu, transitAlpha, anyBravo, otherMap, untracked };
        foreach (var node in new[] { currentZulu, transitAlpha, otherMap, untracked }) node.TraderId = praporId;
        anyBravo.TraderId = therapistId;
        var feed = Feed(nodes, []);
        feed.Topology.Traders =
        [
            new QuestTraderPayload { Id = therapistId, Name = "Therapist" },
            new QuestTraderPayload { Id = praporId, Name = "Prapor" },
        ];
        feed.DefaultVisibleQuestIds = nodes.Select(node => node.Id).ToArray();
        feed.AllApplicableQuestIds = nodes.Select(node => node.Id).ToArray();
        var topology = QuestTopologyNormalizer.Normalize(feed);
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            new LiveQuestSnapshot("current-zulu", "Started", true,
            [
                new QuestObjectiveProgress("open", false, 2, 5, true),
                new QuestObjectiveProgress("done", true, 1, 1, true),
            ], null, false),
            Quest("transit-alpha", "Started"), Quest("any-bravo", "AvailableForFinish"),
            Quest("other-map", "Started"), Quest("untracked", "Started"))) with
        {
            ApplicableQuestIds = nodes.Select(node => node.Id).ToArray(),
        };

        var projection = RaidTrackedQuestListProjectionBuilder.Build(
            topology,
            overlay,
            ["current-zulu", "transit-alpha", "any-bravo", "other-map"],
            ["any", "marathon", "customs"]);

        Assert.Multiple(() =>
        {
            Assert.That(projection.Groups.Select(group => group.Trader.Id),
                Is.EqualTo(new[] { praporId }));
            Assert.That(projection.Groups[0].Quests.Select(quest => quest.Quest.Id),
                Is.EqualTo(new[] { "transit-alpha", "current-zulu" }));
            Assert.That(projection.Groups.SelectMany(group => group.Quests).Select(quest => quest.Quest.Id),
                Does.Not.Contain("other-map"));
            Assert.That(projection.Groups.SelectMany(group => group.Quests).Select(quest => quest.Quest.Id),
                Does.Not.Contain("any-bravo"));
            Assert.That(projection.Groups[0].Quests[1].Objectives.Select(objective => objective.Definition.Id),
                Is.EqualTo(new[] { "open" }));
        });
    }

    [Test]
    public void GenericRules_MatchNormalizedTopologyRules()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [Node("root", "Prapor", "Any"), Node("next", "Prapor", "Any"), Node("later", "Prapor", "Any")],
            [Edge("root", "next", "Success"), Edge("next", "later", "Success")]));
        var dependencies = topology.Edges.Select(edge => new QuestDependency(edge.SourceId, edge.TargetId)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                QuestGraphRules.BuildPrerequisiteClosure("later", topology.NodesById.Keys, dependencies),
                Is.EquivalentTo(QuestGraphRules.BuildPrerequisiteClosure(topology, "later")));
            Assert.That(
                QuestGraphRules.BuildDefaultFrontier(Set("root"), Set("root"), Set("root", "next", "later"), dependencies),
                Is.EquivalentTo(QuestGraphRules.BuildDefaultFrontier(topology, Set("root"), Set("root"), Set("root", "next", "later"))));
            Assert.That(QuestGraphRules.Compare(3, 2, ">="), Is.True);
            Assert.That(QuestGraphRules.Compare(3, 3, "<"), Is.False);
        });
    }

    [Test]
    public void Selection_HighlightsRecursivePrerequisitesAndDirectSuccessorsOnly()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [Node("root", "Prapor", "Any"), Node("selected", "Prapor", "Any"), Node("direct", "Prapor", "Any"), Node("later", "Prapor", "Any")],
            [Edge("root", "selected", "Success"), Edge("selected", "direct", "Success"), Edge("direct", "later", "Success")]));

        var selection = QuestGraphRules.BuildSelection(topology, "selected");

        Assert.Multiple(() =>
        {
            Assert.That(selection.GetNodeHighlight("selected"), Is.EqualTo(QuestNodeHighlightKind.Selected));
            Assert.That(selection.GetNodeHighlight("root"), Is.EqualTo(QuestNodeHighlightKind.Prerequisite));
            Assert.That(selection.GetNodeHighlight("direct"), Is.EqualTo(QuestNodeHighlightKind.DirectSuccessor));
            Assert.That(selection.GetNodeHighlight("later"), Is.EqualTo(QuestNodeHighlightKind.None));
        });
    }

    [Test]
    public void SpatialIndex_CullsDeterministicallyAcrossTargetViewportSizes()
    {
        var positions = new Dictionary<string, QuestNodePosition>(StringComparer.Ordinal)
        {
            ["a"] = new("a", 0, 0, 0, 320, 112),
            ["b"] = new("b", 1, 900, 0, 320, 112),
            ["c"] = new("c", 2, 1800, 900, 320, 112),
            ["d"] = new("d", 3, 3000, 1300, 320, 112),
        };
        var index = new QuestGraphSpatialIndex(positions, 256);

        Assert.Multiple(() =>
        {
            Assert.That(index.Query(new QuestGraphRect(0, 0, 1920, 1080)), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(index.Query(new QuestGraphRect(0, 0, 2560, 1440)), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(index.Query(new QuestGraphRect(0, 0, 3440, 1440)), Is.EqualTo(new[] { "a", "b", "c", "d" }));
            Assert.That(index.Query(new QuestGraphRect(0, 0, 1920, 1080)), Is.EqualTo(index.Query(new QuestGraphRect(0, 0, 1920, 1080))));
        });
    }

    [Test]
    public void EdgeRoutes_AreDeterministicAndUseDistinctPorts()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [Node("source", "Prapor", "Any"), Node("upper", "Prapor", "Any"), Node("lower", "Prapor", "Any")],
            [Edge("source", "upper", "Success"), Edge("source", "lower", "Fail")]));
        var overlay = QuestOverlayBuilder.Build(topology, Snapshot(
            Quest("source", "Started"), Quest("upper", "Started"), Quest("lower", "Started")));
        var projection = GlobalQuestGraphProjectionBuilder.Build(
            topology,
            DeterministicGraphLayout.Build(topology),
            overlay,
            new GlobalQuestGraphOptions(GlobalQuestGraphMode.Full, true, false, false, null, null, null, null, QuestRouteFilter.None));

        var first = QuestEdgeRoutePlanner.Build(projection);
        var second = QuestEdgeRoutePlanner.Build(projection);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Has.Count.EqualTo(2));
            Assert.That(first[0].Start.Y, Is.Not.EqualTo(first[1].Start.Y));
        });
    }

    private static QuestTopologyFeed Feed(QuestNodePayload[] nodes, QuestEdgePayload[] edges) => new()
    {
        Topology = new QuestTopologyPayload
        {
            Version = "test",
            Quests = nodes,
            Edges = edges,
            Traders =
            [
                new QuestTraderPayload { Id = "prapor", Name = "Prapor" },
                new QuestTraderPayload { Id = "therapist", Name = "Therapist" },
            ],
        },
    };

    private static QuestNodePayload Node(string id, string traderName, string faction) => new()
    {
        Id = id,
        Name = id,
        TraderId = traderName.ToLowerInvariant(),
        TraderName = traderName,
        Faction = faction,
        Location = new QuestLocationPayload { Id = "any", Any = true },
    };

    private static QuestEdgePayload Edge(string source, string target, params string[] statuses) => new()
    {
        SourceId = source,
        TargetId = target,
        RequiredStatuses = statuses,
    };

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);

    private static LiveQuestSnapshot Quest(string id, string status) => new(id, status, true, [], null, status == "AvailableForFinish");

    private static LiveProfileSnapshot Snapshot(params LiveQuestSnapshot[] quests) =>
        new("profile", "USEC", 20, quests, []);
}
