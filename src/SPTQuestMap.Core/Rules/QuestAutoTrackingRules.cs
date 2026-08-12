namespace SPTQuestMap.Core.Rules;

public static class QuestAutoTrackingRules
{
    private static readonly HashSet<string> AnyLocationInRaidObjectiveTypes = new(StringComparer.Ordinal)
    {
        "CounterCreator",
        "FindItem",
        "LeaveItemAtLocation",
        "PlaceBeacon",
        "VisitPlace",
    };

    public static bool ShouldAutoTrackNewQuest(bool anyLocation, IEnumerable<string> objectiveTypes)
    {
        if (!anyLocation) return true;
        return objectiveTypes.Any(IsInRaidObjectiveType);
    }

    public static bool IsInRaidObjectiveType(string objectiveType) =>
        AnyLocationInRaidObjectiveTypes.Contains(objectiveType);
}
