using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestProgressRules
{
    public static bool HasEffectiveIncrease(
        QuestObjectiveProgress current,
        QuestObjectiveProgress? previous)
    {
        if (previous is null) return EffectiveValue(current) > 0d || current.Complete;
        if (current.Complete && !previous.Complete) return true;
        return EffectiveValue(current) - EffectiveValue(previous) > 0.0001d;
    }

    public static double EffectiveValue(QuestObjectiveProgress progress)
    {
        var current = progress.Current.GetValueOrDefault();
        return progress.Required.HasValue ? Math.Min(current, progress.Required.Value) : current;
    }
}
