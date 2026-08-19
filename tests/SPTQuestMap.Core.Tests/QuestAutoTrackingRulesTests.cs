using NUnit.Framework;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Tests;

[TestFixture]
public sealed class QuestAutoTrackingRulesTests
{
    [TestCase("CounterCreator")]
    [TestCase("FindItem")]
    [TestCase("LeaveItemAtLocation")]
    [TestCase("PlaceBeacon")]
    [TestCase("VisitPlace")]
    [TestCase("CustomFutureObjective")]
    public void AnyLocation_InRaidObjective_IsTracked(string objectiveType)
    {
        Assert.That(QuestAutoTrackingRules.ShouldAutoTrackNewQuest(
            true, [QuestAutoTrackingRules.IsInRaidObjectiveType(objectiveType)]), Is.True);
    }

    [TestCase("GlobalVariableValue")]
    [TestCase("HandoverItem")]
    [TestCase("HideoutArea")]
    [TestCase("Quest")]
    [TestCase("SellItemToTrader")]
    [TestCase("Skill")]
    [TestCase("TraderLoyalty")]
    [TestCase("TraderStanding")]
    [TestCase("WeaponAssembly")]
    public void AnyLocation_PassiveOnlyObjective_IsNotTracked(string objectiveType)
    {
        Assert.That(QuestAutoTrackingRules.ShouldAutoTrackNewQuest(
            true, [QuestAutoTrackingRules.IsInRaidObjectiveType(objectiveType)]), Is.False);
    }

    [Test]
    public void AnyLocation_MixedObjectives_IsTrackedWhenOneIsInRaid()
    {
        Assert.That(QuestAutoTrackingRules.ShouldAutoTrackNewQuest(
            true,
            [false, false, true]), Is.True);
    }

    [Test]
    public void AnyLocation_NoObjectives_IsNotTracked()
    {
        Assert.That(QuestAutoTrackingRules.ShouldAutoTrackNewQuest(true, []), Is.False);
    }

    [Test]
    public void SpecificLocation_PreservesExistingAutoTrackingBehavior()
    {
        Assert.That(QuestAutoTrackingRules.ShouldAutoTrackNewQuest(false, [false]), Is.True);
    }

    [TestCase("CounterCreator", true)]
    [TestCase("FindItem", true)]
    [TestCase("HandoverItem", false)]
    [TestCase("WeaponAssembly", false)]
    [TestCase("CustomFutureObjective", true)]
    public void SmartInRaidTracking_UsesSharedObjectiveDefinitions(string objectiveType, bool expected)
    {
        Assert.That(QuestAutoTrackingRules.IsInRaidObjectiveType(objectiveType), Is.EqualTo(expected));
    }
}
