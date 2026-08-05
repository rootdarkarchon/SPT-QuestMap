using SPTarkov.Server.Core.Models.Enums;

namespace SPTQuestMap.Services;

internal static class RepeatableQuestRules
{
    internal static bool ShouldIncludeGroup(string? name) => name is "Daily" or "Weekly";

    internal static QuestStatusEnum ExactStatus(int? status) =>
        status is >= (int)QuestStatusEnum.Locked and <= (int)QuestStatusEnum.AvailableAfter
            ? (QuestStatusEnum)status.Value
            : QuestStatusEnum.AvailableForStart;

    internal static string Classify(QuestStatusEnum? status, bool expired)
    {
        if (expired) return "Expired";
        return status switch
        {
            QuestStatusEnum.AvailableForStart => "Available",
            QuestStatusEnum.Started => "InProgress",
            QuestStatusEnum.AvailableForFinish => "ReadyToFinish",
            QuestStatusEnum.Success => "Completed",
            QuestStatusEnum.Fail or QuestStatusEnum.MarkedAsFailed => "Failed",
            QuestStatusEnum.FailRestartable => "RestartableFailure",
            QuestStatusEnum.Expired => "Expired",
            QuestStatusEnum.AvailableAfter => "Pending",
            _ => "Locked",
        };
    }
}
