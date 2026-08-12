using SPTQuestMap.Core.Rules;
using SPTQuestMap.Services;

namespace SPTQuestMap.Presentation;

internal static class QuestMapLocationPresentation
{
    public static QuestMapReferenceDto[] DisplayMaps(QuestNodeDto node, string anyLocationName)
    {
        if (UsesActualMaps(node))
        {
            if (QuestObjectiveMapRules.ShouldPrependTransitionLabel(
                    node.Location.Id,
                    node.Location.Name,
                    node.Location.Any,
                    node.ActualMaps.Length))
            {
                return
                [
                    NativeLocation(node, anyLocationName),
                    .. node.ActualMaps,
                ];
            }

            return node.ActualMaps;
        }

        return [NativeLocation(node, anyLocationName)];
    }

    private static bool UsesActualMaps(QuestNodeDto node) =>
        (node.Location.Any || QuestObjectiveMapRules.IsTransitionLocation(
            node.Location.Id,
            node.Location.Name,
            node.Location.Any))
        && node.ActualMapsComplete
        && node.ActualMaps.Length > 0;

    private static QuestMapReferenceDto NativeLocation(QuestNodeDto node, string anyLocationName) => new(
        node.Location.Id,
        node.Location.Any ? anyLocationName : node.Location.Name ?? node.Location.Id,
        node.Location.BannerImageUrl);
}
