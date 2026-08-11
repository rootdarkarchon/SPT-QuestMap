using System.Text.Json.Serialization;

namespace SPTQuestMap.Services;

public sealed record QuestTopologyDto(
    string Version,
    IReadOnlyList<QuestNodeDto> Quests,
    IReadOnlyList<QuestEdgeDto> Edges,
    IReadOnlyList<QuestTraderDto> Traders,
    string[] CollectorPathQuestIds,
    string[] LightkeeperPathQuestIds
);

public sealed record QuestTraderDto(string Id, string Name, string? ImageUrl);

public sealed record QuestNodeDto(
    string Id,
    string Name,
    string Description,
    string TraderId,
    string TraderName,
    string? TraderImageUrl,
    string ImageUrl,
    string Faction,
    QuestLocationDto Location,
    string? EventSeason,
    bool Restartable,
    RequirementDto[] DirectRequirements,
    RequirementDto[] EffectiveRequirements,
    ObjectiveDefinitionDto[] Objectives,
    QuestExclusionRuleDto[] ExclusionRules,
    QuestRewardDto[] Rewards
)
{
    public UnknownConditionDto[] UnknownConditions { get; init; } = [];

    public bool ScavRepeatable { get; init; }

    [JsonIgnore]
    public string? Summary { get; init; }

    [JsonIgnore]
    public string? WikiUrl { get; init; }

    [JsonIgnore]
    public QuestRelevantItemDto[] RelevantItems { get; init; } = [];
}

public sealed record QuestRelevantItemDto(string TemplateId, string Name, bool FleaEligible);

public sealed record QuestMetaInfoDto(string WikiUrl, QuestRelevantItemDto[] RelevantItems);

public sealed record QuestLocationDto(string Id, string? Name, bool Any, string? BannerImageUrl);

public sealed record QuestEdgeDto(string SourceId, string TargetId, string[] RequiredStatuses, int AvailableAfterSeconds)
{
    public string RequirementKind => QuestGraphRules.ClassifyEdgeRequirement(RequiredStatuses);
}

public sealed record RequirementDto(string Kind, string? TraderId, string Compare, double Value);
public sealed record ObjectiveDefinitionDto(
    string Id,
    string Text,
    string ConditionType,
    int? Index,
    string? ParentId,
    double? RequiredValue,
    string? Compare,
    string[] DependsOn,
    string[]? ZoneIds = null,
    bool OneSessionOnly = false,
    bool DoNotResetIfCounterCompleted = false);
public sealed record QuestRewardDto(string Id, string Type, string? TargetId, string? TargetName, double? Value, int? LoyaltyLevel, string? TraderName, bool Unknown, bool Hidden, QuestRewardItemDto[] Items);
public sealed record QuestRewardItemDto(string TemplateId, string Name, double Count);
public sealed record QuestExclusionRuleDto(string CausedByQuestId, string[] RequiredStatuses);
public sealed record UnknownConditionDto(string Stage, string ConditionType, string? ConditionId);
public sealed record ProfileStateDto(string ProfileId, string Nickname, string Side, int Level, long GeneratedAt, bool ChristmasActive, bool HalloweenActive, IReadOnlyList<QuestStateDto> Quests, IReadOnlyList<TraderStateDto> Traders, string[] DefaultVisibleQuestIds, string[] AllApplicableQuestIds)
{
    public IReadOnlyList<RepeatableQuestGroupDto> RepeatableQuestGroups { get; init; } = [];
}
public sealed record RepeatableQuestGroupDto(string Kind, long EndTime, IReadOnlyList<RepeatableQuestEntryDto> Quests)
{
    public bool Scav { get; init; }
}
public sealed record RepeatableQuestEntryDto(QuestNodeDto Node, QuestStateDto State);
public sealed record QuestStateDto(string QuestId, string? ExactStatus, string DisplayState, bool AuthoritativelyVisible, bool InProfile, double? AvailableAfter, QuestBlockerDto[] Blockers, QuestExclusionDto? Exclusion, ObjectiveProgressDto[] Objectives, double? ProgressPercent);
public sealed record QuestBlockerDto(string Kind, string? SubjectId, string? Compare, double? RequiredValue, string[] RequiredStatuses);
public sealed record QuestExclusionDto(string CausedByQuestId, string CauseStatus, bool Permanent);
public sealed record ObjectiveProgressDto(string ObjectiveId, bool Complete, double? Current, double? Required, bool ProgressKnown);
public sealed record TraderStateDto(string TraderId, bool Available, int? LoyaltyLevel, double? Standing, double? SalesSum);
public sealed record QuestMapBootstrapDto(string Language, string BrowserLocale, QuestMapLanguageDto[] Languages, IReadOnlyDictionary<string, string> Strings);
public sealed record QuestMapLanguageDto(string Code, string Name);
public sealed record ProfileSummaryDto(string Id, string Nickname, string Side, int Level);
public sealed record QuestMapClientTopologyFeedDto(
    QuestTopologyDto Topology,
    QuestNodeDto[] ProfileGeneratedQuests,
    IReadOnlyDictionary<string, string> RepeatableKinds,
    string[] DefaultVisibleQuestIds,
    string[] AllApplicableQuestIds,
    IReadOnlyDictionary<string, string> DisplayStates,
    IReadOnlyDictionary<string, double?> ProgressPercentages,
    IReadOnlyDictionary<string, long> RepeatableEndTimes,
    IReadOnlyDictionary<string, string[]> PrerequisiteBlockerIds,
    IReadOnlyDictionary<string, string> QuestSummaries,
    IReadOnlyDictionary<string, QuestMetaInfoDto> QuestMetaInfo);
public sealed record QuestMapClientRepeatableFeedDto(
    QuestNodeDto[] ProfileGeneratedQuests,
    IReadOnlyDictionary<string, string> RepeatableKinds,
    string[] DefaultVisibleQuestIds,
    string[] AllApplicableQuestIds,
    IReadOnlyDictionary<string, string> DisplayStates,
    IReadOnlyDictionary<string, double?> ProgressPercentages,
    IReadOnlyDictionary<string, long> RepeatableEndTimes,
    IReadOnlyDictionary<string, string[]> PrerequisiteBlockerIds,
    IReadOnlyDictionary<string, string> QuestSummaries,
    IReadOnlyDictionary<string, QuestMetaInfoDto> QuestMetaInfo);
