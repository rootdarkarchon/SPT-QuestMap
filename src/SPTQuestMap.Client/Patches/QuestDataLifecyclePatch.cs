using System;
using System.Linq;

namespace SPTQuestMap.Client.Patches;

internal static class QuestDataLifecyclePatch
{
    private static Action<AbstractQuestControllerClass>? _observeQuestController;

    public static void Configure(Action<AbstractQuestControllerClass>? observeQuestController)
    {
        _observeQuestController = observeQuestController;
    }

    public static void Postfix(object[] __args)
    {
        var questController = __args.OfType<AbstractQuestControllerClass>().SingleOrDefault();
        if (questController is not null) _observeQuestController?.Invoke(questController);
    }
}
