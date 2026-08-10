using System.Text.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;
using IOPath = System.IO.Path;

namespace SPTQuestMap.Services;

internal sealed class QuestMetaInfoCatalog
{
    internal const string ExternalRelativePath = "SPT/user/mods/SPT-QuestMap/Data/metainfo.json";
    private readonly object _resolveLock = new();
    private readonly Action<string>? _warning;
    private readonly IReadOnlyDictionary<string, ParsedQuestMetaInfo> _parsedEntries;
    private IReadOnlyDictionary<string, CachedQuestMetaInfo>? _entries;

    internal QuestMetaInfoCatalog(
        string? path = null,
        Action<string>? warning = null)
    {
        _warning = warning;
        _parsedEntries = Parse(ResolvePath(path), warning);
    }

    internal QuestMetaInfoCatalog(
        string? path,
        Func<string, CachedRelevantItem?> resolveItem,
        Action<string>? warning = null)
        : this(path, warning)
    {
        ArgumentNullException.ThrowIfNull(resolveItem);
        Resolve(resolveItem);
    }

    internal void Resolve(DatabaseService databaseService)
    {
        ArgumentNullException.ThrowIfNull(databaseService);
        var items = databaseService.GetItems();
        Resolve(itemId => ResolveItem(items, itemId));
    }

    internal QuestMetaInfoDto? Get(
        string questId,
        IReadOnlyDictionary<string, string> locale)
    {
        var entries = _entries
            ?? throw new InvalidOperationException("SPT-QuestMap quest metadata was requested before its post-database startup initialization.");
        if (!entries.TryGetValue(questId, out var entry)) return null;
        return new QuestMetaInfoDto(
            entry.WikiUrl,
            entry.RelevantItems
                .Select(item => new QuestRelevantItemDto(
                    item.TemplateId,
                    locale.GetValueOrDefault($"{item.TemplateId} Name", item.FallbackName),
                    item.FleaEligible))
                .ToArray());
    }

    internal void Resolve(Func<string, CachedRelevantItem?> resolveItem)
    {
        lock (_resolveLock)
        {
            if (_entries is not null) return;
            var entries = new Dictionary<string, CachedQuestMetaInfo>(StringComparer.Ordinal);
            foreach (var (questId, parsed) in _parsedEntries)
            {
                var items = new List<CachedRelevantItem>();
                foreach (var itemId in parsed.RelevantItemIds)
                {
                    var resolved = resolveItem(itemId);
                    if (resolved is null)
                    {
                        _warning?.Invoke($"SPT-QuestMap: relevant item '{itemId}' for quest '{questId}' could not be resolved and was omitted.");
                        continue;
                    }

                    items.Add(resolved);
                }

                entries[questId] = new CachedQuestMetaInfo(parsed.WikiUrl, items.ToArray());
            }

            _entries = entries;
        }
    }

    private static IReadOnlyDictionary<string, ParsedQuestMetaInfo> Parse(
        string path,
        Action<string>? warning)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("SPT-QuestMap authoritative quest metadata was not found.", path);
        }

        using var stream = File.OpenRead(path);
        var rawEntries = JsonSerializer.Deserialize<Dictionary<string, RawQuestMetaInfo>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new JsonException($"Quest metadata catalog '{path}' is empty.");
        var entries = new Dictionary<string, ParsedQuestMetaInfo>(StringComparer.Ordinal);
        foreach (var (questId, raw) in rawEntries)
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                warning?.Invoke("SPT-QuestMap: quest metadata contains an empty quest ID; skipping the entry.");
                continue;
            }

            if (!TryNormalizeWikiUrl(raw.Wiki, out var wikiUrl))
            {
                warning?.Invoke($"SPT-QuestMap: quest metadata for '{questId}' has an invalid HTTP(S) wiki URL; skipping the entry.");
                continue;
            }

            var itemIds = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var itemId in raw.RelevantItems ?? [])
            {
                if (string.IsNullOrWhiteSpace(itemId) || !seen.Add(itemId)) continue;
                itemIds.Add(itemId);
            }

            entries[questId] = new ParsedQuestMetaInfo(wikiUrl, itemIds.ToArray());
        }

        return entries;
    }

    private static string ResolvePath(string? path) => path ?? IOPath.Combine(
        IOPath.GetDirectoryName(typeof(QuestMetaInfoCatalog).Assembly.Location)
            ?? throw new InvalidOperationException("SPT-QuestMap assembly directory is unavailable."),
        "Data",
        "metainfo.json");

    private static CachedRelevantItem? ResolveItem(
        IReadOnlyDictionary<MongoId, TemplateItem> items,
        string itemId)
    {
        if (!MongoId.IsValidMongoId(itemId)) return null;
        if (!items.TryGetValue(new MongoId(itemId), out var item)) return null;
        return new CachedRelevantItem(
            itemId,
            item.Name ?? itemId,
            item.Properties?.CanSellOnRagfair == true);
    }

    private static bool TryNormalizeWikiUrl(string? value, out string normalized)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            normalized = uri.AbsoluteUri;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    internal sealed record CachedRelevantItem(
        string TemplateId,
        string FallbackName,
        bool FleaEligible);

    private sealed record CachedQuestMetaInfo(
        string WikiUrl,
        CachedRelevantItem[] RelevantItems);

    private sealed record ParsedQuestMetaInfo(
        string WikiUrl,
        string[] RelevantItemIds);

    private sealed class RawQuestMetaInfo
    {
        public string? Wiki { get; init; }

        public string[]? RelevantItems { get; init; }
    }
}
