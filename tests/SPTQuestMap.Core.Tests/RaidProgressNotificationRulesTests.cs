using NUnit.Framework;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

[TestFixture]
public sealed class RaidProgressNotificationRulesTests
{
    [Test]
    public void SelectPrefersLeafObjectiveBeforeParentAggregate()
    {
        var node = Node(
            Objective("parent", 0),
            Objective("child", 1, parentId: "parent"));
        var previous = ProgressById(
            Progress("parent", 0, 2),
            Progress("child", 0, 1));
        var candidates = new[]
        {
            Progress("parent", 2, 2, complete: true),
            Progress("child", 1, 1, complete: true),
        };

        var selected = RaidProgressNotificationRules.Select(node, candidates, previous, null);

        Assert.That(selected.ObjectiveId, Is.EqualTo("child"));
    }

    [Test]
    public void SelectPrefersNumericProgressBeforeBooleanOnlyChange()
    {
        var node = Node(Objective("boolean", 1), Objective("numeric", 0));
        var previous = ProgressById(
            Progress("boolean", null, null),
            Progress("numeric", 1, 5));
        var candidates = new[]
        {
            Progress("boolean", null, null, complete: true),
            Progress("numeric", 2, 5),
        };

        var selected = RaidProgressNotificationRules.Select(node, candidates, previous, "boolean");

        Assert.That(selected.ObjectiveId, Is.EqualTo("numeric"));
    }

    [Test]
    public void SelectUsesPreferredObjectiveAfterProgressPrioritiesTie()
    {
        var node = Node(Objective("first", 0), Objective("preferred", 1));
        var previous = ProgressById(
            Progress("first", 0, 2),
            Progress("preferred", 0, 2));
        var candidates = new[]
        {
            Progress("first", 1, 2),
            Progress("preferred", 1, 2),
        };

        var selected = RaidProgressNotificationRules.Select(node, candidates, previous, "preferred");

        Assert.That(selected.ObjectiveId, Is.EqualTo("preferred"));
    }

    [Test]
    public void EffectiveIncreaseStopsAfterRequiredValueIsReached()
    {
        var previous = ProgressById(Progress("objective", 5, 5, complete: true));

        Assert.That(
            RaidProgressNotificationRules.HasEffectiveIncrease(
                Progress("objective", 6, 5, complete: true),
                previous),
            Is.False);
    }

    [Test]
    public void NotificationStateSuppressesARepeatedInventoryDerivedValue()
    {
        var state = new RaidProgressNotificationState();

        Assert.That(state.ObserveEffectiveIncrease(
            "quest", Progress("objective", 1, 3), Progress("objective", 0, 3)), Is.True);
        Assert.That(state.ObserveEffectiveIncrease(
            "quest", Progress("objective", 0, 3), Progress("objective", 1, 3)), Is.False);
        Assert.That(state.ObserveEffectiveIncrease(
            "quest", Progress("objective", 1, 3), Progress("objective", 0, 3)), Is.False);
        Assert.That(state.ObserveEffectiveIncrease(
            "quest", Progress("objective", 2, 3), Progress("objective", 1, 3)), Is.True);
    }

    [Test]
    public void NotificationStateAllowsProgressAfterAResettableCounterDecreases()
    {
        var state = new RaidProgressNotificationState();

        Assert.That(state.ObserveEffectiveIncrease(
            "quest", Progress("objective", 2, 3), Progress("objective", 0, 3), resetOnDecrease: true), Is.True);
        Assert.That(state.ObserveEffectiveIncrease(
            "quest", Progress("objective", 0, 3), Progress("objective", 2, 3), resetOnDecrease: true), Is.False);
        Assert.That(state.ObserveEffectiveIncrease(
            "quest", Progress("objective", 1, 3), Progress("objective", 0, 3), resetOnDecrease: true), Is.True);
    }

    [Test]
    public void NotificationStateClearStartsANewRaidHighWaterMark()
    {
        var state = new RaidProgressNotificationState();
        var previous = Progress("objective", 0, 3);
        var current = Progress("objective", 1, 3);

        Assert.That(state.ObserveEffectiveIncrease("quest", current, previous), Is.True);
        Assert.That(state.ObserveEffectiveIncrease("quest", current, previous), Is.False);

        state.Clear();

        Assert.That(state.ObserveEffectiveIncrease("quest", current, previous), Is.True);
    }

    [Test]
    public void KillDelayCoalescesProgressAndHoldsSeparateFinalStatus()
    {
        var queue = new RaidProgressNotificationQueue();
        var kill = Objective("kill", 0) with { IsKillObjective = true };
        var first = new RaidProgressNotification(Node(kill), kill, Progress("kill", 1, 2), "Started");
        Assert.That(queue.Schedule(first, 5, 10), Is.Null);
        Assert.That(queue.TryDequeue(14.99, out _), Is.False);
        var final = first with { Progress = Progress("kill", 2, 2, complete: true) };
        Assert.That(queue.Schedule(final, 5, 14), Is.Null);
        Assert.That(queue.Schedule(final with { Objective = null, Progress = null, ExactStatus = "AvailableForFinish" }, 5, 14.1), Is.Null);
        Assert.That(queue.TryDequeue(18.99, out _), Is.False);
        Assert.That(queue.TryDequeue(19, out var shown), Is.True);
        Assert.That(shown.Progress, Is.EqualTo(final.Progress));
        Assert.That(shown.ExactStatus, Is.EqualTo("AvailableForFinish"));
        Assert.That(queue.TryDequeue(30, out _), Is.False);
    }

    [Test]
    public void NonKillProgressIsImmediateWithoutDiscardingPendingKills()
    {
        var queue = new RaidProgressNotificationQueue();
        var kill = Objective("kill", 0) with { IsKillObjective = true };
        var item = Objective("item", 1);
        var change = new RaidProgressNotification(Node(kill, item), kill, Progress("kill", 1, 2), "Started");
        queue.Schedule(change, 5, 0);
        var itemChange = change with { Objective = item, Progress = Progress("item", 1, 2) };
        Assert.That(queue.Schedule(itemChange, 5, 1), Is.SameAs(itemChange));
        Assert.That(queue.TryDequeue(5, out var shown), Is.True);
        Assert.That(shown, Is.EqualTo(change));
    }

    [TestCase(0, 0)]
    [TestCase(-1, 0)]
    [TestCase(20, 10)]
    [TestCase(0.5, 0.5)]
    public void KillDelayHonorsZeroAndClampsRange(double setting, double expectedDelay)
    {
        var queue = new RaidProgressNotificationQueue();
        var kill = Objective("kill", 0) with { IsKillObjective = true };
        var change = new RaidProgressNotification(Node(kill), kill, Progress("kill", 1, 2), "Started");
        var immediate = queue.Schedule(change, setting, 10);
        if (expectedDelay == 0)
        {
            Assert.That(immediate, Is.SameAs(change));
            Assert.That(queue.TryDequeue(100, out _), Is.False);
        }
        else
        {
            Assert.That(immediate, Is.Null);
            Assert.That(queue.TryDequeue(10 + expectedDelay - 0.01, out _), Is.False);
            Assert.That(queue.TryDequeue(10 + expectedDelay, out _), Is.True);
        }
    }

    [Test]
    public void KillDelayKeepsQuestsIndependentAndClearsAtRaidEnd()
    {
        var queue = new RaidProgressNotificationQueue();
        var kill = Objective("kill", 0) with { IsKillObjective = true };
        var change = new RaidProgressNotification(Node(kill), kill, Progress("kill", 1, 2), "Started");
        queue.Schedule(change, 10, 0);
        queue.Schedule(change with { Quest = change.Quest with { Id = "other" } }, 1, 0);
        Assert.That(queue.TryDequeue(1, out var shown), Is.True);
        Assert.That(shown.Quest.Id, Is.EqualTo("other"));
        queue.Clear();
        Assert.That(queue.TryDequeue(100, out _), Is.False);
    }

    [Test]
    public void RepeatableTimerUsesServerEndWithLiveFallbackAndNoInventedDeadline()
    {
        var node = Node() with { RepeatableKind = "Weekly" };
        var live = new QuestLiveState(node.Id, "Started", true, true, [], 200, false);
        var overlay = new QuestProfileOverlay("profile", "Usec", 1,
            new Dictionary<string, QuestLiveState> { [node.Id] = live },
            new Dictionary<string, LiveTraderSnapshot>(), []);
        Assert.That(QuestRepeatableTimeRules.ExpirationTime(node, overlay), Is.EqualTo(200));
        overlay = overlay with { RepeatableEndTimes = new Dictionary<string, long> { [node.Id] = 300 } };
        Assert.That(QuestRepeatableTimeRules.ExpirationTime(node, overlay), Is.EqualTo(300));
        Assert.That(QuestRepeatableTimeRules.ExpirationTime(Node(), overlay), Is.Null);
        Assert.That(QuestRepeatableTimeRules.ExpirationTime(node with { Id = "missing" }, overlay), Is.Null);
        Assert.That(QuestRepeatableTimeRules.RemainingSeconds(300, 299), Is.EqualTo(1));
        Assert.That(QuestRepeatableTimeRules.RemainingSeconds(300, 300), Is.Zero);
        Assert.That(QuestRepeatableTimeRules.RemainingSeconds(300, 301), Is.Zero);
        Assert.That(QuestRepeatableTimeRules.RemainingSeconds(7 * 86400, 0), Is.EqualTo(604800));
    }

    private static QuestGraphNode Node(params QuestObjectiveDefinition[] objectives) => new(
        "quest",
        "Quest",
        "Description",
        "prapor",
        "Prapor",
        "/files/trader/avatar/prapor.png",
        "/files/quest/icon/quest.png",
        "Any",
        new QuestLocation("any", null, true, null),
        null,
        false,
        [],
        [],
        objectives,
        [],
        [],
        [],
        false,
        null);

    private static QuestObjectiveDefinition Objective(string id, int index, string? parentId = null) =>
        new(id, id, "CounterCreator", index, parentId, null, null, [], []);

    private static QuestObjectiveProgress Progress(
        string id,
        double? current,
        double? required,
        bool complete = false) =>
        new(id, complete, current, required, current.HasValue || required.HasValue);

    private static IReadOnlyDictionary<string, QuestObjectiveProgress> ProgressById(
        params QuestObjectiveProgress[] progress) =>
        progress.ToDictionary(item => item.ObjectiveId, StringComparer.Ordinal);
}
