namespace SPTQuestMap.Core.Transport;

public sealed class QuestTopologyFeed
{
    public QuestTopologyPayload Topology { get; set; } = new();

    public QuestNodePayload[] ProfileGeneratedQuests { get; set; } = [];
}

public sealed class QuestTopologyPayload
{
    public string Version { get; set; } = string.Empty;

    public QuestNodePayload[] Quests { get; set; } = [];

    public QuestEdgePayload[] Edges { get; set; } = [];

    public QuestTraderPayload[] Traders { get; set; } = [];
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
    public QuestRequirementPayload[] DirectRequirements { get; set; } = [];
    public QuestRequirementPayload[] EffectiveRequirements { get; set; } = [];
    public QuestObjectivePayload[] Objectives { get; set; } = [];
    public QuestExclusionPayload[] ExclusionRules { get; set; } = [];
    public QuestRewardPayload[] Rewards { get; set; } = [];
    public QuestUnknownConditionPayload[] UnknownConditions { get; set; } = [];
}

public sealed class QuestLocationPayload
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool Any { get; set; }
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
