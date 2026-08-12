using System.Collections.ObjectModel;

namespace SPTQuestMap.Core.Models;

public enum QuestEdgeRequirementKind
{
    Success,
    Failure,
    Started,
    AnyOutcome,
    Unknown,
}

public enum QuestDisplayStateKind
{
    LockedFuture,
    AvailableForStart,
    Started,
    AvailableForFinish,
    Success,
    Fail,
    FailRestartable,
    MarkedAsFailed,
    Expired,
    AvailableAfter,
    Unknown,
}

/// <summary>
/// Profile-aware presentation states shared by the native renderer and the
/// browser QuestMap contract. Unlike <see cref="QuestDisplayStateKind"/>,
/// these values include evaluated blockers rather than only EFT's exact status.
/// </summary>
public enum QuestMapDisplayStateKind
{
    Locked,
    PrerequisiteGated,
    PrestigeGated,
    LevelGated,
    TraderGated,
    TraderUnavailable,
    Available,
    InProgress,
    ReadyToFinish,
    Completed,
    Failed,
    Excluded,
    RestartableFailure,
    Expired,
    Pending,
    Unknown,
}

public enum QuestNodeHighlightKind
{
    None,
    Prerequisite,
    DirectSuccessor,
    Selected,
}

public sealed record QuestRequirement(string Kind, string? TraderId, string Compare, double Value);

public sealed record QuestObjectiveDefinition(
    string Id,
    string Text,
    string ConditionType,
    int? Index,
    string? ParentId,
    double? RequiredValue,
    string? Compare,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> ZoneIds,
    bool OneSessionOnly = false,
    bool DoNotResetIfCounterCompleted = false)
{
    public IReadOnlyList<string> MapIds { get; init; } = [];

    public IReadOnlyList<string> UnresolvedZoneIds { get; init; } = [];

    public bool ContributesToProgress { get; init; } = true;
}

public sealed record QuestUnknownCondition(string Stage, string ConditionType, string? ConditionId);

public sealed record QuestExclusionRule(string CausedByQuestId, IReadOnlyList<string> RequiredStatuses);

public sealed record QuestRewardItem(string TemplateId, string Name, double Count);

public sealed record QuestRelevantItem(string TemplateId, string Name, bool FleaEligible);

public sealed record QuestReward(
    string Id,
    string Type,
    string? TargetId,
    string? TargetName,
    double? Value,
    int? LoyaltyLevel,
    string? TraderName,
    bool Unknown,
    bool Hidden,
    IReadOnlyList<QuestRewardItem> Items);

public sealed record QuestLocation(string Id, string? Name, bool Any, string? BannerImageUrl);

public sealed record QuestMapReference(string Id, string Name, string? BannerImageUrl);

public sealed record QuestTrader(string Id, string Name, string? ImageUrl);

public sealed record QuestGraphNode(
    string Id,
    string Name,
    string Description,
    string TraderId,
    string TraderName,
    string? TraderImageUrl,
    string ImageUrl,
    string Faction,
    QuestLocation Location,
    string? EventSeason,
    bool Restartable,
    IReadOnlyList<QuestRequirement> DirectRequirements,
    IReadOnlyList<QuestRequirement> EffectiveRequirements,
    IReadOnlyList<QuestObjectiveDefinition> Objectives,
    IReadOnlyList<QuestExclusionRule> ExclusionRules,
    IReadOnlyList<QuestReward> Rewards,
    IReadOnlyList<QuestUnknownCondition> UnknownConditions,
    bool ProfileGenerated,
    string? RepeatableKind)
{
    public IReadOnlyList<QuestMapReference> ActualMaps { get; init; } = [];

    public bool ActualMapsComplete { get; init; }

    public string? Summary { get; init; }

    public string? WikiUrl { get; init; }

    public IReadOnlyList<QuestRelevantItem> RelevantItems { get; init; } = [];

    public bool ScavRepeatable { get; init; }

    /// <summary>Stable order supplied by the authoritative topology feed.</summary>
    public int NaturalOrder { get; init; } = int.MaxValue;
}

public sealed record QuestGraphEdge(
    string SourceId,
    string TargetId,
    IReadOnlyList<string> RequiredStatuses,
    int AvailableAfterSeconds,
    QuestEdgeRequirementKind RequirementKind);

public sealed record QuestDependency(string SourceId, string TargetId);

public sealed record QuestTopologyDiagnostics(
    IReadOnlyList<string> MissingPredecessorIds,
    IReadOnlyList<string> MissingTargetIds,
    int UnknownConditionCount);

public sealed class QuestGraphTopology
{
    internal QuestGraphTopology(
        string version,
        IReadOnlyList<QuestGraphNode> nodes,
        IReadOnlyList<QuestGraphEdge> edges,
        IReadOnlyList<QuestTrader> traders,
        IReadOnlyCollection<string> defaultVisibleQuestIds,
        IReadOnlyCollection<string> applicableQuestIds,
        IReadOnlyCollection<string> collectorPathQuestIds,
        IReadOnlyCollection<string> lightkeeperPathQuestIds,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> mapAliases,
        QuestTopologyDiagnostics diagnostics)
    {
        Version = version;
        Nodes = nodes;
        Edges = edges;
        Traders = traders;
        DefaultVisibleQuestIds = defaultVisibleQuestIds;
        ApplicableQuestIds = applicableQuestIds;
        CollectorPathQuestIds = collectorPathQuestIds;
        LightkeeperPathQuestIds = lightkeeperPathQuestIds;
        MapAliases = mapAliases;
        Diagnostics = diagnostics;

        NodesById = new ReadOnlyDictionary<string, QuestGraphNode>(
            nodes.ToDictionary(node => node.Id, StringComparer.Ordinal));
        TradersById = new ReadOnlyDictionary<string, QuestTrader>(
            traders.ToDictionary(trader => trader.Id, StringComparer.Ordinal));
        IncomingEdgesByTarget = BuildEdgeLookup(edges, edge => edge.TargetId);
        OutgoingEdgesBySource = BuildEdgeLookup(edges, edge => edge.SourceId);
    }

    public string Version { get; }

    public IReadOnlyList<QuestGraphNode> Nodes { get; }

    public IReadOnlyList<QuestGraphEdge> Edges { get; }

    public IReadOnlyList<QuestTrader> Traders { get; }

    public IReadOnlyCollection<string> DefaultVisibleQuestIds { get; }

    public IReadOnlyCollection<string> ApplicableQuestIds { get; }

    public IReadOnlyCollection<string> CollectorPathQuestIds { get; }

    public IReadOnlyCollection<string> LightkeeperPathQuestIds { get; }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> MapAliases { get; }

    public IReadOnlyDictionary<string, QuestGraphNode> NodesById { get; }

    public IReadOnlyDictionary<string, QuestTrader> TradersById { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<QuestGraphEdge>> IncomingEdgesByTarget { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<QuestGraphEdge>> OutgoingEdgesBySource { get; }

    public QuestTopologyDiagnostics Diagnostics { get; }

    private static IReadOnlyDictionary<string, IReadOnlyList<QuestGraphEdge>> BuildEdgeLookup(
        IEnumerable<QuestGraphEdge> edges,
        Func<QuestGraphEdge, string> keySelector)
    {
        var lookup = edges
            .GroupBy(keySelector, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<QuestGraphEdge>)group
                    .OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
                    .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        return new ReadOnlyDictionary<string, IReadOnlyList<QuestGraphEdge>>(lookup);
    }
}

public sealed record QuestObjectiveProgress(
    string ObjectiveId,
    bool Complete,
    double? Current,
    double? Required,
    bool ProgressKnown)
{
    public bool ContributesToProgress { get; init; } = true;
}

public sealed record LiveQuestSnapshot(
    string QuestId,
    string ExactStatus,
    bool Visible,
    IReadOnlyList<QuestObjectiveProgress> Objectives,
    long? ExpirationTime,
    bool HandoverReady);

public sealed record LiveTraderSnapshot(
    string TraderId,
    bool Available,
    int LoyaltyLevel,
    double Standing,
    long SalesSum);

public sealed record LiveProfileSnapshot(
    string ProfileId,
    string Faction,
    int Level,
    IReadOnlyList<LiveQuestSnapshot> Quests,
    IReadOnlyList<LiveTraderSnapshot> Traders)
{
    public int PrestigeLevel { get; init; }
}

public sealed record QuestLiveState(
    string QuestId,
    string? ExactStatus,
    bool HasLiveQuest,
    bool Visible,
    IReadOnlyList<QuestObjectiveProgress> Objectives,
    long? ExpirationTime,
    bool HandoverReady);

public sealed record QuestProfileOverlay(
    string ProfileId,
    string Faction,
    int Level,
    IReadOnlyDictionary<string, QuestLiveState> QuestsById,
    IReadOnlyDictionary<string, LiveTraderSnapshot> TradersById,
    IReadOnlyList<string> MissingLiveQuestIds)
{
    public int PrestigeLevel { get; init; }

    public IReadOnlyDictionary<string, QuestMapDisplayStateKind> AuthoritativeDisplayStates { get; init; }
        = new ReadOnlyDictionary<string, QuestMapDisplayStateKind>(new Dictionary<string, QuestMapDisplayStateKind>(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, double?> AuthoritativeProgressPercentages { get; init; }
        = new ReadOnlyDictionary<string, double?>(new Dictionary<string, double?>(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, long> RepeatableEndTimes { get; init; }
        = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> PrerequisiteBlockerIds { get; init; }
        = new ReadOnlyDictionary<string, IReadOnlyCollection<string>>(new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal));

    public IReadOnlyCollection<string> DefaultVisibleQuestIds { get; init; } = [];

    public IReadOnlyCollection<string> ApplicableQuestIds { get; init; } = [];
}

public sealed record QuestServerProfileProjection(
    IReadOnlyDictionary<string, QuestMapDisplayStateKind> DisplayStates,
    IReadOnlyDictionary<string, double?> ProgressPercentages,
    IReadOnlyDictionary<string, long> RepeatableEndTimes,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> PrerequisiteBlockerIds,
    IReadOnlyCollection<string> DefaultVisibleQuestIds,
    IReadOnlyCollection<string> ApplicableQuestIds);

public sealed record QuestNodePosition(string QuestId, int Rank, double X, double Y, double Width, double Height);

public sealed record QuestGraphLayout(
    string TopologyVersion,
    IReadOnlyDictionary<string, QuestNodePosition> NodesById,
    double Width,
    double Height);

public interface IQuestGraphProjection
{
    IReadOnlyList<QuestGraphNode> Nodes { get; }
    IReadOnlyList<QuestGraphEdge> Edges { get; }
    IReadOnlyDictionary<string, QuestNodePosition> NodesById { get; }
    double Width { get; }
    double Height { get; }
}

public enum GlobalQuestGraphMode
{
    InProgress,
    Full,
}

public enum QuestRouteFilter
{
    None,
    Collector,
    Lightkeeper,
}

public enum QuestTableSortColumn
{
    Trader,
    Quest,
    Location,
    Status,
    Progress,
}

public enum QuestTableSortDirection
{
    Ascending,
    Descending,
}

public sealed record QuestTableSortCriterion(
    QuestTableSortColumn Column,
    QuestTableSortDirection Direction);

public sealed record GlobalQuestGraphOptions(
    GlobalQuestGraphMode Mode,
    bool ShowAllFuture,
    bool HideFinished,
    bool LevelEligibleOnly,
    string? ActiveStatusFilter,
    string? TraderId,
    string? Search,
    string? FocusQuestId,
    QuestRouteFilter RouteFilter,
    string? SelectedQuestId = null,
    IReadOnlyCollection<string>? LocationIds = null,
    bool IncludeAvailableRepeatables = false,
    bool TraderTasksContext = false,
    bool TraderGraphContext = false,
    bool ExcludeReadyToFinish = false,
    bool HideUnavailableTraderTasks = false);

public sealed record GlobalQuestGraphProjection(
    GlobalQuestGraphMode Mode,
    IReadOnlyList<QuestGraphNode> Nodes,
    IReadOnlyList<QuestGraphEdge> Edges,
    IReadOnlyDictionary<string, QuestNodePosition> NodesById,
    double Width,
    double Height) : IQuestGraphProjection;

public sealed record QuestGraphSelection(
    string? SelectedQuestId,
    IReadOnlyCollection<string> PrerequisiteQuestIds,
    IReadOnlyCollection<string> DirectSuccessorQuestIds)
{
    public static QuestGraphSelection Empty { get; } = new(null, [], []);

    public QuestNodeHighlightKind GetNodeHighlight(string questId)
    {
        if (string.Equals(SelectedQuestId, questId, StringComparison.Ordinal)) return QuestNodeHighlightKind.Selected;
        if (PrerequisiteQuestIds.Contains(questId)) return QuestNodeHighlightKind.Prerequisite;
        if (DirectSuccessorQuestIds.Contains(questId)) return QuestNodeHighlightKind.DirectSuccessor;
        return QuestNodeHighlightKind.None;
    }
}

public readonly record struct QuestGraphRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Intersects(QuestNodePosition node, double padding = 0) =>
        node.X + node.Width >= X - padding
        && node.X <= Right + padding
        && node.Y + node.Height >= Y - padding
        && node.Y <= Bottom + padding;
}

public readonly record struct QuestGraphPoint(double X, double Y);

public sealed record QuestEdgeRoute(
    QuestGraphEdge Edge,
    QuestGraphPoint Start,
    QuestGraphPoint Control1,
    QuestGraphPoint Control2,
    QuestGraphPoint End);
