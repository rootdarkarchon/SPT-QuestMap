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
                    StringComparison.OrdinalIgnoreCase))
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
        node.ActualMaps.Length > 0;
}
