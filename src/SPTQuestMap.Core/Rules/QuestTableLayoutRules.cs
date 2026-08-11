namespace SPTQuestMap.Core.Rules;

public static class QuestTableLayoutRules
{
    public const float QuestBannerActionHeight = 24f;
    public const float RepeatableBadgeHeight = QuestBannerActionHeight;

    private const float QuestBannerRightPadding = 8f;
    private const float MaximumRouteStripInset = 18f;

    public static float QuestBannerActionRightOffset(bool collectorRoute, bool lightkeeperRoute)
    {
        // Route membership controls only which strips are painted. Reserving the
        // maximum two-strip footprint keeps every row action on one vertical axis.
        _ = collectorRoute;
        _ = lightkeeperRoute;
        return QuestBannerRightPadding + MaximumRouteStripInset;
    }
}
