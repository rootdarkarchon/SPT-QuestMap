using System.Collections.ObjectModel;
using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Adapters;

public static class QuestOverlayBuilder
{
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
                    live.Objectives,
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
}
