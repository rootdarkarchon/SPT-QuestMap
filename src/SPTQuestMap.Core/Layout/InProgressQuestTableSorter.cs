using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Layout;

public static class InProgressQuestTableSorter
{
    private static readonly QuestTableSortCriterion[] DefaultCriteria =
    [
        new(QuestTableSortColumn.Trader, QuestTableSortDirection.Ascending),
        new(QuestTableSortColumn.Location, QuestTableSortDirection.Ascending),
        new(QuestTableSortColumn.Quest, QuestTableSortDirection.Ascending),
    ];

    public static IReadOnlyList<QuestGraphNode> Sort(
        IEnumerable<QuestGraphNode> nodes,
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        IReadOnlyList<QuestTableSortCriterion> criteria)
        => Sort(nodes, topology, overlay, criteria, true);

    public static IReadOnlyList<QuestGraphNode> SortWithinSection(
        IEnumerable<QuestGraphNode> nodes,
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        IReadOnlyList<QuestTableSortCriterion> criteria)
        => Sort(nodes, topology, overlay, criteria, false);

    private static IReadOnlyList<QuestGraphNode> Sort(
        IEnumerable<QuestGraphNode> nodes,
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        IReadOnlyList<QuestTableSortCriterion> criteria,
        bool groupByRepeatableKind)
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        if (topology is null) throw new ArgumentNullException(nameof(topology));
        if (overlay is null) throw new ArgumentNullException(nameof(overlay));
        if (criteria is null) throw new ArgumentNullException(nameof(criteria));

        var effective = criteria.Count == 0 ? DefaultCriteria : criteria;
        return nodes.OrderBy(node => node, new NodeComparer(topology, overlay, effective, groupByRepeatableKind)).ToArray();
    }

    public static IReadOnlyList<QuestTableSortCriterion> Toggle(
        IReadOnlyList<QuestTableSortCriterion> criteria,
        QuestTableSortColumn column)
    {
        if (criteria is null) throw new ArgumentNullException(nameof(criteria));
        var next = criteria.ToList();
        var index = next.FindIndex(criterion => criterion.Column == column);
        if (index < 0)
        {
            next.Add(new QuestTableSortCriterion(column, QuestTableSortDirection.Ascending));
        }
        else if (next[index].Direction == QuestTableSortDirection.Ascending)
        {
            next[index] = next[index] with { Direction = QuestTableSortDirection.Descending };
        }
        else
        {
            next.RemoveAt(index);
        }
        return next;
    }

    private sealed class NodeComparer(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        IReadOnlyList<QuestTableSortCriterion> criteria,
        bool groupByRepeatableKind) : IComparer<QuestGraphNode>
    {
        public int Compare(QuestGraphNode? left, QuestGraphNode? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            if (groupByRepeatableKind)
            {
                var section = Section(left).CompareTo(Section(right));
                if (section != 0) return section;
            }

            foreach (var criterion in criteria)
            {
                if (criterion.Column == QuestTableSortColumn.Progress)
                {
                    var progressComparison = CompareProgress(Progress(left), Progress(right), criterion.Direction);
                    if (progressComparison != 0) return progressComparison;
                    continue;
                }
                var comparison = CompareCriterion(left, right, criterion.Column);
                if (comparison == 0) continue;
                return criterion.Direction == QuestTableSortDirection.Ascending ? comparison : -comparison;
            }

            return StringComparer.Ordinal.Compare(left.Id, right.Id);
        }

        private int CompareCriterion(QuestGraphNode left, QuestGraphNode right, QuestTableSortColumn column) => column switch
        {
            QuestTableSortColumn.Trader => CompareText(left.TraderName, right.TraderName),
            QuestTableSortColumn.Quest => CompareText(left.Name, right.Name),
            QuestTableSortColumn.Location => CompareText(LocationName(left), LocationName(right)),
            QuestTableSortColumn.Status => StatusRank(left).CompareTo(StatusRank(right)),
            QuestTableSortColumn.Progress => 0,
            _ => 0,
        };

        private int StatusRank(QuestGraphNode node) => QuestGraphRules.ClassifyProfileDisplayState(topology, node, overlay) switch
        {
            QuestMapDisplayStateKind.InProgress => 0,
            QuestMapDisplayStateKind.ReadyToFinish => 1,
            QuestMapDisplayStateKind.RestartableFailure => 2,
            _ => 3,
        };

        private double? Progress(QuestGraphNode node)
            => QuestGraphRules.ResolveProfileProgressPercent(node.Id, overlay);

        private static int CompareProgress(
            double? left,
            double? right,
            QuestTableSortDirection direction)
        {
            if (left.HasValue && right.HasValue)
            {
                var comparison = left.Value.CompareTo(right.Value);
                return direction == QuestTableSortDirection.Ascending ? comparison : -comparison;
            }
            if (left.HasValue) return -1;
            if (right.HasValue) return 1;
            return 0;
        }

        private static int Section(QuestGraphNode node) => node.RepeatableKind?.ToUpperInvariant() switch
        {
            "DAILY" => 0,
            "WEEKLY" => 1,
            _ => 2,
        };

        private static string LocationName(QuestGraphNode node) =>
            node.Location.Any ? "Any" : node.Location.Name ?? node.Location.Id;

        private static int CompareText(string left, string right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }
}
