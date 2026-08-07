using EFT.UI;
using SPTQuestMap.Client.Data;

namespace SPTQuestMap.Client.Patches;

internal static class GlobalTasksLifecyclePatch
{
    private static QuestMapDataRuntime? _runtime;

    public static void Configure(QuestMapDataRuntime? runtime)
    {
        _runtime = runtime;
    }

    public static void ShowPostfix(TasksScreen __instance, object[] __args)
    {
        _runtime?.ShowGlobalTasksScreen(__instance, __args);
    }

    public static void ClosePrefix(TasksScreen __instance)
    {
        _runtime?.CloseGlobalTasksScreen(__instance);
    }
}
