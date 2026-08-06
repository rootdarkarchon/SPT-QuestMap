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
            Assert.That(QuestGraphRules.FilterActive(topology, overlay).Select(node => node.Id), Is.EqualTo(new[] { "active" }));
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
    public void TraderProjection_CompactsRanksAndKeepsOnlyInternalEdges()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed(
            [
                Node("p0", "Prapor", "Any"),
                Node("therapist", "Therapist", "Any"),
                Node("p2", "Prapor", "Any"),
                Node("p3", "Prapor", "Any"),
            ],
            [
                Edge("p0", "therapist", "Success"),
                Edge("therapist", "p2", "Success"),
                Edge("p2", "p3", "Success"),
            ]));
        var layout = DeterministicGraphLayout.Build(topology);

        var projection = TraderGraphProjectionBuilder.Build(topology, layout, "prapor");

        Assert.Multiple(() =>
        {
            Assert.That(projection.Nodes.Select(node => node.Id), Is.EqualTo(new[] { "p0", "p2", "p3" }));
            Assert.That(projection.Edges.Select(edge => (edge.SourceId, edge.TargetId)), Is.EqualTo(new[] { ("p2", "p3") }));
            Assert.That(projection.NodesById["p0"].X, Is.EqualTo(0));
            Assert.That(projection.NodesById["p2"].X, Is.EqualTo(DeterministicGraphLayout.NodeWidth + DeterministicGraphLayout.LayerGap));
            Assert.That(projection.NodesById["p3"].X, Is.EqualTo(2 * (DeterministicGraphLayout.NodeWidth + DeterministicGraphLayout.LayerGap)));
        });
    }

    [Test]
    public void OverlayRefresh_DoesNotReplaceTopologyOrLayout()
    {
        var topology = QuestTopologyNormalizer.Normalize(Feed([Node("a", "Prapor", "Any")], []));
        var data = new QuestGraphDataSet(topology);
        var originalLayout = data.Layout;

        var first = data.RefreshOverlay(Snapshot(Quest("a", "Started")));
        var second = data.RefreshOverlay(Snapshot(Quest("a", "Success")));

        Assert.Multiple(() =>
        {
            Assert.That(data.Topology, Is.SameAs(topology));
            Assert.That(data.Layout, Is.SameAs(originalLayout));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.QuestsById["a"].ExactStatus, Is.EqualTo("Success"));
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
        var projection = TraderGraphProjectionBuilder.Build(topology, DeterministicGraphLayout.Build(topology), "prapor");

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
