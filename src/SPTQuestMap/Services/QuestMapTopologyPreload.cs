using System.Diagnostics;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace SPTQuestMap.Services;

/// <summary>
/// Materializes the item/location inputs and loose-loot-derived quest-item map lookup at the
/// beginning of the post-database phase. This deliberately precedes later loose-loot transformers,
/// keeps materialization out of the first /questmap request, and gives every later topology build
/// one stable startup snapshot.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostDBModLoader + 0)]
public sealed class QuestMapTopologyPreload(
    DatabaseService databaseService,
    ISptLogger<QuestMapDataService> logger) : IOnLoad
{
    private IReadOnlyDictionary<MongoId, TemplateItem>? _items;
    private Location[]? _locations;
    private IReadOnlyDictionary<string, string[]>? _questItemSpawnMapIds;

    internal IReadOnlyDictionary<MongoId, TemplateItem> Items => _items
        ?? throw new InvalidOperationException("SPT-QuestMap topology preload has not completed.");

    internal IReadOnlyList<Location> Locations => _locations
        ?? throw new InvalidOperationException("SPT-QuestMap topology preload has not completed.");

    internal IReadOnlyDictionary<string, string[]> QuestItemSpawnMapIds => _questItemSpawnMapIds
        ?? throw new InvalidOperationException("SPT-QuestMap topology preload has not completed.");

    public Task OnLoad()
    {
        var stopwatch = Stopwatch.StartNew();
        var items = databaseService.GetItems();
        var locations = databaseService
            .GetLocations()
            .GetDictionary()
            .Values
            .Where(location => location?.Base?.Id is not null)
            .ToArray();
        var lookup = QuestTemplateMapper.BuildQuestItemSpawnMapLookup(locations, items);

        _items = items;
        _locations = locations;
        _questItemSpawnMapIds = lookup;
        stopwatch.Stop();
        logger.Info(
            "QUESTMAP_M08_SERVER_PRELOAD " +
            $"items={items.Count}; locations={locations.Length}; questItemsWithSpawnMaps={lookup.Count}; " +
            $"totalMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
        return Task.CompletedTask;
    }
}
