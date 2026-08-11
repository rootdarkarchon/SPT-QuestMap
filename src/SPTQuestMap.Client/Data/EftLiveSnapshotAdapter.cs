using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Client.Data;

internal static class EftLiveSnapshotAdapter
{
    public static LiveProfileSnapshot Capture(IEnumerable<QuestClass> liveQuests, Profile profile)
    {
        if (liveQuests is null) throw new ArgumentNullException(nameof(liveQuests));
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        var quests = liveQuests
            .Select(CaptureQuest)
            .OrderBy(quest => quest.QuestId, StringComparer.Ordinal)
            .ToArray();
        var traders = profile.TradersInfo
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new LiveTraderSnapshot(
                pair.Key.ToString(),
                pair.Value.Available,
                pair.Value.LoyaltyLevel,
                pair.Value.Standing,
                pair.Value.SalesSum))
            .ToArray();

        return new LiveProfileSnapshot(
            profile.ProfileId,
            profile.Side.ToString(),
            profile.Info.Level,
            quests,
            traders)
        {
            PrestigeLevel = profile.Info.PrestigeLevel,
        };
    }

    internal static LiveQuestSnapshot CaptureQuest(QuestClass quest)
    {
        var objectiveProgress = (quest.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finishConditions)
                ? finishConditions
                : Enumerable.Empty<Condition>())
            .GroupBy(condition => condition.id.ToString(), StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(condition => condition.index)
            .ThenBy(condition => condition.id.ToString(), StringComparer.Ordinal)
            .Select(condition =>
            {
                var complete = quest.CompletedConditions.Contains(condition.id);
                var progressKnown = quest.ProgressCheckers.TryGetValue(condition, out var checker);
                var current = progressKnown ? checker.CurrentValue : complete ? condition.value : (double?)null;
                return new QuestObjectiveProgress(
                    condition.id.ToString(),
                    complete,
                    current,
                    condition.value,
                    progressKnown || complete);
            })
            .ToArray();
        var expiration = quest is GClass3996 repeatable ? repeatable.ExpirationDate : (long?)null;

        return new LiveQuestSnapshot(
            quest.Id,
            quest.QuestStatus.ToString(),
            quest.IsVisible,
            objectiveProgress,
            expiration,
            quest.QuestStatus == EQuestStatus.AvailableForFinish);
    }
}
