using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Layout;

public static class RaidTrackedQuestListProjectionBuilder
{
    public static RaidTrackedQuestListProjection Build(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        IReadOnlyCollection<string> trackedQuestIds,
        IReadOnlyCollection<string> currentMapIds,
        bool smartTracking = false)
    {
        currentMapIds = QuestObjectiveMapRules.ExpandMapIds(currentMapIds, topology.MapAliases);
        var tracked = trackedQuestIds.ToHashSet(StringComparer.Ordinal);
        var applicableSource = overlay.AuthoritativeDisplayStates.Count > 0
            ? overlay.ApplicableQuestIds
            : topology.ApplicableQuestIds;
        var applicable = applicableSource.ToHashSet(StringComparer.Ordinal);
        var entries = topology.Nodes
            .Where(node => tracked.Contains(node.Id) && applicable.Contains(node.Id))
            .Select(node => (Node: node, State: overlay.QuestsById.GetValueOrDefault(node.Id)))
            .Where(pair => pair.State?.HasLiveQuest == true && IsActive(pair.State.ExactStatus))
            .Select(pair => new RaidTrackedQuestEntry(
                pair.Node,
                OpenObjectives(pair.Node, pair.State!, currentMapIds, smartTracking)))
            .Where(entry => entry.Objectives.Count > 0)
            .OrderBy(entry => InProgressQuestTableSorter.NaturalTraderRank(entry.Quest.TraderId))
            .ThenBy(entry => entry.Quest.TraderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Quest.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Quest.Id, StringComparer.Ordinal)
            .ToArray();

        var currentMapEntries = entries
            .Where(entry => HasCurrentMapObjective(entry, currentMapIds))
            .ToArray();
        var currentMapQuestIds = currentMapEntries
            .Select(entry => entry.Quest.Id)
            .ToHashSet(StringComparer.Ordinal);
        var anyMapEntries = entries
            .Where(entry => !currentMapQuestIds.Contains(entry.Quest.Id) && HasAnyMapObjective(entry))
            .ToArray();

        var sections = new List<RaidTrackedQuestListSection>(2);
        if (currentMapEntries.Length > 0)
        {
            sections.Add(new RaidTrackedQuestListSection(
                RaidTrackedQuestListScope.CurrentMap,
                ResolveCurrentMapName(currentMapEntries, currentMapIds),
                BuildTraderGroups(topology, currentMapEntries)));
        }
        if (anyMapEntries.Length > 0)
        {
            sections.Add(new RaidTrackedQuestListSection(
                RaidTrackedQuestListScope.Any,
                ResolveAnyMapName(anyMapEntries),
                BuildTraderGroups(topology, anyMapEntries)));
        }
        return new RaidTrackedQuestListProjection(sections);
    }

    private static IReadOnlyList<RaidTrackedTraderGroup> BuildTraderGroups(
        QuestGraphTopology topology,
        IReadOnlyCollection<RaidTrackedQuestEntry> entries) =>
        entries.GroupBy(entry => entry.Quest.TraderId, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First().Quest;
                var trader = topology.TradersById.GetValueOrDefault(group.Key)
                    ?? new QuestTrader(group.Key, first.TraderName, first.TraderImageUrl);
                return new RaidTrackedTraderGroup(trader, group.ToArray());
            })
            .ToArray();

    private static bool HasCurrentMapObjective(
        RaidTrackedQuestEntry entry,
        IReadOnlyCollection<string> currentMapIds) =>
        entry.Objectives.Any(objective => objective.Definition.TaskLocations.Any(map =>
            !map.Id.Equals(QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase)
            && !map.Id.Equals(QuestObjectiveMapRules.TransitionFilterId, StringComparison.OrdinalIgnoreCase)
            && currentMapIds.Contains(map.Id, StringComparer.OrdinalIgnoreCase)));

    private static bool HasAnyMapObjective(RaidTrackedQuestEntry entry) =>
        entry.Objectives.Any(objective => objective.Definition.TaskLocations.Any(map =>
            map.Id.Equals(QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase)));

    private static string ResolveCurrentMapName(
        IReadOnlyCollection<RaidTrackedQuestEntry> entries,
        IReadOnlyCollection<string> currentMapIds) =>
        entries.SelectMany(entry => entry.Objectives)
            .SelectMany(objective => objective.Definition.TaskLocations)
            .FirstOrDefault(map =>
                !map.Id.Equals(QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase)
                && !map.Id.Equals(QuestObjectiveMapRules.TransitionFilterId, StringComparison.OrdinalIgnoreCase)
                && currentMapIds.Contains(map.Id, StringComparer.OrdinalIgnoreCase))
            ?.Name
        ?? currentMapIds.FirstOrDefault()
        ?? string.Empty;

    private static string ResolveAnyMapName(IReadOnlyCollection<RaidTrackedQuestEntry> entries) =>
        entries.SelectMany(entry => entry.Objectives)
            .SelectMany(objective => objective.Definition.TaskLocations)
            .FirstOrDefault(map => map.Id.Equals(
                QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase))
            ?.Name
        ?? "Any";

    private static IReadOnlyList<RaidTrackedObjectiveEntry> OpenObjectives(
        QuestGraphNode node,
        QuestLiveState state,
        IReadOnlyCollection<string> currentMapIds,
        bool smartTracking)
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
            .Where(entry => QuestObjectiveMapRules.IsObjectiveActiveInRaid(
                node, entry.Definition, currentMapIds, smartTracking))
            .Where(entry => entry.Progress?.Complete != true)
            .ToArray();
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

public enum RaidTrackedQuestListScope
{
    CurrentMap,
    Any,
}

public sealed record RaidTrackedQuestListSection(
    RaidTrackedQuestListScope Scope,
    string Name,
    IReadOnlyList<RaidTrackedTraderGroup> Groups);

public sealed record RaidTrackedQuestListProjection(
    IReadOnlyList<RaidTrackedQuestListSection> Sections)
{
    public static RaidTrackedQuestListProjection Empty { get; } = new([]);
}
