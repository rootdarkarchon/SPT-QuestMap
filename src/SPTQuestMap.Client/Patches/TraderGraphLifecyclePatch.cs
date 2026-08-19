using EFT.InventoryLogic;
using EFT.UI;
using SPTQuestMap.Client.Data;

namespace SPTQuestMap.Client.Patches;

internal static class TraderGraphLifecyclePatch
{
    private static QuestMapDataRuntime? _runtime;

    public static void Configure(QuestMapDataRuntime? runtime)
    {
        _runtime = runtime;
    }

    public static void ShowPostfix(
        QuestsScreen __instance,
        ISession backendSession,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        TraderClass trader)
    {
        _runtime?.ShowTraderScreen(__instance, backendSession, inventoryController, questController, trader);
    }

    public static void ClosePrefix(QuestsScreen __instance)
    {
        _runtime?.CloseTraderScreen(__instance);
    }
}
