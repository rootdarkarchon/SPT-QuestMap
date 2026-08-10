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
        return objectiveTypes.Any(AnyLocationInRaidObjectiveTypes.Contains);
    }
}
