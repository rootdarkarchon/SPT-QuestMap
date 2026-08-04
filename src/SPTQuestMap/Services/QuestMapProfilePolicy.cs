namespace SPTQuestMap.Services;

internal static class QuestMapProfilePolicy
{
    internal static bool ShouldInclude(string? nickname, int level) =>
        level > 0 && nickname is not null && !nickname.StartsWith("headless_", StringComparison.OrdinalIgnoreCase);
}
