using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Layout;

public static class RaidTrackedQuestListProjectionBuilder
{
    public static RaidTrackedQuestListProjection Build(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        IReadOnlyCollection<string> trackedQuestIds,
        IReadOnlyCollection<string> locationIds,
        IReadOnlyCollection<string>? currentMapZoneIds = null)
    {
        var tracked = trackedQuestIds.ToHashSet(StringComparer.Ordinal);
        var locations = locationIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mapZones = currentMapZoneIds?.ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        var applicableSource = overlay.AuthoritativeDisplayStates.Count > 0
            ? overlay.ApplicableQuestIds
            : topology.ApplicableQuestIds;
        var applicable = applicableSource.ToHashSet(StringComparer.Ordinal);
        var entries = topology.Nodes
            .Where(node => tracked.Contains(node.Id) && applicable.Contains(node.Id))
            .Where(node => MatchesLocation(node.Location, locations))
            .Select(node => (Node: node, State: overlay.QuestsById.GetValueOrDefault(node.Id)))
            .Where(pair => pair.State?.HasLiveQuest == true && IsActive(pair.State.ExactStatus))
            .Select(pair => new RaidTrackedQuestEntry(pair.Node, OpenObjectives(pair.Node, pair.State!, mapZones)))
            .Where(entry => entry.Quest.Objectives.Count == 0 || entry.Objectives.Count > 0)
            .OrderBy(entry => InProgressQuestTableSorter.NaturalTraderRank(entry.Quest.TraderId))
            .ThenBy(entry => entry.Quest.TraderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Quest.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Quest.Id, StringComparer.Ordinal)
            .ToArray();

        var groups = entries.GroupBy(entry => entry.Quest.TraderId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First().Quest;
                var trader = topology.TradersById.GetValueOrDefault(group.Key)
                    ?? new QuestTrader(group.Key, first.TraderName, first.TraderImageUrl);
                return new RaidTrackedTraderGroup(trader, group.ToArray());
            })
            .ToArray();
        return new RaidTrackedQuestListProjection(groups);
    }

    private static IReadOnlyList<RaidTrackedObjectiveEntry> OpenObjectives(
        QuestGraphNode node,
        QuestLiveState state,
        ISet<string> currentMapZoneIds)
    {
        var progressById = state.Objectives.GroupBy(progress => progress.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        return node.Objectives
            .Select((definition, order) => new { definition, order })
            .OrderBy(value => value.definition.Index ?? int.MaxValue)
            .ThenBy(value => value.order)
            .Select(value => new RaidTrackedObjectiveEntry(
                value.definition,
                progressById.GetValueOrDefault(value.definition.Id)))
            .Where(entry => RaidObjectiveLocationRules.IsActiveOnMap(
                node,
                entry.Definition,
                currentMapZoneIds))
            .Where(entry => entry.Progress?.Complete != true)
            .ToArray();
    }

    private static bool MatchesLocation(QuestLocation location, HashSet<string> locations)
    {
        if (location.Any || locations.Contains(location.Id)) return true;
        var transit = location.Id.Contains("transit", StringComparison.OrdinalIgnoreCase)
            || location.Id.Contains("marathon", StringComparison.OrdinalIgnoreCase)
            || (location.Name?.Contains("transition", StringComparison.OrdinalIgnoreCase) ?? false);
        return transit && (locations.Contains("transit") || locations.Contains("marathon"));
    }

    private static bool IsActive(string? exactStatus) => exactStatus == "Started";
}

public sealed record RaidTrackedObjectiveEntry(
    QuestObjectiveDefinition Definition,
    QuestObjectiveProgress? Progress);

public sealed record RaidTrackedQuestEntry(
    QuestGraphNode Quest,
    IReadOnlyList<RaidTrackedObjectiveEntry> Objectives);

public sealed record RaidTrackedTraderGroup(
    QuestTrader Trader,
    IReadOnlyList<RaidTrackedQuestEntry> Quests);

public sealed record RaidTrackedQuestListProjection(
    IReadOnlyList<RaidTrackedTraderGroup> Groups)
{
    public static RaidTrackedQuestListProjection Empty { get; } = new([]);
}
