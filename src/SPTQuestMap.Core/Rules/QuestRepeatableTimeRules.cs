using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestRepeatableTimeRules
{
    public static long? ExpirationTime(QuestGraphNode node, QuestProfileOverlay overlay)
    {
        if (node.RepeatableKind is not ("Daily" or "Weekly")) return null;
        if (overlay.RepeatableEndTimes.TryGetValue(node.Id, out var serverEnd) && serverEnd > 0)
            return serverEnd;
        return overlay.QuestsById.TryGetValue(node.Id, out var live) && live.ExpirationTime is > 0
            ? live.ExpirationTime : null;
    }

    public static long RemainingSeconds(long expirationTime, long now) => Math.Max(0, expirationTime - now);
}
