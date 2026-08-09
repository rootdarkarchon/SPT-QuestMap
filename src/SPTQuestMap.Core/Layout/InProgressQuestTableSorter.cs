using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Core.Layout;

public static class InProgressQuestTableSorter
{
    private static readonly string[] TraderOrder =
    [
        "54cb50c76803fa8b248b4571", "54cb57776803fa99248b456e", "579dc571d53a0658a154fbec",
        "58330581ace78e27b8b10cee", "5935c25fb3acc3127c3d8cd9", "5a7c2eca46aef81a7ca2145d",
        "5ac3b934156ae10c4430e83c", "5c0647fdd443bc2504c2d371", "6617beeaa9cfa777ca915b7c",
        "638f541a29ffd1183d187f57", "656f0f98d80a697f855d34b1",
    ];

    public static int NaturalTraderRank(string traderId)
    {
        var rank = Array.IndexOf(TraderOrder, traderId);
        return rank < 0 ? int.MaxValue : rank;
    }

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

        return nodes.OrderBy(node => node, new NodeComparer(topology, overlay, criteria, groupByRepeatableKind)).ToArray();
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

            if (criteria.Count == 0)
            {
                var trader = NaturalTraderRank(left.TraderId).CompareTo(NaturalTraderRank(right.TraderId));
                if (trader != 0) return trader;
                var location = CompareText(LocationName(left), LocationName(right));
                if (location != 0) return location;
                return left.NaturalOrder.CompareTo(right.NaturalOrder);
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
