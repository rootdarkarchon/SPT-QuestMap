using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class RaidObjectiveLocationRules
{
    public static bool IsActiveOnMap(
        QuestGraphNode quest,
        QuestObjectiveDefinition objective,
        ISet<string> currentMapZoneIds)
    {
        if (!quest.Location.Any || objective.ZoneIds.Count == 0) return true;
        return objective.ZoneIds.Any(currentMapZoneIds.Contains);
    }
}
