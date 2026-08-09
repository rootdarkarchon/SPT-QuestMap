namespace SPTQuestMap.Core.Rules;

/// <summary>
/// Canonical EFT trader order shared by the server page and native client.
/// Unknown/modded traders sort after the built-in sequence.
/// </summary>
public static class QuestTraderOrder
{
    public static readonly IReadOnlyList<string> Ids =
    [
        "54cb50c76803fa8b248b4571", "54cb57776803fa99248b456e", "579dc571d53a0658a154fbec",
        "58330581ace78e27b8b10cee", "5935c25fb3acc3127c3d8cd9", "5a7c2eca46aef81a7ca2145d",
        "5ac3b934156ae10c4430e83c", "5c0647fdd443bc2504c2d371", "6617beeaa9cfa777ca915b7c",
        "638f541a29ffd1183d187f57", "656f0f98d80a697f855d34b1",
    ];

    public static int Rank(string traderId)
    {
        for (var index = 0; index < Ids.Count; index++)
        {
            if (string.Equals(Ids[index], traderId, StringComparison.Ordinal)) return index;
        }

        return int.MaxValue;
    }
}
