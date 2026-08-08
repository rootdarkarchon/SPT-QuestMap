using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;

namespace SPTQuestMap.Client.Data;

internal readonly struct InRaidQuestContext
{
    private const string FactoryDay = "55f2d3fd4bdc2d5f408b4567";
    private const string FactoryNight = "59fc81d786f774390775787e";
    private const string GroundZero = "653e6760052c01c1c805532f";
    private const string GroundZeroHigh = "65b8d6f5cdde2479cb2a3125";

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

    public IReadOnlyCollection<string> DefaultLocationIds()
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "any",
            "marathon",
            LocationId,
        };
        if (string.Equals(LocationId, FactoryNight, StringComparison.OrdinalIgnoreCase)) locations.Add(FactoryDay);
        if (string.Equals(LocationId, GroundZeroHigh, StringComparison.OrdinalIgnoreCase)) locations.Add(GroundZero);
        return locations;
    }

    public IReadOnlyCollection<string> TrackingLocationIds()
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { LocationId };
        if (string.Equals(LocationId, FactoryNight, StringComparison.OrdinalIgnoreCase)) locations.Add(FactoryDay);
        if (string.Equals(LocationId, GroundZeroHigh, StringComparison.OrdinalIgnoreCase)) locations.Add(GroundZero);
        return locations;
    }
}
