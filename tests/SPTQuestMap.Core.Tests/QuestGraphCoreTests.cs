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
        });
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
