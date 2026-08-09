using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestProgressRules
{
    public static bool IsComplete(
        bool conditionRecorded,
        double? current,
        double? required,
        string? compare) =>
        conditionRecorded
        || (current.HasValue && required.HasValue
            && QuestGraphRules.Compare(current.Value, required.Value, compare ?? ">="));

    public static double? CapCurrent(double? current, double? required) =>
        current.HasValue && required.HasValue && current.Value > required.Value ? required : current;

    public static double? CalculatePercent(double? current, double? required)
    {
        if (!current.HasValue || required is not > 0d) return null;
        return Math.Clamp(current.Value / required.Value * 100d, 0d, 100d);
    }

    public static double? CalculateObjectiveProgressPercent<T>(
        IReadOnlyCollection<T> objectives,
        Func<T, bool> isComplete,
        Func<T, double?> current,
        Func<T, double?> required)
    {
        if (objectives.Count == 0) return null;
        var completedShare = objectives.Sum(objective =>
        {
            if (isComplete(objective)) return 1d;
            var objectiveCurrent = current(objective);
            var objectiveRequired = required(objective);
            return objectiveCurrent.HasValue && objectiveRequired is > 0d
                ? Math.Clamp(objectiveCurrent.Value / objectiveRequired.Value, 0d, 1d)
                : 0d;
        });
        return Math.Round(completedShare / objectives.Count * 100d, 1, MidpointRounding.AwayFromZero);
    }

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
        return CapCurrent(progress.Current, progress.Required).GetValueOrDefault();
    }
}
