using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Client.Data;

internal readonly struct InRaidQuestContext
{
    public InRaidQuestContext(string locationId, AbstractQuestControllerClass questController)
    {
        LocationId = locationId;
        QuestController = questController;
    }

    public string LocationId { get; }

    public AbstractQuestControllerClass QuestController { get; }

    public static bool TryCapture(out InRaidQuestContext context)
    {
        context = default;
        if (!Singleton<AbstractGame>.Instantiated || !Singleton<GameWorld>.Instantiated) return false;
        var game = Singleton<AbstractGame>.Instance;
        var world = Singleton<GameWorld>.Instance;
        if (game is null || game.Status != GameStatus.Started
            || world is null || world is HideoutGameWorld
            || world.MainPlayer?.AbstractQuestControllerClass is null) return false;
        var locationId = game.LocationObjectId;
        if (string.IsNullOrWhiteSpace(locationId)) return false;
        context = new InRaidQuestContext(locationId, world.MainPlayer.AbstractQuestControllerClass);
        return true;
    }

    public IReadOnlyCollection<string> DefaultLocationIds(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> mapAliases)
    {
        var locations = QuestObjectiveMapRules.ExpandMapIds([LocationId], mapAliases)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        locations.UnionWith(new[]
        {
            QuestObjectiveMapRules.AnyFilterId,
            QuestObjectiveMapRules.TransitionFilterId,
        });
        return locations;
    }
}
