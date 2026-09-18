using System;
using System.Linq;

namespace SPTQuestMap.Client.Patches;

internal static class QuestDataLifecyclePatch
{
    private static Action<EFT.Quests.QuestController>? _observeQuestController;

    public static void Configure(Action<EFT.Quests.QuestController>? observeQuestController)
    {
        _observeQuestController = observeQuestController;
    }

    public static void Postfix(object[] __args)
    {
        var questController = __args.OfType<EFT.Quests.QuestController>().SingleOrDefault();
        if (questController is not null) _observeQuestController?.Invoke(questController);
    }
}
