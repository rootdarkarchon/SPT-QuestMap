using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public static class QuestGraphRules
{
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.Ordinal)
    {
        "Started",
        "AvailableForFinish",
        "MarkedAsFailed",
        "FailRestartable",
    };

    private static readonly HashSet<string> FinishedStatuses = new(StringComparer.Ordinal)
    {
        "Success",
        "Fail",
    };

    public static QuestEdgeRequirementKind ClassifyEdgeRequirement(IEnumerable<string> requiredStatuses)
    {
        var statuses = requiredStatuses.ToHashSet(StringComparer.Ordinal);
        var hasStarted = statuses.Contains("Started");
        var hasSuccess = statuses.Contains("Success");
        var hasFailure = statuses.Contains("Fail")
            || statuses.Contains("FailRestartable")
            || statuses.Contains("MarkedAsFailed");

        if (hasStarted) return QuestEdgeRequirementKind.Started;
        if (hasSuccess && hasFailure) return QuestEdgeRequirementKind.AnyOutcome;
        if (hasFailure) return QuestEdgeRequirementKind.Failure;
        if (hasSuccess) return QuestEdgeRequirementKind.Success;
        return QuestEdgeRequirementKind.Unknown;
    }

    public static QuestDisplayStateKind ClassifyDisplayState(string? exactStatus, bool hasLiveQuest, bool restartable)
    {
        if (!hasLiveQuest) return QuestDisplayStateKind.LockedFuture;
        return exactStatus switch
        {
            "AvailableForStart" => QuestDisplayStateKind.AvailableForStart,
            "Started" => QuestDisplayStateKind.Started,
            "AvailableForFinish" => QuestDisplayStateKind.AvailableForFinish,
            "Success" => QuestDisplayStateKind.Success,
            "Fail" => restartable ? QuestDisplayStateKind.FailRestartable : QuestDisplayStateKind.Fail,
            "FailRestartable" => QuestDisplayStateKind.FailRestartable,
            "MarkedAsFailed" => restartable ? QuestDisplayStateKind.FailRestartable : QuestDisplayStateKind.MarkedAsFailed,
            "Expired" => QuestDisplayStateKind.Expired,
            "AvailableAfter" => QuestDisplayStateKind.AvailableAfter,
            _ => QuestDisplayStateKind.Unknown,
        };
    }

    /// <summary>
    /// Applies the same blocker priority used by the browser QuestMap to the
    /// native client's live profile overlay.
    /// </summary>
    public static QuestMapDisplayStateKind ClassifyProfileDisplayState(
        QuestGraphTopology topology,
        QuestGraphNode node,
        QuestProfileOverlay overlay)
    {
        overlay.QuestsById.TryGetValue(node.Id, out var state);
        var exact = ClassifyDisplayState(state?.ExactStatus, state?.HasLiveQuest == true, node.Restartable);
        if (TryClassifyLiveDynamicState(exact, out var liveDynamic)) return liveDynamic;
        if (overlay.AuthoritativeDisplayStates.TryGetValue(node.Id, out var authoritative))
        {
            if (authoritative == QuestMapDisplayStateKind.Available && state?.HasLiveQuest != true)
                return QuestMapDisplayStateKind.Locked;
            return authoritative;
        }

        if (IsExcluded(node, overlay)) return QuestMapDisplayStateKind.Excluded;
        if (HasUnavailableTrader(node, overlay)) return QuestMapDisplayStateKind.TraderUnavailable;
        if (exact == QuestDisplayStateKind.AvailableAfter) return QuestMapDisplayStateKind.Pending;
        if (HasUnmetLevel(node, overlay.Level)) return QuestMapDisplayStateKind.LevelGated;
        if (HasUnmetTraderRequirement(node, overlay)) return QuestMapDisplayStateKind.TraderGated;
        if (HasUnmetPrerequisiteGate(topology, node.Id, overlay)) return QuestMapDisplayStateKind.PrerequisiteGated;
        if (exact == QuestDisplayStateKind.AvailableForStart || state?.Visible == true) return QuestMapDisplayStateKind.Available;
        return exact == QuestDisplayStateKind.Unknown ? QuestMapDisplayStateKind.Unknown : QuestMapDisplayStateKind.Locked;
    }

    public static bool TryParseProfileDisplayState(string? value, out QuestMapDisplayStateKind state) =>
        Enum.TryParse(value, false, out state);

    public static bool IsFinishedForFilter(
        QuestMapDisplayStateKind state,
        bool restartable,
        bool permanentExclusion = true) => state switch
    {
        QuestMapDisplayStateKind.Completed or QuestMapDisplayStateKind.Failed => true,
        QuestMapDisplayStateKind.Excluded => permanentExclusion,
        QuestMapDisplayStateKind.Expired => !restartable,
        _ => false,
    };

    public static bool IsLevelGatedForFilter(QuestMapDisplayStateKind state) =>
        state == QuestMapDisplayStateKind.LevelGated;

    public static bool IsTraderBoundaryForFilter(QuestMapDisplayStateKind state) => state is
        QuestMapDisplayStateKind.Available or QuestMapDisplayStateKind.InProgress
        or QuestMapDisplayStateKind.ReadyToFinish or QuestMapDisplayStateKind.Completed;

    public static double? CalculateObjectiveProgressPercent(IReadOnlyCollection<QuestObjectiveProgress> objectives)
        => QuestProgressRules.CalculateObjectiveProgressPercent(
            objectives,
            objective => objective.Complete,
            objective => objective.Current,
            objective => objective.Required);

    public static double? ResolveProfileProgressPercent(string questId, QuestProfileOverlay overlay)
    {
        if (overlay.QuestsById.TryGetValue(questId, out var live) && live.HasLiveQuest)
        {
            var exact = ClassifyDisplayState(live.ExactStatus, true, false);
            if (exact == QuestDisplayStateKind.AvailableForFinish) return 100d;
            if (exact == QuestDisplayStateKind.Started && live.Objectives.Count > 0
                && live.Objectives.All(objective => objective.Complete || objective.ProgressKnown))
            {
                return CalculateObjectiveProgressPercent(live.Objectives);
            }
        }
        return overlay.AuthoritativeProgressPercentages.TryGetValue(questId, out var authoritative)
            ? authoritative.HasValue ? Math.Clamp(authoritative.Value, 0d, 100d) : null
            : null;
    }

    public static bool IsTerminalQuest(QuestGraphTopology topology, string questId) =>
        !topology.NodesById.TryGetValue(questId, out var node) || node.ProfileGenerated
            ? false
            : !topology.OutgoingEdgesBySource.TryGetValue(questId, out var outgoing)
              || !outgoing.Any(edge => topology.ApplicableQuestIds.Contains(edge.TargetId));

    private static bool IsExcluded(QuestGraphNode node, QuestProfileOverlay overlay) =>
        node.ExclusionRules.Any(rule => overlay.QuestsById.TryGetValue(rule.CausedByQuestId, out var cause)
            && cause.ExactStatus is not null
            && rule.RequiredStatuses.Contains(cause.ExactStatus));

    private static bool TryClassifyLiveDynamicState(
        QuestDisplayStateKind exact,
        out QuestMapDisplayStateKind state)
    {
        state = exact switch
        {
            QuestDisplayStateKind.Success => QuestMapDisplayStateKind.Completed,
            QuestDisplayStateKind.AvailableForFinish => QuestMapDisplayStateKind.ReadyToFinish,
            QuestDisplayStateKind.Started => QuestMapDisplayStateKind.InProgress,
            QuestDisplayStateKind.FailRestartable => QuestMapDisplayStateKind.RestartableFailure,
            QuestDisplayStateKind.Expired => QuestMapDisplayStateKind.Expired,
            QuestDisplayStateKind.Fail or QuestDisplayStateKind.MarkedAsFailed => QuestMapDisplayStateKind.Failed,
            _ => default,
        };
        return exact is QuestDisplayStateKind.Success or QuestDisplayStateKind.AvailableForFinish
            or QuestDisplayStateKind.Started or QuestDisplayStateKind.FailRestartable
            or QuestDisplayStateKind.Expired or QuestDisplayStateKind.Fail or QuestDisplayStateKind.MarkedAsFailed;
    }

    private static bool HasUnavailableTrader(QuestGraphNode node, QuestProfileOverlay overlay)
    {
        if (overlay.TradersById.TryGetValue(node.TraderId, out var questTrader) && !questTrader.Available) return true;
        return node.EffectiveRequirements.Any(requirement => requirement.TraderId is not null
            && overlay.TradersById.TryGetValue(requirement.TraderId, out var trader)
            && !trader.Available);
    }

    private static bool HasUnmetLevel(QuestGraphNode node, int level) =>
        node.EffectiveRequirements.Any(requirement => requirement.Kind == "Level"
            && !Compare(level, requirement.Value, requirement.Compare));

    private static bool HasUnmetTraderRequirement(QuestGraphNode node, QuestProfileOverlay overlay) =>
        node.EffectiveRequirements.Any(requirement =>
        {
            if (requirement.Kind is not ("TraderLoyalty" or "TraderStanding") || requirement.TraderId is null) return false;
            if (!overlay.TradersById.TryGetValue(requirement.TraderId, out var trader) || !trader.Available) return false;
            var actual = requirement.Kind == "TraderLoyalty" ? trader.LoyaltyLevel : trader.Standing;
            return !Compare(actual, requirement.Value, requirement.Compare);
        });

    /// <summary>
    /// Evaluates prerequisite membership independently from the single
    /// presentation-state priority. This matters when a quest is blocked by a
    /// prerequisite and another gate at the same time.
    /// </summary>
    public static bool HasUnmetPrerequisiteGate(
        QuestGraphTopology topology,
        string questId,
        QuestProfileOverlay overlay)
    {
        if (overlay.PrerequisiteBlockerIds.TryGetValue(questId, out var authoritativeBlockers))
            return authoritativeBlockers.Count > 0;
        if (!topology.IncomingEdgesByTarget.TryGetValue(questId, out var incoming)) return false;
        return incoming.Any(edge => !overlay.QuestsById.TryGetValue(edge.SourceId, out var prerequisite)
            || prerequisite.ExactStatus is null
            || !edge.RequiredStatuses.Contains(prerequisite.ExactStatus));
    }

    public static bool Compare(double actual, double required, string compare) => compare switch
    {
        ">=" => actual >= required,
        ">" => actual > required,
        "<=" => actual <= required,
        "<" => actual < required,
        "=" or "==" => Math.Abs(actual - required) < 0.000001,
        _ => false,
    };

    public static IReadOnlyCollection<string> BuildPrerequisiteClosure(
        string questId,
        IEnumerable<string> questIds,
        IEnumerable<QuestDependency> dependencies)
    {
        var known = questIds.ToHashSet(StringComparer.Ordinal);
        if (!known.Contains(questId)) return new HashSet<string>(StringComparer.Ordinal);
        var incoming = dependencies
            .Where(edge => known.Contains(edge.SourceId) && known.Contains(edge.TargetId))
            .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.SourceId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal) { questId };
        var queue = new Queue<string>();
        queue.Enqueue(questId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!incoming.TryGetValue(current, out var prerequisites)) continue;
            foreach (var prerequisite in prerequisites)
            {
                if (result.Add(prerequisite)) queue.Enqueue(prerequisite);
            }
        }

        return result;
    }

    public static IReadOnlyCollection<string> BuildPrerequisiteClosure(QuestGraphTopology topology, string questId)
    {
        return BuildPrerequisiteClosure(
            questId,
            topology.NodesById.Keys,
            topology.Edges.Select(edge => new QuestDependency(edge.SourceId, edge.TargetId)));
    }

    public static QuestGraphSelection BuildSelection(QuestGraphTopology topology, string? questId)
    {
        if (string.IsNullOrWhiteSpace(questId) || !topology.NodesById.ContainsKey(questId))
            return QuestGraphSelection.Empty;
        var prerequisites = BuildPrerequisiteClosure(topology, questId).ToHashSet(StringComparer.Ordinal);
        prerequisites.Remove(questId);
        return new QuestGraphSelection(questId, prerequisites, GetDirectSuccessors(topology, questId));
    }

    public static IReadOnlyList<string> GetDirectSuccessors(QuestGraphTopology topology, string questId) =>
        topology.OutgoingEdgesBySource.TryGetValue(questId, out var outgoing)
            ? outgoing.Select(edge => edge.TargetId).Where(topology.NodesById.ContainsKey).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : [];

    public static IReadOnlyList<QuestGraphNode> FilterByTrader(IEnumerable<QuestGraphNode> nodes, string? traderId) =>
        StableOrder(string.IsNullOrWhiteSpace(traderId) ? nodes : nodes.Where(node => node.TraderId == traderId));

    public static IReadOnlyList<QuestGraphNode> FilterByFaction(IEnumerable<QuestGraphNode> nodes, string faction) =>
        StableOrder(nodes.Where(node => node.Faction == "Any" || node.Faction.Equals(faction, StringComparison.OrdinalIgnoreCase)));

    public static IReadOnlyList<QuestGraphNode> FilterByEvent(
        QuestGraphTopology topology,
        IReadOnlyCollection<string> activeEvents)
    {
        var excluded = topology.Nodes
            .Where(node => node.EventSeason == "None"
                || (node.EventSeason is not null && !activeEvents.Contains(node.EventSeason)))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in topology.Nodes)
            {
                if (excluded.Contains(node.Id)
                    || !topology.IncomingEdgesByTarget.TryGetValue(node.Id, out var incoming))
                {
                    continue;
                }

                var existingParents = incoming.Where(edge => topology.NodesById.ContainsKey(edge.SourceId)).ToArray();
                if (existingParents.Length > 0 && existingParents.All(edge => excluded.Contains(edge.SourceId)))
                {
                    excluded.Add(node.Id);
                    changed = true;
                }
            }
        }

        return StableOrder(topology.Nodes.Where(node => !excluded.Contains(node.Id)));
    }

    public static IReadOnlyCollection<string> BuildDefaultFrontier(
        QuestGraphTopology topology,
        IReadOnlyCollection<string> knownQuestIds,
        IReadOnlyCollection<string> boundaryQuestIds,
        IReadOnlyCollection<string> applicableQuestIds)
    {
        var result = new HashSet<string>(knownQuestIds.Where(applicableQuestIds.Contains), StringComparer.Ordinal);
        foreach (var sourceId in boundaryQuestIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!applicableQuestIds.Contains(sourceId)
                || !topology.OutgoingEdgesBySource.TryGetValue(sourceId, out var outgoing))
            {
                continue;
            }

            foreach (var edge in outgoing)
            {
                if (applicableQuestIds.Contains(edge.TargetId) && !knownQuestIds.Contains(edge.TargetId))
                {
                    result.Add(edge.TargetId);
                }
            }
        }

        return result;
    }

    public static IReadOnlyCollection<string> BuildDefaultFrontier(
        IReadOnlyCollection<string> knownQuestIds,
        IReadOnlyCollection<string> boundaryQuestIds,
        IReadOnlyCollection<string> applicableQuestIds,
        IEnumerable<QuestDependency> dependencies)
    {
        var known = knownQuestIds.ToHashSet(StringComparer.Ordinal);
        var applicable = applicableQuestIds.ToHashSet(StringComparer.Ordinal);
        var boundary = boundaryQuestIds.ToHashSet(StringComparer.Ordinal);
        var result = new HashSet<string>(known.Where(applicable.Contains), StringComparer.Ordinal);
        foreach (var edge in dependencies)
        {
            if (boundary.Contains(edge.SourceId)
                && applicable.Contains(edge.SourceId)
                && applicable.Contains(edge.TargetId)
                && !known.Contains(edge.TargetId))
            {
                result.Add(edge.TargetId);
            }
        }

        return result;
    }

    public static IReadOnlyCollection<string> BuildDescendantExclusionSet(
        IEnumerable<string> questIds,
        IEnumerable<string> initiallyExcludedQuestIds,
        IEnumerable<QuestDependency> dependencies)
    {
        var known = questIds.ToHashSet(StringComparer.Ordinal);
        var excluded = initiallyExcludedQuestIds.Where(known.Contains).ToHashSet(StringComparer.Ordinal);
        var incoming = dependencies
            .Where(edge => known.Contains(edge.SourceId) && known.Contains(edge.TargetId))
            .GroupBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(edge => edge.SourceId).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pair in incoming)
            {
                if (!excluded.Contains(pair.Key) && pair.Value.Length > 0 && pair.Value.All(excluded.Contains))
                {
                    excluded.Add(pair.Key);
                    changed = true;
                }
            }
        }

        return excluded;
    }

    public static IReadOnlyList<QuestGraphNode> FilterActive(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay) =>
        StableOrder(topology.Nodes.Where(node => overlay.QuestsById.TryGetValue(node.Id, out var state)
            && state.ExactStatus is not null
            && ActiveStatuses.Contains(state.ExactStatus)));

    public static IReadOnlyList<QuestGraphNode> FilterFinishedAndFailed(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        bool hideFinished) =>
        StableOrder(!hideFinished
            ? topology.Nodes
            : topology.Nodes.Where(node => !overlay.QuestsById.TryGetValue(node.Id, out var state)
                || state.ExactStatus is null
                || !FinishedStatuses.Contains(state.ExactStatus)));

    public static IReadOnlyList<QuestGraphNode> StableOrder(IEnumerable<QuestGraphNode> nodes) =>
        nodes.OrderBy(node => node.TraderName, StringComparer.Ordinal)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
}
