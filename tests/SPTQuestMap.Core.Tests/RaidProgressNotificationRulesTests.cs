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
