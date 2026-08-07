using System.Collections.ObjectModel;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Layout;

public static class GlobalQuestGraphProjectionBuilder
{
    public static GlobalQuestGraphProjection Build(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay,
        GlobalQuestGraphOptions options)
    {
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        if (layout is null) throw new ArgumentNullException(nameof(layout));
        if (overlay is null) throw new ArgumentNullException(nameof(overlay));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (!string.Equals(topology.Version, layout.TopologyVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("The topology and layout versions do not match.", nameof(layout));
        }

        var visible = BuildVisibleIds(topology, overlay, options);
        var nodes = topology.Nodes
            .Where(node => visible.Contains(node.Id) && layout.NodesById.ContainsKey(node.Id))
            .OrderBy(node => layout.NodesById[node.Id].Rank)
            .ThenBy(node => node.TraderName, StringComparer.Ordinal)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        var visibleIds = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var positions = CompactPositions(nodes, layout, options.Mode);
        var edges = topology.Edges
            .Where(edge => visibleIds.Contains(edge.SourceId) && visibleIds.Contains(edge.TargetId))
            .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToArray();

        return new GlobalQuestGraphProjection(
            options.Mode,
            nodes,
            edges,
            new ReadOnlyDictionary<string, QuestNodePosition>(positions),
            positions.Count == 0 ? 0 : positions.Values.Max(position => position.X + position.Width),
            positions.Count == 0 ? 0 : positions.Values.Max(position => position.Y + position.Height));
    }

    private static HashSet<string> BuildVisibleIds(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        GlobalQuestGraphOptions options)
    {
        var applicableSource = overlay.ApplicableQuestIds.Count > 0 ? overlay.ApplicableQuestIds : topology.ApplicableQuestIds;
        var defaultSource = overlay.DefaultVisibleQuestIds.Count > 0 ? overlay.DefaultVisibleQuestIds : topology.DefaultVisibleQuestIds;
        var applicable = applicableSource.ToHashSet(StringComparer.Ordinal);
        if (options.Mode == GlobalQuestGraphMode.Full)
        {
            return QuestVisibilityRules.BuildVisibleIds(
                topology.Nodes.Select(node => new QuestVisibilityNode(
                    node.Id, node.Name, node.TraderId, node.TraderName, node.Restartable, node.ProfileGenerated)).ToArray(),
                topology.Edges.Select(edge => new QuestDependency(edge.SourceId, edge.TargetId)).ToArray(),
                topology.Nodes.Select(node => new QuestVisibilityState(
                    node.Id,
                    QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay),
                    overlay.PrerequisiteBlockerIds.GetValueOrDefault(node.Id))).ToArray(),
                defaultSource,
                applicableSource,
                new QuestVisibilityOptions(
                    options.ShowAllFuture,
                    !options.HideFinished,
                    options.LevelEligibleOnly,
                    options.TraderId,
                    options.Search,
                    options.SelectedQuestId,
                    options.FocusQuestId,
                    true));
        }
        var visible = options.Mode == GlobalQuestGraphMode.InProgress
            ? topology.Nodes.Where(node => IsActive(QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay)))
                .Select(node => node.Id).ToHashSet(StringComparer.Ordinal)
            : (options.ShowAllFuture ? applicableSource : defaultSource)
                .ToHashSet(StringComparer.Ordinal);

        if (options.Mode == GlobalQuestGraphMode.InProgress && !string.IsNullOrWhiteSpace(options.ActiveStatusFilter))
        {
            visible.RemoveWhere(id => !topology.NodesById.TryGetValue(id, out var node)
                || !MatchesActiveStatus(
                    QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay),
                    options.ActiveStatusFilter!));
        }

        // Focus is a chain projection, not another filter. Match the browser by
        // ignoring future, finished, level, trader, and search constraints here.
        if (!string.IsNullOrWhiteSpace(options.FocusQuestId)
            && topology.NodesById.ContainsKey(options.FocusQuestId!))
        {
            var focus = QuestGraphRules.BuildPrerequisiteClosure(topology, options.FocusQuestId!)
                .Where(applicable.Contains)
                .ToHashSet(StringComparer.Ordinal);
            focus.UnionWith(QuestGraphRules.GetDirectSuccessors(topology, options.FocusQuestId!).Where(applicable.Contains));
            return focus;
        }

        if (options.HideFinished)
        {
            visible.RemoveWhere(id => topology.NodesById.TryGetValue(id, out var node)
                && QuestGraphRules.IsFinishedForFilter(
                    QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay), node.Restartable));
        }

        if (options.LevelEligibleOnly)
        {
            visible.RemoveWhere(id => topology.NodesById.TryGetValue(id, out var node)
                && QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay) == QuestMapDisplayStateKind.LevelGated);
        }

        if (!string.IsNullOrWhiteSpace(options.TraderId))
        {
            var traderVisible = visible.Intersect(topology.Nodes
                .Where(node => string.Equals(node.TraderId, options.TraderId, StringComparison.Ordinal))
                .Select(node => node.Id)).ToHashSet(StringComparer.Ordinal);
            var boundaries = traderVisible.Where(id => topology.NodesById.TryGetValue(id, out var node)
                && QuestGraphRules.IsTraderBoundaryForFilter(
                    QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay))).ToArray();
            foreach (var source in boundaries)
            {
                if (!topology.OutgoingEdgesBySource.TryGetValue(source, out var outgoing)) continue;
                foreach (var edge in outgoing)
                {
                    if (applicable.Contains(edge.TargetId) && PassesCommonFilters(topology, overlay, options, edge.TargetId))
                        traderVisible.Add(edge.TargetId);
                }
            }
            foreach (var target in traderVisible.ToArray())
            {
                if (!topology.IncomingEdgesByTarget.TryGetValue(target, out var incoming)) continue;
                foreach (var edge in incoming)
                {
                    if (!applicable.Contains(edge.SourceId) || IsRequirementSatisfied(edge, overlay)) continue;
                    traderVisible.Add(edge.SourceId);
                }
            }
            visible = traderVisible;
        }

        var search = options.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            visible.IntersectWith(topology.Nodes
                .Where(node => node.Name.Contains(search!, StringComparison.OrdinalIgnoreCase)
                    || node.TraderName.Contains(search!, StringComparison.OrdinalIgnoreCase)
                    || node.Id.Contains(search!, StringComparison.OrdinalIgnoreCase))
                .Select(node => node.Id));
        }

        var routeIds = options.RouteFilter switch
        {
            QuestRouteFilter.Collector => topology.CollectorPathQuestIds,
            QuestRouteFilter.Lightkeeper => topology.LightkeeperPathQuestIds,
            _ => null,
        };
        if (routeIds is not null) visible.IntersectWith(routeIds);

        // Browser parity: ordinary selection is not a destructive filter. A selected
        // full-map quest must retain its complete applicable predecessor chain even
        // when search, trader, future-depth, or finished visibility hid those nodes.
        if (options.Mode == GlobalQuestGraphMode.Full
            && !string.IsNullOrWhiteSpace(options.SelectedQuestId)
            && topology.NodesById.ContainsKey(options.SelectedQuestId!))
        {
            var selectedChain = QuestGraphRules.BuildPrerequisiteClosure(topology, options.SelectedQuestId!)
                .Where(applicable.Contains);
            visible.UnionWith(selectedChain);
        }

        return visible;
    }

    private static bool MatchesActiveStatus(QuestMapDisplayStateKind status, string filter) => filter switch
    {
        "READY" => status == QuestMapDisplayStateKind.ReadyToFinish,
        "RETRY" => status == QuestMapDisplayStateKind.RestartableFailure,
        "STARTED" => status == QuestMapDisplayStateKind.InProgress,
        _ => true,
    };

    private static bool IsActive(QuestMapDisplayStateKind display) => display is
        QuestMapDisplayStateKind.InProgress or QuestMapDisplayStateKind.ReadyToFinish
        or QuestMapDisplayStateKind.RestartableFailure;

    private static bool PassesCommonFilters(QuestGraphTopology topology, QuestProfileOverlay overlay, GlobalQuestGraphOptions options, string id)
    {
        if (!topology.NodesById.TryGetValue(id, out var node)) return false;
        var display = QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay);
        if (options.HideFinished && QuestGraphRules.IsFinishedForFilter(display, node.Restartable)) return false;
        return !options.LevelEligibleOnly || display != QuestMapDisplayStateKind.LevelGated;
    }

    private static bool IsRequirementSatisfied(QuestGraphEdge edge, QuestProfileOverlay overlay)
    {
        if (!overlay.QuestsById.TryGetValue(edge.SourceId, out var state) || state.ExactStatus is null) return false;
        return edge.RequiredStatuses.Count == 0 || edge.RequiredStatuses.Contains(state.ExactStatus);
    }

    private static Dictionary<string, QuestNodePosition> CompactPositions(
        IReadOnlyList<QuestGraphNode> nodes,
        QuestGraphLayout layout,
        GlobalQuestGraphMode mode)
    {
        if (mode == GlobalQuestGraphMode.InProgress)
        {
            return BuildInProgressPositions(nodes, layout);
        }

        var repeatables = nodes.Where(node => node.ProfileGenerated).ToArray();
        var ordinary = nodes.Where(node => !node.ProfileGenerated).ToArray();
        var rankMap = ordinary
            .Select(node => layout.NodesById[node.Id].Rank)
            .Distinct()
            .OrderBy(rank => rank)
            .Select((rank, compactRank) => (rank, compactRank))
            .ToDictionary(pair => pair.rank, pair => pair.compactRank);
        var positions = new Dictionary<string, QuestNodePosition>(StringComparer.Ordinal);
        const double repeatableBandHeight = 210;
        var repeatableX = 0d;
        foreach (var kind in new[] { "Daily", "Weekly" })
        {
            var group = repeatables.Where(node => string.Equals(node.RepeatableKind, kind, StringComparison.OrdinalIgnoreCase))
                .OrderBy(node => node.TraderName, StringComparer.Ordinal).ThenBy(node => node.Name, StringComparer.Ordinal).ToArray();
            for (var index = 0; index < group.Length; index++)
            {
                var node = group[index];
                positions[node.Id] = new QuestNodePosition(node.Id, -1,
                    repeatableX + index * (DeterministicGraphLayout.NodeWidth + 24), 42,
                    DeterministicGraphLayout.NodeWidth, DeterministicGraphLayout.NodeHeight);
            }
            if (group.Length > 0) repeatableX += group.Length * (DeterministicGraphLayout.NodeWidth + 24) + 48;
        }
        foreach (var node in repeatables.Where(node => node.RepeatableKind is not "Daily" and not "Weekly"))
        {
            positions[node.Id] = new QuestNodePosition(node.Id, -1,
                repeatableX, 42,
                DeterministicGraphLayout.NodeWidth, DeterministicGraphLayout.NodeHeight);
            repeatableX += DeterministicGraphLayout.NodeWidth + 24;
        }
        foreach (var rankGroup in ordinary.GroupBy(node => layout.NodesById[node.Id].Rank).OrderBy(group => group.Key))
        {
            var row = 0;
            foreach (var node in rankGroup
                         .OrderBy(node => node.TraderName, StringComparer.Ordinal)
                         .ThenBy(node => node.Name, StringComparer.Ordinal)
                         .ThenBy(node => node.Id, StringComparer.Ordinal))
            {
                var compactRank = rankMap[rankGroup.Key];
                positions[node.Id] = new QuestNodePosition(
                    node.Id,
                    rankGroup.Key,
                    compactRank * (DeterministicGraphLayout.NodeWidth + DeterministicGraphLayout.LayerGap),
                    repeatableBandHeight + row * (DeterministicGraphLayout.NodeHeight + DeterministicGraphLayout.RowGap),
                    DeterministicGraphLayout.NodeWidth,
                    DeterministicGraphLayout.NodeHeight);
                row++;
            }
        }

        return positions;
    }

    private static Dictionary<string, QuestNodePosition> BuildInProgressPositions(
        IReadOnlyList<QuestGraphNode> nodes,
        QuestGraphLayout layout)
    {
        const double cardWidth = 500;
        const double cardHeight = 82;
        const double columnGap = 28;
        const double rowGap = 14;
        var positions = new Dictionary<string, QuestNodePosition>(StringComparer.Ordinal);
        var column = 0;
        foreach (var traderGroup in nodes.GroupBy(node => (node.TraderId, node.TraderName))
                     .OrderBy(group => group.Key.TraderName, StringComparer.Ordinal))
        {
            var row = 0;
            foreach (var node in traderGroup.OrderBy(node => node.Name, StringComparer.Ordinal).ThenBy(node => node.Id, StringComparer.Ordinal))
            {
                positions[node.Id] = new QuestNodePosition(
                    node.Id, layout.NodesById[node.Id].Rank,
                    column * (cardWidth + columnGap), 34 + row * (cardHeight + rowGap),
                    cardWidth, cardHeight);
                row++;
            }
            column++;
        }
        return positions;
    }
}
