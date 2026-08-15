using System.Reflection;
using System.Text.Json;

namespace SPTQuestMap.Services;

internal sealed class QuestZoneMapCatalog
{
    private readonly IReadOnlyDictionary<string, string[]> _mapIdsByZoneId;

    internal QuestZoneMapCatalog(string? catalogPath = null)
    {
        var path = catalogPath ?? DefaultCatalogPath();
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("SPT-QuestMap zone-to-map catalog was not found.", path);
        }

        Dictionary<string, string[]> source;
        try
        {
            source = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))
                ?? throw new InvalidDataException("The zone-to-map catalog must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"SPT-QuestMap zone-to-map catalog '{path}' is not valid JSON.", exception);
        }

        var mapIdsByZoneId = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (rawMapId, rawZoneIds) in source)
        {
            var mapId = rawMapId?.Trim();
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new InvalidDataException($"SPT-QuestMap zone-to-map catalog '{path}' contains an empty map ID.");
            }

            foreach (var rawZoneId in rawZoneIds ?? [])
            {
                var zoneId = rawZoneId?.Trim();
                if (string.IsNullOrWhiteSpace(zoneId)) continue;
                if (!mapIdsByZoneId.TryGetValue(zoneId, out var mapIds))
                {
                    mapIds = new HashSet<string>(StringComparer.Ordinal);
                    mapIdsByZoneId[zoneId] = mapIds;
                }

                mapIds.Add(mapId);
            }
        }

        _mapIdsByZoneId = mapIdsByZoneId.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    internal QuestZoneMapResolution Resolve(
        IEnumerable<string> zoneIds,
        string? preferredMapId = null)
    {
        var mapIds = new HashSet<string>(StringComparer.Ordinal);
        var unresolvedZoneIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zoneId in zoneIds.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
        {
            if (!_mapIdsByZoneId.TryGetValue(zoneId, out var resolvedMapIds))
            {
                unresolvedZoneIds.Add(zoneId);
                continue;
            }

            var preferredMatch = resolvedMapIds.Length > 1 && !string.IsNullOrWhiteSpace(preferredMapId)
                ? resolvedMapIds.FirstOrDefault(mapId => mapId.Equals(
                    preferredMapId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (preferredMatch is not null) mapIds.Add(preferredMatch);
            else mapIds.UnionWith(resolvedMapIds);
        }

        return new QuestZoneMapResolution(
            mapIds.Order(StringComparer.Ordinal).ToArray(),
            unresolvedZoneIds.Order(StringComparer.Ordinal).ToArray());
    }

    private static string DefaultCatalogPath() => Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            ?? throw new InvalidOperationException("Unable to resolve the SPT-QuestMap assembly directory."),
        "Data",
        "triggerIds.json");
}

internal sealed record QuestZoneMapResolution(string[] MapIds, string[] UnresolvedZoneIds);
