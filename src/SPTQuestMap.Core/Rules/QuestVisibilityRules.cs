using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public sealed record QuestVisibilityNode(
    string Id,
    string Name,
    string TraderId,
    string TraderName,
    bool Restartable,
    bool Repeatable);

public sealed record QuestVisibilityState(
    string QuestId,
    QuestMapDisplayStateKind DisplayState,
    IReadOnlyCollection<string>? PrerequisiteBlockerIds = null);

public sealed record QuestVisibilityOptions(
    bool ShowAllFuture,
    bool ShowFinished,
    bool LevelEligibleOnly,
    string? TraderId,
    string? Search,
    string? SelectedQuestId,
    string? FocusQuestId,
    bool ShowRepeatables,
    bool TraderInProgressSuccessorsOnly = false,
    bool IncludeTraderPrerequisiteBlockers = true);

public static class QuestVisibilityRules
{
    public static HashSet<string> BuildVisibleIds(
        IReadOnlyCollection<QuestVisibilityNode> nodes,
        IReadOnlyCollection<QuestDependency> edges,
        IReadOnlyCollection<QuestVisibilityState> states,
        IReadOnlyCollection<string> defaultVisibleIds,
        IReadOnlyCollection<string> applicableIds,
        QuestVisibilityOptions options)
    {
        var nodeById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var stateById = states.GroupBy(state => state.QuestId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var applicable = applicableIds.Where(nodeById.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var incoming = edges.Where(edge => applicable.Contains(edge.SourceId) && applicable.Contains(edge.TargetId))
            .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var outgoing = edges.Where(edge => applicable.Contains(edge.SourceId) && applicable.Contains(edge.TargetId))
            .GroupBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.TargetId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        var selectedPrerequisites = Closure(options.SelectedQuestId);
        selectedPrerequisites.Remove(options.SelectedQuestId ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(options.FocusQuestId) && applicable.Contains(options.FocusQuestId!))
        {
            var focused = Closure(options.FocusQuestId);
            if (outgoing.TryGetValue(options.FocusQuestId!, out var successors)) focused.UnionWith(successors);
            return focused;
        }

        var visible = (options.ShowAllFuture ? applicable : defaultVisibleIds.Where(applicable.Contains))
            .Where(id => nodeById.TryGetValue(id, out var node) && (!node.Repeatable || options.ShowRepeatables))
            .ToHashSet(StringComparer.Ordinal);
        if (options.ShowRepeatables)
        {
            visible.UnionWith(nodes.Where(node => node.Repeatable && applicable.Contains(node.Id)).Select(node => node.Id));
        }

        if (!options.ShowFinished)
        {
            visible.RemoveWhere(id => id != options.SelectedQuestId && !selectedPrerequisites.Contains(id)
                && nodeById.TryGetValue(id, out var node)
                && stateById.TryGetValue(id, out var state)
                && QuestGraphRules.IsFinishedForFilter(state.DisplayState, node.Restartable));
        }
        if (options.LevelEligibleOnly)
        {
            visible.RemoveWhere(id => id != options.SelectedQuestId
                && stateById.TryGetValue(id, out var state)
                && QuestGraphRules.IsLevelGatedForFilter(state.DisplayState));
        }

        var search = options.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            visible.RemoveWhere(id => id != options.SelectedQuestId && !selectedPrerequisites.Contains(id)
                && (!nodeById.TryGetValue(id, out var node)
                    || !$"{node.Name} {node.TraderName} {node.Id}".Contains(search!, StringComparison.OrdinalIgnoreCase)));
        }

        var traderId = options.TraderId?.Trim();
        if (!string.IsNullOrWhiteSpace(traderId))
        {
            visible.RemoveWhere(id => id != options.SelectedQuestId && !selectedPrerequisites.Contains(id)
                && (!nodeById.TryGetValue(id, out var node) || !string.Equals(node.TraderId, traderId, StringComparison.Ordinal)));
            var boundaries = visible.Where(id => nodeById.TryGetValue(id, out var node)
                && string.Equals(node.TraderId, traderId, StringComparison.Ordinal)
                && stateById.TryGetValue(id, out var state)
                && (options.TraderInProgressSuccessorsOnly
                    ? state.DisplayState == QuestMapDisplayStateKind.InProgress
                    : QuestGraphRules.IsTraderBoundaryForFilter(state.DisplayState))).ToArray();
            foreach (var source in boundaries)
            {
                if (!outgoing.TryGetValue(source, out var successors)) continue;
                foreach (var successor in successors)
                {
                    if (PassesFinishedAndLevel(successor)) visible.Add(successor);
                }
            }
            foreach (var blocked in options.IncludeTraderPrerequisiteBlockers
                         ? visible.Where(id => stateById.TryGetValue(id, out var state)
                             && state.PrerequisiteBlockerIds is { Count: > 0 }).ToArray()
                         : Array.Empty<string>())
            {
                var blockers = stateById[blocked].PrerequisiteBlockerIds!;
                if (!incoming.TryGetValue(blocked, out var prerequisites)) continue;
                visible.UnionWith(prerequisites.Where(blockers.Contains));
            }
        }

        // Selection is context, not a destructive filter. Always restore the
        // complete applicable predecessor closure after presentation filters.
        visible.UnionWith(selectedPrerequisites);
        if (!string.IsNullOrWhiteSpace(options.SelectedQuestId) && applicable.Contains(options.SelectedQuestId!))
            visible.Add(options.SelectedQuestId!);
        return visible;

        bool PassesFinishedAndLevel(string id)
        {
            if (!nodeById.TryGetValue(id, out var node)) return false;
            if (!stateById.TryGetValue(id, out var state)) return true;
            if (!options.ShowFinished && QuestGraphRules.IsFinishedForFilter(state.DisplayState, node.Restartable)) return false;
            return !options.LevelEligibleOnly || !QuestGraphRules.IsLevelGatedForFilter(state.DisplayState);
        }

        HashSet<string> Closure(string? questId)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(questId) || !applicable.Contains(questId!)) return result;
            var queue = new Queue<string>();
            result.Add(questId!);
            queue.Enqueue(questId!);
            while (queue.TryDequeue(out var current))
            {
                if (!incoming.TryGetValue(current, out var prerequisites)) continue;
                foreach (var prerequisite in prerequisites)
                {
                    if (result.Add(prerequisite)) queue.Enqueue(prerequisite);
                }
            }
            return result;
        }
    }
}
