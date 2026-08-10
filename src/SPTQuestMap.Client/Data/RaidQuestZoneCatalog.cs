using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EFT.Interactive;
using UnityEngine;

namespace SPTQuestMap.Client.Data;

internal sealed class RaidQuestZoneCatalog
{
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, IReadOnlyCollection<string>> _zonesByMap =
        new(StringComparer.OrdinalIgnoreCase);

    public RaidQuestZoneCatalog(ManualLogSource log)
    {
        _log = log;
    }

    public IReadOnlyCollection<string> CaptureOnce(string mapId)
    {
        if (_zonesByMap.TryGetValue(mapId, out var cached))
        {
            QuestMapDebugLog.Info(_log,
                $"QUESTMAP_M06_RAID_ZONES map={mapId}; cached=True; zones={cached.Count}");
            return cached;
        }

        IReadOnlyCollection<string> zones;
        try
        {
            zones = UnityEngine.Object.FindObjectsOfType<TriggerWithId>()
                .Select(trigger => trigger.Id)
                .Where(zoneId => !string.IsNullOrWhiteSpace(zoneId))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(zoneId => zoneId, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception)
        {
            zones = Array.Empty<string>();
            _log.LogWarning(
                $"QUESTMAP_M06_RAID_ZONES map={mapId}; scanFailed=True; zones=0; {exception.Message}");
        }

        _zonesByMap.Add(mapId, zones);
        QuestMapDebugLog.Info(_log,
            $"QUESTMAP_M06_RAID_ZONES map={mapId}; cached=False; zones={zones.Count}");
        return zones;
    }
}
