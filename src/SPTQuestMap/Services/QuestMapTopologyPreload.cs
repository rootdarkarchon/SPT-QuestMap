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
/// Materializes the final item/location inputs and loose-loot-derived quest-item map lookup once
/// during startup. Location loose-loot snapshots are independent and are materialized in parallel;
/// every later topology build consumes the same stable result.
/// </summary>
[Injectable(InjectionType.Singleton, TypePriority = OnLoadOrder.PostSptModLoader + 0)]
public sealed class QuestMapTopologyPreload(
    DatabaseService databaseService,
    ISptLogger<QuestMapDataService> logger) : IOnLoad
{
    private IReadOnlyDictionary<MongoId, TemplateItem>? _items;
    private Location[]? _locations;
    private IReadOnlyDictionary<MongoId, string[]>? _questItemSpawnMapIds;

    internal IReadOnlyDictionary<MongoId, TemplateItem> Items => _items
        ?? throw new InvalidOperationException("SPT-QuestMap topology preload has not completed.");

    internal IReadOnlyList<Location> Locations => _locations
        ?? throw new InvalidOperationException("SPT-QuestMap topology preload has not completed.");

    internal IReadOnlyDictionary<MongoId, string[]> QuestItemSpawnMapIds => _questItemSpawnMapIds
        ?? throw new InvalidOperationException("SPT-QuestMap topology preload has not completed.");

    public Task OnLoad()
    {
        var totalStopwatch = Stopwatch.StartNew();
        var stageStopwatch = Stopwatch.StartNew();
        var items = databaseService.GetItems();
        var locations = databaseService
            .GetLocations()
            .GetDictionary()
            .Values
            .Where(location => location?.Base?.Id is not null)
            .ToArray();
        var snapshotMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;

        stageStopwatch.Restart();
        var questItemIds = QuestTemplateMapper.BuildQuestItemIdSet(items);
        var itemFilterMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;

        stageStopwatch.Restart();
        var lookup = QuestTemplateMapper.BuildQuestItemSpawnMapLookup(locations, questItemIds);
        var locationScanMilliseconds = stageStopwatch.Elapsed.TotalMilliseconds;

        _items = items;
        _locations = locations;
        _questItemSpawnMapIds = lookup;
        totalStopwatch.Stop();
        logger.Info(
            "QUESTMAP_M08_SERVER_PRELOAD " +
            $"items={items.Count}; questItems={questItemIds.Count}; locations={locations.Length}; " +
            $"processors={Environment.ProcessorCount}; " +
            $"questItemsWithSpawnMaps={lookup.Count}; snapshotMs={snapshotMilliseconds:F2}; " +
            $"itemFilterMs={itemFilterMilliseconds:F2}; locationScanMs={locationScanMilliseconds:F2}; " +
            $"totalMs={totalStopwatch.Elapsed.TotalMilliseconds:F2}");
        return Task.CompletedTask;
    }
}
