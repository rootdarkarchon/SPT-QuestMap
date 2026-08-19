namespace SPTQuestMap.Core.Rules;

public enum QuestTaskTextSize
{
    Small,
    Medium,
    Large,
}

public static class QuestTableLayoutRules
{
    public const float RepeatableBadgeHeight = 24f;
    public const float ProgressActionHeight = 30f;

    public static float TaskFontSize(QuestTaskTextSize size) => size switch
    {
        QuestTaskTextSize.Medium => 13f,
        QuestTaskTextSize.Large => 15f,
        _ => 11f,
    };

    public static float CompactTaskRowHeight(QuestTaskTextSize size) => size switch
    {
        QuestTaskTextSize.Medium => 24f,
        QuestTaskTextSize.Large => 28f,
        _ => 20f,
    };

    public static float ProgressTaskRowHeight(QuestTaskTextSize size) => CompactTaskRowHeight(size) + 10f;

    public static float RaidTrackedTaskFontSize(QuestTaskTextSize size) => TaskFontSize(size) - 1f;

    public static float RaidTrackedProgressFontSize(QuestTaskTextSize size) => TaskFontSize(size) - 2f;

    public static float RaidTrackedTaskRowHeight(QuestTaskTextSize size) => CompactTaskRowHeight(size) - 2f;

    public static float RaidTrackedProgressRowHeight(QuestTaskTextSize size) => CompactTaskRowHeight(size) + 4f;

    public static float RaidPopupTaskFontSize(QuestTaskTextSize size) => TaskFontSize(size);

    public static (float Minimum, float Maximum) ProgressActionSpan(int index, int count)
    {
        if (count is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(count));
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return (index / (float)count, (index + 1) / (float)count);
    }
}
