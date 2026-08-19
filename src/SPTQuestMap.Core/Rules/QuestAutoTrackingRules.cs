namespace SPTQuestMap.Core.Rules;

public static class QuestAutoTrackingRules
{
    private static readonly HashSet<string> OutOfRaidObjectiveTypes = new(StringComparer.Ordinal)
    {
        "GlobalVariableValue",
        "HandoverItem",
        "HideoutArea",
        "Quest",
        "SellItemToTrader",
        "Skill",
        "TraderLoyalty",
        "TraderStanding",
        "WeaponAssembly",
    };

    public static bool ShouldAutoTrackNewQuest(bool anyLocation, IEnumerable<bool> objectiveRaidRelevance)
    {
        if (!anyLocation) return true;
        return objectiveRaidRelevance.Any(inRaidRelevant => inRaidRelevant);
    }

    public static bool IsInRaidObjectiveType(string objectiveType) =>
        !OutOfRaidObjectiveTypes.Contains(objectiveType);
}
