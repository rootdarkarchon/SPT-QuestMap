using SPTQuestMap.Core.Rules;
using SPTQuestMap.Services;

namespace SPTQuestMap.Presentation;

internal static class QuestMapLocationPresentation
{
    public static QuestMapReferenceDto[] DisplayMaps(QuestNodeDto node)
    {
        if (UsesActualMaps(node))
        {
            if (node.TaskLocation.Id.Equals(
                    QuestObjectiveMapRules.TransitionFilterId,
                    StringComparison.OrdinalIgnoreCase)
                && node.ActualMaps.Length > 1)
            {
                return
                [
                    node.TaskLocation,
                    .. node.ActualMaps,
                ];
            }

            return node.ActualMaps;
        }

        return [node.TaskLocation];
    }

    private static bool UsesActualMaps(QuestNodeDto node) =>
        (node.TaskLocation.Id.Equals(QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase)
            || node.TaskLocation.Id.Equals(QuestObjectiveMapRules.TransitionFilterId, StringComparison.OrdinalIgnoreCase))
        && node.ActualMapsComplete
        && node.ActualMaps.Length > 0;
}
