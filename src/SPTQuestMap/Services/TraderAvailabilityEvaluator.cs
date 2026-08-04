using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace SPTQuestMap.Services;

internal sealed class TraderAvailabilityEvaluator(
    Dictionary<MongoId, TraderInfo> traders,
    IReadOnlyDictionary<string, string> questStatuses
)
{
    internal bool IsAvailable(MongoId traderId)
    {
        if (!traders.TryGetValue(traderId, out var trader) || trader.Unlocked != true || trader.Disabled == true)
        {
            return false;
        }

        if (traderId == Traders.JAEGER) return QuestSucceeded(QuestMapQuestIds.Introduction);
        if (traderId == Traders.REF) return QuestSucceeded(QuestMapQuestIds.RefPveUnlock);
        if (traderId == Traders.LIGHTHOUSEKEEPER) return QuestSucceeded(QuestMapQuestIds.KnockKnock);
        return true;
    }

    private bool QuestSucceeded(string questId) =>
        questStatuses.TryGetValue(questId, out var status) && status == nameof(QuestStatusEnum.Success);
}
