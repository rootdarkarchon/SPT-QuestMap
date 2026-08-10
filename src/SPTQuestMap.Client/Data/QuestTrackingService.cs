using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTQuestMap.Client.Configuration;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;

namespace SPTQuestMap.Client.Data;

internal sealed class QuestTrackingService : IDisposable
{
    private const int DocumentVersion = 1;
    private readonly QuestMapClientConfiguration _configuration;
    private readonly ManualLogSource _log;
    private readonly string _statePath;
    private readonly string _registryPath;
    private readonly Dictionary<string, HashSet<string>> _manualByProfile = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _favoritesByProfile = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedFavoriteProfiles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _raidLocationIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public QuestTrackingService(QuestMapClientConfiguration configuration, ManualLogSource log)
    {
        _configuration = configuration;
        _log = log;
        var configDirectory = Path.GetDirectoryName(configuration.ConfigFilePath)
            ?? throw new InvalidOperationException("BepInEx config file path has no directory.");
        var gameRoot = Directory.GetParent(configDirectory)?.Parent?.FullName
            ?? throw new InvalidOperationException("BepInEx config directory has no game-root parent.");
        _statePath = Path.Combine(configDirectory, "SPTQuestMap", "tracking-state.json");
        _registryPath = Path.Combine(gameRoot, "SPT", "user", "sptRegistry", "registry.json");
        Load();
        _configuration.AutoTrackNewQuests.SettingChanged += OnPolicyChanged;
        _configuration.TrackFavoriteQuests.SettingChanged += OnPolicyChanged;
        _configuration.AutoTrackMapRelatedQuests.SettingChanged += OnPolicyChanged;
    }

    public event Action<string?, string?>? TrackingChanged;

    public string StatePath => _statePath;

    public QuestTrackingState Resolve(string profileId, QuestGraphNode node)
    {
        EnsureFavoritesLoaded(profileId);
        var manual = _manualByProfile.TryGetValue(profileId, out var manualIds) && manualIds.Contains(node.Id);
        var favorite = _configuration.TrackFavoriteQuests.Value
            && _favoritesByProfile.TryGetValue(profileId, out var favoriteIds)
            && favoriteIds.Contains(node.Id);
        var map = _configuration.AutoTrackMapRelatedQuests.Value
            && _raidLocationIds.Count > 0
            && !node.Location.Any
            && _raidLocationIds.Contains(node.Location.Id);
        return new QuestTrackingState(manual, favorite, map);
    }

    public void ToggleManual(string profileId, string questId)
    {
        var ids = GetManualIds(profileId);
        if (!ids.Add(questId)) ids.Remove(questId);
        Save();
        TrackingChanged?.Invoke(profileId, questId);
    }

    public void ApplyNewQuestTransitions(
        QuestProfileOverlay previous,
        QuestProfileOverlay current,
        IReadOnlyCollection<string> changedQuestIds,
        IReadOnlyDictionary<string, QuestGraphNode> nodesById)
    {
        if (!_configuration.AutoTrackNewQuests.Value
            || !string.Equals(previous.ProfileId, current.ProfileId, StringComparison.Ordinal)) return;

        var ids = GetManualIds(current.ProfileId);
        var added = new List<string>();
        foreach (var questId in changedQuestIds)
        {
            current.QuestsById.TryGetValue(questId, out var currentState);
            previous.QuestsById.TryGetValue(questId, out var previousState);
            if (!IsActiveStatus(currentState?.ExactStatus) || IsActiveStatus(previousState?.ExactStatus)) continue;
            if (!nodesById.TryGetValue(questId, out var node))
            {
                _log.LogWarning($"QUESTMAP_M06_TRACKING autoTrackSkipped=True; reason=missing-topology; quest={questId}");
                continue;
            }
            if (!QuestAutoTrackingRules.ShouldAutoTrackNewQuest(
                    node.Location.Any,
                    node.Objectives.Select(objective => objective.ConditionType)))
            {
                QuestMapDebugLog.Info(_log,
                    $"QUESTMAP_M06_TRACKING autoTrackSkipped=True; reason=passive-any-objectives; quest={questId}");
                continue;
            }
            if (ids.Add(questId)) added.Add(questId);
        }

        if (added.Count == 0) return;
        Save();
        foreach (var questId in added) TrackingChanged?.Invoke(current.ProfileId, questId);
        QuestMapDebugLog.Info(_log,
            $"QUESTMAP_M06_TRACKING autoTracked={added.Count}; profile={current.ProfileId}; quests={string.Join(",", added)}");
    }

    public void BeginRaid(IReadOnlyCollection<string> locationIds)
    {
        if (_raidLocationIds.SetEquals(locationIds)) return;
        _raidLocationIds.Clear();
        _raidLocationIds.UnionWith(locationIds);
        TrackingChanged?.Invoke(null, null);
    }

    public void EndRaid()
    {
        if (_raidLocationIds.Count == 0) return;
        _raidLocationIds.Clear();
        TrackingChanged?.Invoke(null, null);
    }

    public void RefreshFavoriteSnapshot(
        string profileId,
        IEnumerable<QuestGraphNode> nodes,
        Func<string, bool> isFavorite,
        bool notify)
    {
        var next = nodes.Where(node => isFavorite(node.Id)).Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        _favoritesByProfile.TryGetValue(profileId, out var previous);
        var changed = previous is null
            ? new HashSet<string>(next, StringComparer.Ordinal)
            : new HashSet<string>(previous, StringComparer.Ordinal);
        if (previous is not null) changed.SymmetricExceptWith(next);
        _favoritesByProfile[profileId] = next;
        _loadedFavoriteProfiles.Add(profileId);
        if (!notify) return;
        foreach (var questId in changed) TrackingChanged?.Invoke(profileId, questId);
    }

    public void EnsureFavoritesLoaded(string profileId)
    {
        if (_loadedFavoriteProfiles.Contains(profileId)) return;
        _loadedFavoriteProfiles.Add(profileId);
        var favorites = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(_registryPath))
            {
                var registry = JObject.Parse(File.ReadAllText(_registryPath));
                var serialized = registry.Value<string>($"favorite_quests_{profileId}");
                if (!string.IsNullOrWhiteSpace(serialized))
                {
                    favorites.UnionWith(JsonConvert.DeserializeObject<string[]>(serialized!) ?? []);
                }
            }
        }
        catch (Exception exception)
        {
            _log.LogWarning($"QUESTMAP_M06_TRACKING favoriteLoadFailed=True; profile={profileId}; {exception.Message}");
        }
        _favoritesByProfile[profileId] = favorites;
    }

    public void ReloadFavorites(string profileId)
    {
        _loadedFavoriteProfiles.Remove(profileId);
        EnsureFavoritesLoaded(profileId);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _configuration.AutoTrackNewQuests.SettingChanged -= OnPolicyChanged;
        _configuration.TrackFavoriteQuests.SettingChanged -= OnPolicyChanged;
        _configuration.AutoTrackMapRelatedQuests.SettingChanged -= OnPolicyChanged;
        TrackingChanged = null;
    }

    private void OnPolicyChanged(object? sender, EventArgs args) => TrackingChanged?.Invoke(null, null);

    private HashSet<string> GetManualIds(string profileId)
    {
        if (_manualByProfile.TryGetValue(profileId, out var ids)) return ids;
        ids = new HashSet<string>(StringComparer.Ordinal);
        _manualByProfile.Add(profileId, ids);
        return ids;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_statePath)) return;
            var document = JsonConvert.DeserializeObject<TrackingDocument>(File.ReadAllText(_statePath));
            if (document?.Version != DocumentVersion || document.Profiles is null) return;
            foreach (var pair in document.Profiles)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                _manualByProfile[pair.Key] = (pair.Value ?? [])
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.Ordinal);
            }
        }
        catch (Exception exception)
        {
            _log.LogWarning($"QUESTMAP_M06_TRACKING stateLoadFailed=True; path={_statePath}; {exception.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_statePath)
                ?? throw new InvalidOperationException("Tracking state path has no directory.");
            Directory.CreateDirectory(directory);
            var document = new TrackingDocument
            {
                Version = DocumentVersion,
                Profiles = _manualByProfile.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal),
            };
            File.WriteAllText(_statePath, JsonConvert.SerializeObject(document, Formatting.Indented));
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M06_TRACKING stateSaveFailed=True; path={_statePath}; {exception}");
        }
    }

    private static bool IsActiveStatus(string? status) =>
        string.Equals(status, "Started", StringComparison.Ordinal)
        || string.Equals(status, "AvailableForFinish", StringComparison.Ordinal);

    private sealed class TrackingDocument
    {
        public int Version { get; set; }
        public Dictionary<string, string[]>? Profiles { get; set; }
    }
}

internal readonly struct QuestTrackingState
{
    public QuestTrackingState(bool manual, bool favoritePolicy, bool mapPolicy)
    {
        Manual = manual;
        FavoritePolicy = favoritePolicy;
        MapPolicy = mapPolicy;
    }

    public bool Manual { get; }
    public bool FavoritePolicy { get; }
    public bool MapPolicy { get; }
    public bool Tracked => Manual || FavoritePolicy || MapPolicy;
    public bool Implicit => Tracked && !Manual;
}
