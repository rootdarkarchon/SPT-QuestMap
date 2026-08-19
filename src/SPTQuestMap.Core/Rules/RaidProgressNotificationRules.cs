using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class RaidProgressNotificationRules
{
    public static bool HasEffectiveIncrease(
        QuestObjectiveProgress current,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById)
    {
        previousById.TryGetValue(current.ObjectiveId, out var previous);
        return QuestProgressRules.HasEffectiveIncrease(current, previous);
    }

    public static QuestObjectiveProgress Select(
        QuestGraphNode node,
        IReadOnlyCollection<QuestObjectiveProgress> candidates,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById,
        string? preferredObjectiveId)
    {
        if (candidates.Count == 0)
            throw new ArgumentException("At least one notification candidate is required.", nameof(candidates));

        return candidates
            .OrderByDescending(candidate => IsLeafObjective(node, candidate.ObjectiveId))
            .ThenByDescending(candidate => NumericProgressChanged(candidate, previousById))
            .ThenByDescending(candidate => EffectivePositiveProgressDelta(candidate, previousById))
            .ThenBy(candidate => candidate.Complete)
            .ThenByDescending(candidate => string.Equals(
                candidate.ObjectiveId,
                preferredObjectiveId,
                StringComparison.Ordinal))
            .ThenByDescending(candidate => ObjectiveIndex(node, candidate.ObjectiveId))
            .First();
    }

    public static bool IsLeafObjective(QuestGraphNode node, string objectiveId) =>
        !node.Objectives.Any(objective =>
            string.Equals(objective.ParentId, objectiveId, StringComparison.Ordinal)
            || objective.DependsOn.Contains(objectiveId, StringComparer.Ordinal));

    public static bool NumericProgressChanged(
        QuestObjectiveProgress current,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById) =>
        current.Current.HasValue
        && (!previousById.TryGetValue(current.ObjectiveId, out var previous)
            || !previous.Current.HasValue
            || Math.Abs(current.Current.Value - previous.Current.Value) > 0.0001d);

    public static double EffectivePositiveProgressDelta(
        QuestObjectiveProgress current,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById) =>
        Math.Max(0d, QuestProgressRules.EffectiveValue(current)
            - (previousById.TryGetValue(current.ObjectiveId, out var previous)
                ? QuestProgressRules.EffectiveValue(previous)
                : 0d));

    public static double NumericDelta(
        QuestObjectiveProgress current,
        IReadOnlyDictionary<string, QuestObjectiveProgress> previousById) =>
        current.Current.GetValueOrDefault()
        - (previousById.TryGetValue(current.ObjectiveId, out var previous)
            ? previous.Current.GetValueOrDefault()
            : 0d);

    public static int ObjectiveIndex(QuestGraphNode node, string objectiveId) =>
        node.Objectives.FirstOrDefault(objective =>
            string.Equals(objective.Id, objectiveId, StringComparison.Ordinal))?.Index ?? int.MinValue;
}
