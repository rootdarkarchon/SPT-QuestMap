using System.Collections.ObjectModel;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Adapters;

public static class QuestOverlayBuilder
{
    public static IReadOnlyList<string> FindServerAvailableQuestsMissingFromNativeClient(QuestProfileOverlay overlay) =>
        overlay.AuthoritativeDisplayStates
            .Where(pair => pair.Value == QuestMapDisplayStateKind.Available
                && (!overlay.QuestsById.TryGetValue(pair.Key, out var state) || !state.HasLiveQuest))
            .Select(pair => pair.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    public static QuestProfileOverlay RejectServerAvailableQuestsMissingFromNativeClient(
        QuestProfileOverlay overlay,
        IReadOnlyCollection<string> rejectedQuestIds)
    {
        if (rejectedQuestIds.Count == 0) return overlay;
        var rejected = rejectedQuestIds.ToHashSet(StringComparer.Ordinal);
        return overlay with
        {
            DefaultVisibleQuestIds = overlay.DefaultVisibleQuestIds.Where(id => !rejected.Contains(id)).ToArray(),
            ApplicableQuestIds = overlay.ApplicableQuestIds.Where(id => !rejected.Contains(id)).ToArray(),
        };
    }

    public static QuestProfileOverlay Build(QuestGraphTopology topology, LiveProfileSnapshot snapshot)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        var liveById = snapshot.Quests
            .GroupBy(quest => quest.QuestId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var states = new Dictionary<string, QuestLiveState>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var node in topology.Nodes)
        {
            if (liveById.TryGetValue(node.Id, out var live))
            {
                states[node.Id] = new QuestLiveState(
                    node.Id,
                    live.ExactStatus,
                    true,
                    live.Visible,
                    NormalizeObjectiveProgress(node, live.Objectives),
                    live.ExpirationTime,
                    live.HandoverReady);
                continue;
            }

            states[node.Id] = new QuestLiveState(node.Id, null, false, false, [], null, false);
            missing.Add(node.Id);
        }

        var traders = snapshot.Traders
            .GroupBy(trader => trader.TraderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        return new QuestProfileOverlay(
            snapshot.ProfileId,
            snapshot.Faction,
            snapshot.Level,
            new ReadOnlyDictionary<string, QuestLiveState>(states),
            new ReadOnlyDictionary<string, LiveTraderSnapshot>(traders),
            missing.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    public static IReadOnlyList<QuestObjectiveProgress> NormalizeObjectiveProgress(
        QuestGraphNode node,
        IReadOnlyList<QuestObjectiveProgress> progress)
    {
        var definitions = node.Objectives
            .GroupBy(objective => objective.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return progress.Select(objective =>
        {
            if (objective.Complete || !objective.ProgressKnown
                || !objective.Current.HasValue || !objective.Required.HasValue)
                return objective;

            var compare = definitions.TryGetValue(objective.ObjectiveId, out var definition)
                ? definition.Compare ?? ">="
                : ">=";
            return QuestGraphRules.Compare(objective.Current.Value, objective.Required.Value, compare)
                ? objective with { Complete = true }
                : objective;
        }).ToArray();
    }
}
