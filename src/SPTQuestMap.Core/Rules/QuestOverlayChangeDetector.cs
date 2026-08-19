using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestOverlayChangeDetector
{
    public static string[] FindChangedQuestIds(QuestProfileOverlay previous, QuestProfileOverlay current) =>
        previous.QuestsById.Keys
            .Concat(current.QuestsById.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(questId => HasQuestChanged(previous, current, questId))
            .OrderBy(questId => questId, StringComparer.Ordinal)
            .ToArray();

    public static bool HasQuestChanged(
        QuestProfileOverlay previous,
        QuestProfileOverlay current,
        string questId)
    {
        var hasPrevious = previous.QuestsById.TryGetValue(questId, out var previousState);
        var hasCurrent = current.QuestsById.TryGetValue(questId, out var currentState);
        if (!hasPrevious || !hasCurrent) return true;
        if (previous.AuthoritativeDisplayStates.GetValueOrDefault(questId)
                != current.AuthoritativeDisplayStates.GetValueOrDefault(questId)
            || previous.AuthoritativeProgressPercentages.GetValueOrDefault(questId)
                != current.AuthoritativeProgressPercentages.GetValueOrDefault(questId)
            || previous.RepeatableEndTimes.GetValueOrDefault(questId)
                != current.RepeatableEndTimes.GetValueOrDefault(questId)
            || previous.DefaultVisibleQuestIds.Contains(questId, StringComparer.Ordinal)
                != current.DefaultVisibleQuestIds.Contains(questId, StringComparer.Ordinal)
            || previous.ApplicableQuestIds.Contains(questId, StringComparer.Ordinal)
                != current.ApplicableQuestIds.Contains(questId, StringComparer.Ordinal)
            || !previous.PrerequisiteBlockerIds.GetValueOrDefault(questId, Array.Empty<string>())
                .SequenceEqual(
                    current.PrerequisiteBlockerIds.GetValueOrDefault(questId, Array.Empty<string>()),
                    StringComparer.Ordinal)) return true;
        if (previousState!.HasLiveQuest != currentState!.HasLiveQuest
            || previousState.ExactStatus != currentState.ExactStatus
            || previousState.Visible != currentState.Visible
            || previousState.ExpirationTime != currentState.ExpirationTime
            || previousState.HandoverReady != currentState.HandoverReady
            || previousState.Objectives.Count != currentState.Objectives.Count) return true;

        for (var index = 0; index < previousState.Objectives.Count; index++)
        {
            if (previousState.Objectives[index] != currentState.Objectives[index]) return true;
        }
        return false;
    }
}
