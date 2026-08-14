namespace SPTQuestMap.Core.Transport;

public sealed class QuestTopologyFeed
{
    public QuestTopologyPayload Topology { get; set; } = new();

    public QuestNodePayload[] ProfileGeneratedQuests { get; set; } = [];

    public Dictionary<string, string> RepeatableKinds { get; set; } = new(StringComparer.Ordinal);

    public string[] DefaultVisibleQuestIds { get; set; } = [];

    public string[] AllApplicableQuestIds { get; set; } = [];

    public Dictionary<string, string> DisplayStates { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, double?> ProgressPercentages { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, long> RepeatableEndTimes { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string[]> PrerequisiteBlockerIds { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> QuestSummaries { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, QuestMetaInfoPayload> QuestMetaInfo { get; set; } = new(StringComparer.Ordinal);
}

public sealed class QuestRepeatableFeed
{
    public string StaticTopologyVersion { get; set; } = string.Empty;
    public QuestNodePayload[] ProfileGeneratedQuests { get; set; } = [];
    public Dictionary<string, string> RepeatableKinds { get; set; } = new(StringComparer.Ordinal);
    public string[] DefaultVisibleQuestIds { get; set; } = [];
    public string[] AllApplicableQuestIds { get; set; } = [];
    public Dictionary<string, string> DisplayStates { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, double?> ProgressPercentages { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, long> RepeatableEndTimes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string[]> PrerequisiteBlockerIds { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> QuestSummaries { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, QuestMetaInfoPayload> QuestMetaInfo { get; set; } = new(StringComparer.Ordinal);
}

public sealed class QuestMetaInfoPayload
{
    public string WikiUrl { get; set; } = string.Empty;
    public QuestRelevantItemPayload[] RelevantItems { get; set; } = [];
}

public sealed class QuestRelevantItemPayload
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool FleaEligible { get; set; }
}

public sealed class QuestTopologyPayload
{
    public string Version { get; set; } = string.Empty;

    public QuestNodePayload[] Quests { get; set; } = [];

    public QuestEdgePayload[] Edges { get; set; } = [];

    public QuestTraderPayload[] Traders { get; set; } = [];

    public string[] CollectorPathQuestIds { get; set; } = [];

    public string[] LightkeeperPathQuestIds { get; set; } = [];

    public Dictionary<string, string[]> MapAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class QuestTraderPayload
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }
}

public sealed class QuestNodePayload
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TraderId { get; set; } = string.Empty;
    public string TraderName { get; set; } = string.Empty;
    public string? TraderImageUrl { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Faction { get; set; } = "Any";
    public QuestLocationPayload Location { get; set; } = new();
    public string? EventSeason { get; set; }
    public bool Restartable { get; set; }
    public bool ScavRepeatable { get; set; }
    public QuestRequirementPayload[] DirectRequirements { get; set; } = [];
    public QuestRequirementPayload[] EffectiveRequirements { get; set; } = [];
    public QuestObjectivePayload[] Objectives { get; set; } = [];
    public QuestExclusionPayload[] ExclusionRules { get; set; } = [];
    public QuestRewardPayload[] Rewards { get; set; } = [];
    public QuestUnknownConditionPayload[] UnknownConditions { get; set; } = [];
    public QuestMapReferencePayload TaskLocation { get; set; } = new();
    public QuestMapReferencePayload[] ActualMaps { get; set; } = [];
    public bool ActualMapsComplete { get; set; }
}

public sealed class QuestLocationPayload
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool Any { get; set; }
    public string? BannerImageUrl { get; set; }
}

public sealed class QuestMapReferencePayload
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? BannerImageUrl { get; set; }
}

public sealed class QuestEdgePayload
{
    public string SourceId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string[] RequiredStatuses { get; set; } = [];
    public int AvailableAfterSeconds { get; set; }
}

public sealed class QuestRequirementPayload
{
    public string Kind { get; set; } = string.Empty;
    public string? TraderId { get; set; }
    public string Compare { get; set; } = string.Empty;
    public double Value { get; set; }
}

public sealed class QuestObjectivePayload
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string ConditionType { get; set; } = string.Empty;
    public int? Index { get; set; }
    public string? ParentId { get; set; }
    public double? RequiredValue { get; set; }
    public string? Compare { get; set; }
    public string[] DependsOn { get; set; } = [];
    public string[] ZoneIds { get; set; } = [];
    public string[] MapIds { get; set; } = [];
    public string[] UnresolvedZoneIds { get; set; } = [];
    public bool InRaidRelevant { get; set; } = true;
    public QuestMapReferencePayload[] TaskLocations { get; set; } = [];
    public bool OneSessionOnly { get; set; }
    public bool DoNotResetIfCounterCompleted { get; set; }
    public bool ContributesToProgress { get; set; } = true;
}

public sealed class QuestUnknownConditionPayload
{
    public string Stage { get; set; } = string.Empty;
    public string ConditionType { get; set; } = string.Empty;
    public string? ConditionId { get; set; }
}

public sealed class QuestExclusionPayload
{
    public string CausedByQuestId { get; set; } = string.Empty;
    public string[] RequiredStatuses { get; set; } = [];
}

public sealed class QuestRewardPayload
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? TargetId { get; set; }
    public string? TargetName { get; set; }
    public double? Value { get; set; }
    public int? LoyaltyLevel { get; set; }
    public string? TraderName { get; set; }
    public bool Unknown { get; set; }
    public bool Hidden { get; set; }
    public QuestRewardItemPayload[] Items { get; set; } = [];
}

public sealed class QuestRewardItemPayload
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Count { get; set; }
}
