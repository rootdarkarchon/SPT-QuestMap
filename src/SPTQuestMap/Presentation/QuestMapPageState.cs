using SPTQuestMap.Services;

namespace SPTQuestMap.Presentation;

public sealed class QuestMapPageState
{
    private readonly Dictionary<string, QuestProfileUiSettings> _profileSettings = new(StringComparer.Ordinal);
    private Dictionary<string, QuestNodeDto> _nodeById = new(StringComparer.Ordinal);
    private Dictionary<string, QuestStateDto> _stateById = new(StringComparer.Ordinal);
    private Dictionary<string, TraderStateDto> _traderStateById = new(StringComparer.Ordinal);
    private Dictionary<string, List<IndexedQuestEdge>> _incoming = new(StringComparer.Ordinal);
    private Dictionary<string, List<IndexedQuestEdge>> _outgoing = new(StringComparer.Ordinal);
    private HashSet<string> _applicable = new(StringComparer.Ordinal);
    private HashSet<string> _repeatableIds = new(StringComparer.Ordinal);

    public QuestTopologyDto? Topology { get; private set; }
    public ProfileStateDto? Profile { get; private set; }
    public bool ShowAllFuture { get; private set; }
    public bool ShowFinished { get; private set; } = true;
    public bool ShowRepeatables { get; private set; } = true;
    public bool LevelEligibleOnly { get; private set; } = true;
    public bool InProgressExpanded { get; private set; }
    public string Search { get; private set; } = string.Empty;
    public string TraderFilter { get; private set; } = string.Empty;
    public string? SelectedId { get; private set; }
    public string? FocusedId { get; private set; }
    public HashSet<string> VisibleIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> PrerequisiteIds { get; } = new(StringComparer.Ordinal);
    public HashSet<string> SuccessorIds { get; } = new(StringComparer.Ordinal);
    public HashSet<int> HighlightedEdgeIndexes { get; } = [];
    public HashSet<string>? FocusIds { get; private set; }
    public bool FocusMode => FocusedId is not null;
    public bool CanFocusSelection => SelectedId is not null && !_repeatableIds.Contains(SelectedId);
    public QuestNodeDto? SelectedNode => SelectedId is null ? null : GetNode(SelectedId);
    public QuestStateDto? SelectedQuestState => SelectedId is null ? null : GetQuestState(SelectedId);

    public void ApplySettings(QuestMapSettings settings)
    {
        ShowAllFuture = settings.ShowAllFuture;
        ShowFinished = settings.ShowFinished;
        ShowRepeatables = settings.ShowRepeatables;
        LevelEligibleOnly = settings.LevelEligibleOnly;
        InProgressExpanded = settings.InProgressExpanded;
        Search = settings.Search ?? string.Empty;
        TraderFilter = settings.TraderFilter ?? string.Empty;
        _profileSettings.Clear();
        foreach (var pair in settings.Profiles ?? new Dictionary<string, QuestProfileUiSettings>()) _profileSettings[pair.Key] = pair.Value;
        RestoreCurrentProfileSettings();
        Recalculate();
    }

    public QuestMapSettings CaptureSettings(string? selectedProfileId, string language)
    {
        CaptureCurrentProfileSettings();
        return new QuestMapSettings(selectedProfileId, language, ShowAllFuture, ShowFinished, LevelEligibleOnly, InProgressExpanded, Search, TraderFilter, new Dictionary<string, QuestProfileUiSettings>(_profileSettings, StringComparer.Ordinal))
        {
            ShowRepeatables = ShowRepeatables,
        };
    }

    public void SetData(QuestTopologyDto topology, ProfileStateDto? profile)
    {
        CaptureCurrentProfileSettings();
        Topology = topology;
        Profile = profile;
        var repeatableEntries = profile?.RepeatableQuestGroups.SelectMany(group => group.Quests).ToArray() ?? [];
        _repeatableIds = repeatableEntries.Select(entry => entry.Node.Id).ToHashSet(StringComparer.Ordinal);
        _nodeById = topology.Quests
            .Concat(repeatableEntries.Select(entry => entry.Node))
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        _stateById = (profile?.Quests ?? [])
            .Concat(repeatableEntries.Select(entry => entry.State))
            .ToDictionary(state => state.QuestId, StringComparer.Ordinal);
        _traderStateById = profile?.Traders.ToDictionary(state => state.TraderId, StringComparer.Ordinal) ?? new Dictionary<string, TraderStateDto>(StringComparer.Ordinal);
        _applicable = profile?.AllApplicableQuestIds
            .Concat(_repeatableIds)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        _incoming = GroupEdges(topology.Edges, edge => edge.TargetId);
        _outgoing = GroupEdges(topology.Edges, edge => edge.SourceId);
        RestoreCurrentProfileSettings();
        Recalculate();
    }

    public void SetShowAllFuture(bool value) { ShowAllFuture = value; Recalculate(); }
    public void SetShowFinished(bool value) { if (!FocusMode) ShowFinished = value; Recalculate(); }
    public void SetShowRepeatables(bool value) { if (!FocusMode) ShowRepeatables = value; Recalculate(); }
    public void SetLevelEligibleOnly(bool value) { if (!FocusMode) LevelEligibleOnly = value; Recalculate(); }
    public void SetInProgressExpanded(bool value) => InProgressExpanded = value;
    public void SetSearch(string? value) { Search = value ?? string.Empty; Recalculate(); }
    public void SetTraderFilter(string? value) { if (!FocusMode) TraderFilter = value ?? string.Empty; Recalculate(); }

    public bool SelectQuest(string? id)
    {
        if (id is null)
        {
            ClearSelection();
            return true;
        }

        if (!_nodeById.ContainsKey(id) || !_applicable.Contains(id)) return false;
        SelectedId = id;
        Recalculate();
        return true;
    }

    public void ClearSelection()
    {
        SelectedId = null;
        FocusedId = null;
        FocusIds = null;
        Recalculate();
    }

    public void ActivateFocus()
    {
        var selectedId = SelectedId;
        if (selectedId is null || _repeatableIds.Contains(selectedId)) return;
        FocusedId = selectedId;
        FocusIds = ComputeChain(selectedId);
        Recalculate();
    }

    public void ClearFocus()
    {
        FocusedId = null;
        FocusIds = null;
        Recalculate();
    }

    public QuestNodeDto? GetNode(string id) => _nodeById.GetValueOrDefault(id);
    public QuestStateDto? GetQuestState(string id) => _stateById.GetValueOrDefault(id);
    public TraderStateDto? GetTraderState(string id) => _traderStateById.GetValueOrDefault(id);
    public IReadOnlyList<IndexedQuestEdge> Incoming(string id) => _incoming.GetValueOrDefault(id) ?? [];
    public IReadOnlyList<IndexedQuestEdge> Outgoing(string id) => _outgoing.GetValueOrDefault(id) ?? [];
    public bool IsCollectorRoute(string id) => Topology?.CollectorPathQuestIds.Contains(id, StringComparer.Ordinal) == true;
    public bool IsLightkeeperRoute(string id) => Topology?.LightkeeperPathQuestIds.Contains(id, StringComparer.Ordinal) == true;

    public IReadOnlyList<QuestTraderDto> FilterTraders()
    {
        if (Topology is null) return [];
        var questTraderIds = _nodeById.Values.Select(quest => quest.TraderId).ToHashSet(StringComparer.Ordinal);
        var ranks = QuestMapTraderOrder.Ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        return Topology.Traders
            .Where(trader => questTraderIds.Contains(trader.Id))
            .OrderBy(trader => ranks.GetValueOrDefault(trader.Id, int.MaxValue))
            .ThenBy(trader => trader.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<InProgressTraderGroup> InProgressGroups()
    {
        if (Topology is null || Profile is null) return [];
        var traderMeta = Topology.Traders.ToDictionary(trader => trader.Id, StringComparer.Ordinal);
        var ranks = QuestMapTraderOrder.Ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);
        return Profile.Quests
            .Where(state => state.DisplayState == "InProgress" && _applicable.Contains(state.QuestId) && _nodeById.ContainsKey(state.QuestId))
            .Select(state => new InProgressQuest(_nodeById[state.QuestId], state))
            .GroupBy(item => item.Node.TraderId, StringComparer.Ordinal)
            .Select(group =>
            {
                var trader = traderMeta.GetValueOrDefault(group.Key) ?? new QuestTraderDto(group.Key, group.First().Node.TraderName, group.First().Node.TraderImageUrl);
                return new InProgressTraderGroup(trader, group.OrderBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase).ToArray());
            })
            .OrderBy(group => ranks.GetValueOrDefault(group.Trader.Id, int.MaxValue))
            .ThenBy(group => group.Trader.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public QuestGraphView BuildGraphView(QuestMapLocalizer localizer) => new(
        Profile?.ProfileId,
        localizer.Language,
        localizer.BrowserLocale,
        VisibleIds.ToArray(),
        SelectedId,
        FocusedId,
        PrerequisiteIds.ToArray(),
        SuccessorIds.ToArray(),
        HighlightedEdgeIndexes.ToArray(),
        InProgressExpanded,
        ViewportScope(),
        localizer.Strings
    );

    public QuestGraphSnapshot BuildGraphSnapshot(QuestMapLocalizer localizer) => new(
        Topology ?? throw new InvalidOperationException("Quest topology has not been loaded."),
        Profile,
        BuildGraphView(localizer)
    );

    public QuestGraphUpdate BuildGraphUpdate() => new(
        VisibleIds.ToArray(), SelectedId, FocusedId, PrerequisiteIds.ToArray(), SuccessorIds.ToArray(),
        HighlightedEdgeIndexes.ToArray(), InProgressExpanded, ViewportScope()
    );

    private void Recalculate()
    {
        UpdateSelectionHighlight();
        if (FocusedId is not null) FocusIds = ComputeChain(FocusedId);
        VisibleIds.Clear();
        if (Profile is null || Topology is null) return;

        var baseIds = ShowAllFuture ? Profile.AllApplicableQuestIds : Profile.DefaultVisibleQuestIds;
        var search = Search.Trim();
        var focusMode = FocusIds is not null;
        var trader = focusMode ? string.Empty : TraderFilter;
        var showFinished = focusMode || ShowFinished;
        var levelOnly = !focusMode && LevelEligibleOnly;

        void Consider(string id)
        {
            if (!_nodeById.TryGetValue(id, out var node) || !_applicable.Contains(id) || FocusIds is not null && !FocusIds.Contains(id)) return;
            var state = _stateById.GetValueOrDefault(id);
            var selected = id == SelectedId;
            var revealFinished = focusMode || selected || PrerequisiteIds.Contains(id);
            if (!showFinished && !revealFinished && IsFinished(node, state)) return;
            if (!selected && levelOnly && state?.DisplayState == "LevelGated") return;
            if (!selected && trader.Length > 0 && node.TraderId != trader && !PrerequisiteIds.Contains(id)) return;
            if (!selected && search.Length > 0 && !$"{node.Name} {node.TraderName} {node.Id}".Contains(search, StringComparison.OrdinalIgnoreCase)) return;
            VisibleIds.Add(id);
        }

        foreach (var id in baseIds) Consider(id);
        if (ShowRepeatables)
        {
            foreach (var id in _repeatableIds) Consider(id);
        }
        foreach (var id in focusMode ? FocusIds! : PrerequisiteIds) Consider(id);

        if (!focusMode && trader.Length > 0)
        {
            var visibleTraderQuestIds = VisibleIds
                .Where(id => _nodeById.GetValueOrDefault(id)?.TraderId == trader
                    && _stateById.GetValueOrDefault(id)?.DisplayState is "Available" or "InProgress" or "ReadyToFinish" or "Completed")
                .ToArray();

            foreach (var sourceId in visibleTraderQuestIds)
            {
                foreach (var item in Outgoing(sourceId))
                {
                    var targetId = item.Edge.TargetId;
                    if (!_applicable.Contains(targetId)
                        || !_nodeById.ContainsKey(targetId))
                    {
                        continue;
                    }

                    var target = _nodeById[targetId];
                    var state = _stateById.GetValueOrDefault(targetId);
                    var selected = targetId == SelectedId;
                    var revealFinished = selected || PrerequisiteIds.Contains(targetId);
                    if (!showFinished && !revealFinished && IsFinished(target, state)) continue;
                    if (!selected && levelOnly && state?.DisplayState == "LevelGated") continue;

                    VisibleIds.Add(targetId);
                }
            }

            var visibleBlockedQuestIds = VisibleIds
                .Where(id => _stateById.GetValueOrDefault(id)?.Blockers.Any(blocker => blocker.Kind == "Prerequisite") == true)
                .ToArray();

            foreach (var blockedQuestId in visibleBlockedQuestIds)
            {
                var state = _stateById[blockedQuestId];
                foreach (var blocker in state.Blockers.Where(blocker => blocker.Kind == "Prerequisite" && blocker.SubjectId is not null))
                {
                    var prerequisiteId = blocker.SubjectId!;
                    if (_applicable.Contains(prerequisiteId)
                        && _nodeById.ContainsKey(prerequisiteId)
                        && Incoming(blockedQuestId).Any(item => item.Edge.SourceId == prerequisiteId))
                    {
                        VisibleIds.Add(prerequisiteId);
                    }
                }
            }
        }
    }

    private void UpdateSelectionHighlight()
    {
        PrerequisiteIds.Clear();
        SuccessorIds.Clear();
        HighlightedEdgeIndexes.Clear();
        if (SelectedId is null || !_nodeById.ContainsKey(SelectedId)) return;

        var queue = new Queue<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { SelectedId };
        queue.Enqueue(SelectedId);
        while (queue.TryDequeue(out var current))
        {
            foreach (var item in Incoming(current))
            {
                if (!_applicable.Contains(item.Edge.SourceId)) continue;
                HighlightedEdgeIndexes.Add(item.Index);
                PrerequisiteIds.Add(item.Edge.SourceId);
                if (seen.Add(item.Edge.SourceId)) queue.Enqueue(item.Edge.SourceId);
            }
        }

        foreach (var item in Outgoing(SelectedId))
        {
            if (!_applicable.Contains(item.Edge.TargetId)) continue;
            SuccessorIds.Add(item.Edge.TargetId);
            HighlightedEdgeIndexes.Add(item.Index);
        }
    }

    private HashSet<string> ComputeChain(string id)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { id };
        var queue = new Queue<string>();
        queue.Enqueue(id);
        while (queue.TryDequeue(out var current))
        {
            foreach (var item in Incoming(current))
            {
                if (_applicable.Contains(item.Edge.SourceId) && result.Add(item.Edge.SourceId)) queue.Enqueue(item.Edge.SourceId);
            }
        }

        foreach (var item in Outgoing(id)) if (_applicable.Contains(item.Edge.TargetId)) result.Add(item.Edge.TargetId);
        return result;
    }

    private static bool IsFinished(QuestNodeDto node, QuestStateDto? state)
    {
        if (state is null) return false;
        if (state.DisplayState is "Completed" or "Failed") return true;
        if (state.DisplayState == "Excluded") return state.Exclusion?.Permanent is not false;
        return state.DisplayState == "Expired" && !node.Restartable;
    }

    private string ViewportScope() => Topology is null || Profile is null
        ? string.Empty
        : string.Join('|', Topology.Version, Profile.ProfileId, ShowAllFuture ? 1 : 0, ShowFinished ? 1 : 0, ShowRepeatables ? 1 : 0, LevelEligibleOnly ? 1 : 0, TraderFilter, Search.Trim(), FocusedId ?? string.Empty, SelectedId ?? string.Empty);

    private void CaptureCurrentProfileSettings()
    {
        if (Profile is not null) _profileSettings[Profile.ProfileId] = new QuestProfileUiSettings(SelectedId, FocusedId);
    }

    private void RestoreCurrentProfileSettings()
    {
        var saved = Profile is not null && _profileSettings.TryGetValue(Profile.ProfileId, out var value) ? value : null;
        SelectedId = saved?.SelectedId is { } selected && _applicable.Contains(selected) ? selected : null;
        FocusedId = saved?.FocusedId is { } focused && _applicable.Contains(focused) && !_repeatableIds.Contains(focused) ? focused : null;
        FocusIds = FocusedId is null ? null : ComputeChain(FocusedId);
    }

    private static Dictionary<string, List<IndexedQuestEdge>> GroupEdges(IReadOnlyList<QuestEdgeDto> edges, Func<QuestEdgeDto, string> keySelector)
    {
        var result = new Dictionary<string, List<IndexedQuestEdge>>(StringComparer.Ordinal);
        for (var index = 0; index < edges.Count; index++)
        {
            var edge = edges[index];
            var key = keySelector(edge);
            if (!result.TryGetValue(key, out var values)) result[key] = values = [];
            values.Add(new IndexedQuestEdge(index, edge));
        }

        return result;
    }
}

public sealed record IndexedQuestEdge(int Index, QuestEdgeDto Edge);
public sealed record InProgressQuest(QuestNodeDto Node, QuestStateDto State);
public sealed record InProgressTraderGroup(QuestTraderDto Trader, IReadOnlyList<InProgressQuest> Quests);
public sealed record QuestProfileUiSettings(string? SelectedId, string? FocusedId);
public sealed record QuestMapSettings(string? SelectedProfileId, string? Language, bool ShowAllFuture, bool ShowFinished, bool LevelEligibleOnly, bool InProgressExpanded, string? Search, string? TraderFilter, IReadOnlyDictionary<string, QuestProfileUiSettings>? Profiles)
{
    public bool ShowRepeatables { get; init; } = true;
    public static QuestMapSettings Default { get; } = new(null, null, false, true, true, false, string.Empty, string.Empty, new Dictionary<string, QuestProfileUiSettings>());
}
public sealed record QuestGraphView(string? ProfileId, string Language, string BrowserLocale, string[] VisibleQuestIds, string? SelectedId, string? FocusedId, string[] PrerequisiteIds, string[] SuccessorIds, int[] HighlightedEdgeIndexes, bool InProgressExpanded, string ViewportScope, IReadOnlyDictionary<string, string> Strings);
public sealed record QuestGraphSnapshot(QuestTopologyDto Topology, ProfileStateDto? Profile, QuestGraphView View);
public sealed record QuestGraphUpdate(string[] VisibleQuestIds, string? SelectedId, string? FocusedId, string[] PrerequisiteIds, string[] SuccessorIds, int[] HighlightedEdgeIndexes, bool InProgressExpanded, string ViewportScope);
