using SPTQuestMap.Services;
using System.Text.Json.Serialization;

namespace SPTQuestMap.Presentation;

public sealed class QuestMapPageState
{
    private readonly Dictionary<string, QuestProfileUiSettings> _profileSettings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuestProfileUiSettings> _comparePairSettings = new(StringComparer.Ordinal);
    private Dictionary<string, QuestNodeDto> _nodeById = new(StringComparer.Ordinal);
    private Dictionary<string, QuestStateDto> _stateById = new(StringComparer.Ordinal);
    private Dictionary<string, QuestStateDto> _comparisonStateById = new(StringComparer.Ordinal);
    private Dictionary<string, TraderStateDto> _traderStateById = new(StringComparer.Ordinal);
    private Dictionary<string, TraderStateDto> _comparisonTraderStateById = new(StringComparer.Ordinal);
    private Dictionary<string, List<IndexedQuestEdge>> _incoming = new(StringComparer.Ordinal);
    private Dictionary<string, List<IndexedQuestEdge>> _outgoing = new(StringComparer.Ordinal);
    private HashSet<string> _applicable = new(StringComparer.Ordinal);
    private HashSet<string> _primaryApplicable = new(StringComparer.Ordinal);
    private HashSet<string> _comparisonApplicable = new(StringComparer.Ordinal);
    private HashSet<string> _repeatableIds = new(StringComparer.Ordinal);
    private Dictionary<string, QuestComparisonDto> _comparisonById = new(StringComparer.Ordinal);
    private readonly HashSet<string> _comparisonScopeIds = new(StringComparer.Ordinal);

    public QuestTopologyDto? Topology { get; private set; }
    public ProfileStateDto? Profile { get; private set; }
    public ProfileStateDto? ComparisonProfile { get; private set; }
    public bool CompareMode => ComparisonProfile is not null;
    public bool ShowAllFuture { get; private set; }
    public bool ShowFinished { get; private set; } = true;
    public bool ShowRepeatables { get; private set; } = true;
    public bool LevelEligibleOnly { get; private set; } = true;
    public bool InProgressExpanded { get; private set; }
    public QuestComparisonFilter ComparisonFilter { get; private set; }
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
    public IReadOnlyDictionary<QuestDifferenceCategory, int> ComparisonCounts { get; private set; } = new Dictionary<QuestDifferenceCategory, int>();
    public int ComparableQuestCount { get; private set; }
    public int DifferenceCount { get; private set; }

    public void ApplySettings(QuestMapSettings settings)
    {
        ShowAllFuture = settings.ShowAllFuture;
        ShowFinished = settings.ShowFinished;
        ShowRepeatables = settings.ShowRepeatables;
        LevelEligibleOnly = settings.LevelEligibleOnly;
        InProgressExpanded = settings.InProgressExpanded;
        Search = settings.Search ?? string.Empty;
        TraderFilter = settings.TraderFilter ?? string.Empty;
        ComparisonFilter = settings.ComparisonFilter;
        _profileSettings.Clear();
        foreach (var pair in settings.Profiles ?? new Dictionary<string, QuestProfileUiSettings>()) _profileSettings[pair.Key] = pair.Value;
        _comparePairSettings.Clear();
        foreach (var pair in settings.ComparePairs ?? new Dictionary<string, QuestProfileUiSettings>()) _comparePairSettings[pair.Key] = pair.Value;
        RestoreCurrentProfileSettings();
        Recalculate();
    }

    public QuestMapSettings CaptureSettings(string? selectedProfileId, string language, string? comparisonProfileId = null, bool? compareEnabled = null)
    {
        CaptureCurrentProfileSettings();
        return new QuestMapSettings(selectedProfileId, language, ShowAllFuture, ShowFinished, LevelEligibleOnly, InProgressExpanded, Search, TraderFilter, new Dictionary<string, QuestProfileUiSettings>(_profileSettings, StringComparer.Ordinal))
        {
            ShowRepeatables = ShowRepeatables,
            CompareEnabled = compareEnabled ?? CompareMode,
            ComparisonProfileId = comparisonProfileId ?? ComparisonProfile?.ProfileId,
            ComparisonFilter = ComparisonFilter,
            ComparePairs = new Dictionary<string, QuestProfileUiSettings>(_comparePairSettings, StringComparer.Ordinal),
        };
    }

    public void SetData(QuestTopologyDto topology, ProfileStateDto? profile, ProfileStateDto? comparisonProfile = null)
    {
        CaptureCurrentProfileSettings();
        Topology = topology;
        Profile = profile;
        ComparisonProfile = comparisonProfile;
        var repeatableEntries = comparisonProfile is null
            ? profile?.RepeatableQuestGroups.SelectMany(group => group.Quests).ToArray() ?? []
            : [];
        _repeatableIds = repeatableEntries.Select(entry => entry.Node.Id).ToHashSet(StringComparer.Ordinal);
        _nodeById = topology.Quests
            .Concat(repeatableEntries.Select(entry => entry.Node))
            .ToDictionary(node => node.Id, StringComparer.Ordinal);
        _stateById = (profile?.Quests ?? [])
            .Concat(repeatableEntries.Select(entry => entry.State))
            .ToDictionary(state => state.QuestId, StringComparer.Ordinal);
        _comparisonStateById = comparisonProfile?.Quests.ToDictionary(state => state.QuestId, StringComparer.Ordinal)
            ?? new Dictionary<string, QuestStateDto>(StringComparer.Ordinal);
        _traderStateById = profile?.Traders.ToDictionary(state => state.TraderId, StringComparer.Ordinal) ?? new Dictionary<string, TraderStateDto>(StringComparer.Ordinal);
        _comparisonTraderStateById = comparisonProfile?.Traders.ToDictionary(state => state.TraderId, StringComparer.Ordinal) ?? new Dictionary<string, TraderStateDto>(StringComparer.Ordinal);
        _primaryApplicable = profile?.AllApplicableQuestIds.Concat(_repeatableIds).ToHashSet(StringComparer.Ordinal) ?? [];
        _comparisonApplicable = comparisonProfile?.AllApplicableQuestIds.ToHashSet(StringComparer.Ordinal) ?? [];
        _applicable = _primaryApplicable.Concat(_comparisonApplicable).ToHashSet(StringComparer.Ordinal);
        BuildComparisons();
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
    public void SetComparisonFilter(QuestComparisonFilter value) { if (!FocusMode && CompareMode) ComparisonFilter = value; Recalculate(); }
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
    public QuestStateDto? GetComparisonQuestState(string id) => _comparisonStateById.GetValueOrDefault(id);
    public TraderStateDto? GetTraderState(string id) => _traderStateById.GetValueOrDefault(id);
    public TraderStateDto? GetComparisonTraderState(string id) => _comparisonTraderStateById.GetValueOrDefault(id);
    public bool IsPrimaryApplicable(string id) => _primaryApplicable.Contains(id);
    public bool IsComparisonApplicable(string id) => _comparisonApplicable.Contains(id);
    public QuestComparisonDto GetComparison(string id) => _comparisonById.GetValueOrDefault(id) ?? QuestComparisonDto.Match(id);
    public bool ObjectiveDiffers(string objectiveId)
    {
        if (!CompareMode || SelectedId is null) return false;
        var primary = GetQuestState(SelectedId)?.Objectives.FirstOrDefault(item => item.ObjectiveId == objectiveId);
        var comparison = GetComparisonQuestState(SelectedId)?.Objectives.FirstOrDefault(item => item.ObjectiveId == objectiveId);
        return !ObjectiveMatches(primary, comparison);
    }
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
        var ids = Profile.Quests.Where(state => state.DisplayState == "InProgress").Select(state => state.QuestId)
            .Concat(ComparisonProfile?.Quests.Where(state => state.DisplayState == "InProgress").Select(state => state.QuestId) ?? [])
            .Distinct(StringComparer.Ordinal);
        return ids
            .Where(id => _applicable.Contains(id) && _nodeById.ContainsKey(id))
            .Select(id => new InProgressQuest(_nodeById[id], GetQuestState(id), GetComparisonQuestState(id)))
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
        ComparisonProfile?.ProfileId,
        CompareMode,
        localizer.Language,
        localizer.BrowserLocale,
        VisibleIds.ToArray(),
        SelectedId,
        FocusedId,
        PrerequisiteIds.ToArray(),
        SuccessorIds.ToArray(),
        HighlightedEdgeIndexes.ToArray(),
        CompareMode ? VisibleIds.Select(GetComparison).ToArray() : [],
        InProgressExpanded,
        ViewportScope(),
        localizer.Strings
    );

    public QuestGraphSnapshot BuildGraphSnapshot(QuestMapLocalizer localizer) => new(
        Topology ?? throw new InvalidOperationException("Quest topology has not been loaded."),
        Profile,
        ComparisonProfile,
        BuildGraphView(localizer)
    );

    public QuestGraphUpdate BuildGraphUpdate() => new(
        VisibleIds.ToArray(), SelectedId, FocusedId, PrerequisiteIds.ToArray(), SuccessorIds.ToArray(),
        HighlightedEdgeIndexes.ToArray(), CompareMode ? VisibleIds.Select(GetComparison).ToArray() : [],
        CompareMode, InProgressExpanded, ViewportScope()
    );

    private void Recalculate()
    {
        UpdateSelectionHighlight();
        if (FocusedId is not null) FocusIds = ComputeChain(FocusedId);
        VisibleIds.Clear();
        _comparisonScopeIds.Clear();
        if (Profile is null || Topology is null) { UpdateComparisonCounts([]); return; }

        var baseIds = ShowAllFuture
            ? Profile.AllApplicableQuestIds.Concat(ComparisonProfile?.AllApplicableQuestIds ?? [])
            : Profile.DefaultVisibleQuestIds.Concat(ComparisonProfile?.DefaultVisibleQuestIds ?? []);
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
            var selectionContext = selected || PrerequisiteIds.Contains(id) || SuccessorIds.Contains(id);
            if (!showFinished && !revealFinished && IsFinishedForEveryApplicableProfile(node, id)) return;
            if (!selected && levelOnly && IsLevelGatedForEveryApplicableProfile(id)) return;
            if (!selected && trader.Length > 0 && node.TraderId != trader && !PrerequisiteIds.Contains(id)) return;
            if (!selected && search.Length > 0 && !$"{node.Name} {node.TraderName} {node.Id}".Contains(search, StringComparison.OrdinalIgnoreCase)) return;
            if (CompareMode) _comparisonScopeIds.Add(id);
            if (CompareMode && !focusMode && !selectionContext && !MatchesComparisonFilter(id)) return;
            VisibleIds.Add(id);
        }

        foreach (var id in baseIds) Consider(id);
        if (ShowRepeatables && !CompareMode)
        {
            foreach (var id in _repeatableIds) Consider(id);
        }
        foreach (var id in focusMode ? FocusIds! : PrerequisiteIds) Consider(id);

        if (!focusMode && trader.Length > 0)
        {
            var visibleTraderQuestIds = VisibleIds
                .Where(id => _nodeById.GetValueOrDefault(id)?.TraderId == trader
                    && IsBoundaryForAnyApplicableProfile(id))
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
                    if (!showFinished && !revealFinished && IsFinishedForEveryApplicableProfile(target, targetId)) continue;
                    if (!selected && levelOnly && IsLevelGatedForEveryApplicableProfile(targetId)) continue;
                    if (CompareMode) _comparisonScopeIds.Add(targetId);
                    if (CompareMode && !selected && !PrerequisiteIds.Contains(targetId) && !SuccessorIds.Contains(targetId) && !MatchesComparisonFilter(targetId)) continue;

                    VisibleIds.Add(targetId);
                }
            }

            var visibleBlockedQuestIds = VisibleIds
                .Where(id => GetApplicableStates(id).Any(state => state?.Blockers.Any(blocker => blocker.Kind == "Prerequisite") == true))
                .ToArray();

            foreach (var blockedQuestId in visibleBlockedQuestIds)
            {
                var blockers = GetApplicableStates(blockedQuestId)
                    .Where(state => state is not null)
                    .SelectMany(state => state!.Blockers)
                    .Where(blocker => blocker.Kind == "Prerequisite" && blocker.SubjectId is not null)
                    .DistinctBy(blocker => blocker.SubjectId, StringComparer.Ordinal);
                foreach (var blocker in blockers)
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
        UpdateComparisonCounts(_comparisonScopeIds);
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

    private bool IsFinishedForEveryApplicableProfile(QuestNodeDto node, string id)
    {
        var states = new List<QuestStateDto?>();
        if (IsPrimaryApplicable(id)) states.Add(GetQuestState(id));
        if (IsComparisonApplicable(id)) states.Add(GetComparisonQuestState(id));
        return states.Count > 0 && states.All(state => IsFinished(node, state));
    }

    private bool IsLevelGatedForEveryApplicableProfile(string id)
    {
        var states = new List<QuestStateDto?>();
        if (IsPrimaryApplicable(id)) states.Add(GetQuestState(id));
        if (IsComparisonApplicable(id)) states.Add(GetComparisonQuestState(id));
        return states.Count > 0 && states.All(state => state?.DisplayState == "LevelGated");
    }

    private IEnumerable<QuestStateDto?> GetApplicableStates(string id)
    {
        if (IsPrimaryApplicable(id)) yield return GetQuestState(id);
        if (IsComparisonApplicable(id)) yield return GetComparisonQuestState(id);
    }

    private bool IsBoundaryForAnyApplicableProfile(string id) => GetApplicableStates(id)
        .Any(state => state?.DisplayState is "Available" or "InProgress" or "ReadyToFinish" or "Completed");

    private void BuildComparisons()
    {
        _comparisonById = new Dictionary<string, QuestComparisonDto>(StringComparer.Ordinal);
        if (!CompareMode)
        {
            UpdateComparisonCounts([]);
            return;
        }

        foreach (var id in _applicable)
        {
            var categories = new List<QuestDifferenceCategory>();
            var primaryApplicable = IsPrimaryApplicable(id);
            var comparisonApplicable = IsComparisonApplicable(id);
            if (primaryApplicable && !comparisonApplicable) categories.Add(QuestDifferenceCategory.PrimaryOnly);
            else if (!primaryApplicable && comparisonApplicable) categories.Add(QuestDifferenceCategory.ComparisonOnly);
            else if (primaryApplicable)
            {
                var primary = GetQuestState(id);
                var comparison = GetComparisonQuestState(id);
                if (primary?.Exclusion != comparison?.Exclusion) categories.Add(QuestDifferenceCategory.Exclusion);
                if ((primary?.DisplayState ?? "Locked") != (comparison?.DisplayState ?? "Locked")) categories.Add(QuestDifferenceCategory.Status);
                if (!ObjectivesMatch(primary, comparison)) categories.Add(QuestDifferenceCategory.Objectives);
                if (primary?.AvailableAfter != comparison?.AvailableAfter) categories.Add(QuestDifferenceCategory.AvailableAfter);
            }

            _comparisonById[id] = new QuestComparisonDto(id, categories.ToArray());
        }

        UpdateComparisonCounts(_comparisonById.Keys);
    }

    private void UpdateComparisonCounts(IEnumerable<string> ids)
    {
        var comparisons = ids.Select(GetComparison).ToArray();
        ComparableQuestCount = comparisons.Length;
        DifferenceCount = comparisons.Count(comparison => comparison.HasDifferences);
        ComparisonCounts = Enum.GetValues<QuestDifferenceCategory>()
            .ToDictionary(category => category, category => comparisons.Count(comparison => comparison.Categories.Contains(category)));
    }

    private bool MatchesComparisonFilter(string id)
    {
        if (ComparisonFilter == QuestComparisonFilter.AllQuests) return true;
        var comparison = GetComparison(id);
        if (ComparisonFilter == QuestComparisonFilter.AllChanges) return comparison.HasDifferences;
        var category = ComparisonFilter switch
        {
            QuestComparisonFilter.PrimaryOnly => QuestDifferenceCategory.PrimaryOnly,
            QuestComparisonFilter.ComparisonOnly => QuestDifferenceCategory.ComparisonOnly,
            QuestComparisonFilter.Exclusion => QuestDifferenceCategory.Exclusion,
            QuestComparisonFilter.Status => QuestDifferenceCategory.Status,
            QuestComparisonFilter.Objectives => QuestDifferenceCategory.Objectives,
            QuestComparisonFilter.AvailableAfter => QuestDifferenceCategory.AvailableAfter,
            _ => throw new InvalidOperationException($"Unsupported comparison filter {ComparisonFilter}."),
        };
        return comparison.Categories.Contains(category);
    }

    private static bool ObjectivesMatch(QuestStateDto? primary, QuestStateDto? comparison)
    {
        var primaryObjectives = (primary?.Objectives ?? []).ToDictionary(item => item.ObjectiveId, StringComparer.Ordinal);
        var comparisonObjectives = (comparison?.Objectives ?? []).ToDictionary(item => item.ObjectiveId, StringComparer.Ordinal);
        if (!primaryObjectives.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(comparisonObjectives.Keys)) return false;
        return primaryObjectives.All(pair => ObjectiveMatches(pair.Value, comparisonObjectives[pair.Key]));
    }

    private static bool ObjectiveMatches(ObjectiveProgressDto? primary, ObjectiveProgressDto? comparison) =>
        primary is null && comparison is null
        || primary is not null && comparison is not null
        && primary.Complete == comparison.Complete
        && primary.Current == comparison.Current
        && primary.Required == comparison.Required
        && primary.ProgressKnown == comparison.ProgressKnown;

    private string ViewportScope() => Topology is null || Profile is null
        ? string.Empty
        : string.Join('|', Topology.Version, Profile.ProfileId, ComparisonProfile?.ProfileId ?? string.Empty, ShowAllFuture ? 1 : 0, ShowFinished ? 1 : 0, ShowRepeatables && !CompareMode ? 1 : 0, LevelEligibleOnly ? 1 : 0, CompareMode ? ComparisonFilter : QuestComparisonFilter.AllQuests, TraderFilter, Search.Trim(), FocusedId ?? string.Empty, SelectedId ?? string.Empty);

    private void CaptureCurrentProfileSettings()
    {
        if (Profile is null) return;
        var settings = new QuestProfileUiSettings(SelectedId, FocusedId);
        if (ComparisonProfile is null) _profileSettings[Profile.ProfileId] = settings;
        else _comparePairSettings[ComparePairKey(Profile.ProfileId, ComparisonProfile.ProfileId)] = settings;
    }

    private void RestoreCurrentProfileSettings()
    {
        QuestProfileUiSettings? saved = null;
        if (Profile is not null)
        {
            var source = ComparisonProfile is null ? _profileSettings : _comparePairSettings;
            var key = ComparisonProfile is null ? Profile.ProfileId : ComparePairKey(Profile.ProfileId, ComparisonProfile.ProfileId);
            source.TryGetValue(key, out saved);
        }
        SelectedId = saved?.SelectedId is { } selected && _applicable.Contains(selected) ? selected : null;
        FocusedId = saved?.FocusedId is { } focused && _applicable.Contains(focused) && !_repeatableIds.Contains(focused) ? focused : null;
        FocusIds = FocusedId is null ? null : ComputeChain(FocusedId);
    }

    private static string ComparePairKey(string primaryId, string comparisonId) => $"{primaryId}|{comparisonId}";

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
public sealed record InProgressQuest(QuestNodeDto Node, QuestStateDto? PrimaryState, QuestStateDto? ComparisonState)
{
    public QuestStateDto State => PrimaryState ?? ComparisonState ?? throw new InvalidOperationException("An in-progress quest requires at least one profile state.");
}
public sealed record InProgressTraderGroup(QuestTraderDto Trader, IReadOnlyList<InProgressQuest> Quests);
public sealed record QuestProfileUiSettings(string? SelectedId, string? FocusedId);
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestDifferenceCategory { PrimaryOnly, ComparisonOnly, Exclusion, Status, Objectives, AvailableAfter }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuestComparisonFilter { AllQuests, AllChanges, PrimaryOnly, ComparisonOnly, Exclusion, Status, Objectives, AvailableAfter }
public sealed record QuestComparisonDto(string QuestId, QuestDifferenceCategory[] Categories)
{
    public bool HasDifferences => Categories.Length > 0;
    public static QuestComparisonDto Match(string questId) => new(questId, []);
}
public sealed record QuestMapSettings(string? SelectedProfileId, string? Language, bool ShowAllFuture, bool ShowFinished, bool LevelEligibleOnly, bool InProgressExpanded, string? Search, string? TraderFilter, IReadOnlyDictionary<string, QuestProfileUiSettings>? Profiles)
{
    public bool ShowRepeatables { get; init; } = true;
    public bool CompareEnabled { get; init; }
    public string? ComparisonProfileId { get; init; }
    public QuestComparisonFilter ComparisonFilter { get; init; }
    public IReadOnlyDictionary<string, QuestProfileUiSettings>? ComparePairs { get; init; } = new Dictionary<string, QuestProfileUiSettings>();
    public static QuestMapSettings Default { get; } = new(null, null, false, true, true, false, string.Empty, string.Empty, new Dictionary<string, QuestProfileUiSettings>());
}
public sealed record QuestGraphView(string? ProfileId, string? ComparisonProfileId, bool CompareMode, string Language, string BrowserLocale, string[] VisibleQuestIds, string? SelectedId, string? FocusedId, string[] PrerequisiteIds, string[] SuccessorIds, int[] HighlightedEdgeIndexes, QuestComparisonDto[] Comparisons, bool InProgressExpanded, string ViewportScope, IReadOnlyDictionary<string, string> Strings);
public sealed record QuestGraphSnapshot(QuestTopologyDto Topology, ProfileStateDto? Profile, ProfileStateDto? ComparisonProfile, QuestGraphView View);
public sealed record QuestGraphUpdate(string[] VisibleQuestIds, string? SelectedId, string? FocusedId, string[] PrerequisiteIds, string[] SuccessorIds, int[] HighlightedEdgeIndexes, QuestComparisonDto[] Comparisons, bool CompareMode, bool InProgressExpanded, string ViewportScope);
