using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

/// <summary>
/// Trader-scoped host for the same table, graph, details, tracking, asset, and
/// native transaction components used by the global Tasks screen.
/// </summary>
internal sealed class TraderTasksScreenController : IDisposable
{
    private const float HeaderHeight = 48f;
    private static readonly Dictionary<string, TraderTasksFilterState> SharedFiltersByProfile = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> SharedSelectionByProfileAndTrader = new(StringComparer.Ordinal);

    private static readonly FieldInfo QuestsListField = AccessTools.Field(typeof(QuestsScreen), "_questsListView")
        ?? throw new MissingFieldException(typeof(QuestsScreen).FullName, "_questsListView");
    private static readonly FieldInfo QuestViewField = AccessTools.Field(typeof(QuestsScreen), "_questView")
        ?? throw new MissingFieldException(typeof(QuestsScreen).FullName, "_questView");

    private readonly QuestsScreen _screen;
    private readonly ISession _session;
    private readonly InventoryController _inventoryController;
    private readonly AbstractQuestControllerClass _questController;
    private readonly TraderClass _trader;
    private readonly ManualLogSource _log;
    private readonly bool _debugLogging;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly QuestTrackingService _tracking;
    private readonly bool _customDetailsEnabled;
    private readonly Func<bool> _showHiddenRewards;
    private readonly Func<bool> _defaultDetailsToSummary;
    private readonly Action<string, QuestDetailsActionKind> _requestQuestRefresh;
    private readonly List<QuestTableSortCriterion> _sortCriteria = [];
    private readonly HashSet<string> _expandedQuestIds = new(StringComparer.Ordinal);

    private QuestsListView? _vanillaList;
    private QuestView? _nativeQuestView;
    private TraderNativeWorkspaceSuppressor? _nativeWorkspaceSuppressor;
    private GClass3794? _favoriteQuestService;
    private RectTransform? _surfaceParent;
    private Rect _surfaceBounds;
    private bool _vanillaListWasActive;
    private bool _nativeQuestViewWasActive;
    private QuestGraphTopology? _topology;
    private QuestGraphLayout? _layout;
    private QuestProfileOverlay? _overlay;
    private GlobalQuestGraphProjection? _projection;
    private IGlobalTasksContentView? _contentView;
    private QuestDetailsPane? _detailPane;
    private GlobalQuestGraphMode _mode = GlobalQuestGraphMode.InProgress;
    private bool _showAllFuture;
    private bool _hideFinished;
    private bool _levelEligibleOnly;
    private bool _hideCompletedTasks;
    private bool _hideUnavailable = true;
    private string _search = string.Empty;
    private string? _selectedQuestId;
    private string? _focusQuestId;
    private string? _viewStateScope;
    private string? _filterProfileId;
    private Image? _detailsButtonImage;
    private Button? _detailsButton;
    private bool _detailsVisible;
    private bool _mounted;
    private bool _disposed;

    public TraderTasksScreenController(
        QuestsScreen screen,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        TraderClass trader,
        ManualLogSource log,
        bool debugLogging,
        QuestAssetSpriteCache assetCache,
        QuestTrackingService tracking,
        bool customDetailsEnabled,
        Func<bool> showHiddenRewards,
        Func<bool> defaultDetailsToSummary,
        Action<string, QuestDetailsActionKind> requestQuestRefresh)
    {
        _screen = screen;
        _session = session;
        _inventoryController = inventoryController;
        _questController = questController;
        _trader = trader;
        _log = log;
        _debugLogging = debugLogging;
        _assetCache = assetCache;
        _tracking = tracking;
        _customDetailsEnabled = customDetailsEnabled;
        _showHiddenRewards = showHiddenRewards;
        _defaultDetailsToSummary = defaultDetailsToSummary;
        _requestQuestRefresh = requestQuestRefresh;
    }

    public void Mount(QuestGraphTopology topology, QuestGraphLayout layout, QuestProfileOverlay overlay)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TraderTasksScreenController));
        if (_mounted) throw new InvalidOperationException("The trader Tasks workspace is already mounted.");
        _vanillaList = (QuestsListView?)QuestsListField.GetValue(_screen)
            ?? throw new InvalidOperationException("QuestsScreen._questsListView was null.");
        _nativeQuestView = (QuestView?)QuestViewField.GetValue(_screen)
            ?? throw new InvalidOperationException("QuestsScreen._questView was null.");
        _favoriteQuestService = ResolveFavoriteQuestService(_vanillaList, _nativeQuestView, _screen, _questController)
            ?? ResolveFavoriteQuestService(_screen.GetComponentsInChildren<MonoBehaviour>(true).Cast<object>().ToArray());
        _vanillaListWasActive = _vanillaList.gameObject.activeSelf;
        _nativeQuestViewWasActive = _nativeQuestView.gameObject.activeSelf;
        _nativeWorkspaceSuppressor = _screen.gameObject.AddComponent<TraderNativeWorkspaceSuppressor>();
        _nativeWorkspaceSuppressor.Bind(_vanillaList, _nativeQuestView);
        ResolveSurfaceBounds();
        _topology = topology;
        _layout = layout;
        _overlay = overlay;
        _filterProfileId = overlay.ProfileId;
        LoadSharedFilters();
        LoadSharedSelection();

        BuildView(null, true);
        _vanillaList.gameObject.SetActive(false);
        _nativeQuestView.gameObject.SetActive(false);
        _mounted = true;
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_TRADER_WORKSPACE_MOUNT " +
            $"screen={_screen.GetInstanceID()}; trader={_trader.Id}; mode=Tasks; nodes={_projection?.Nodes.Count ?? 0}; " +
            "sharedTable=True; sharedGraph=True; sharedDetails=True; nativeWorkspaceHidden=True");
    }

    public void Dispose()
    {
        if (_disposed) return;
        PersistViewport();
        QuestGraphViewStateStore.Flush();
        _disposed = true;
        _detailPane?.Dispose();
        _detailPane = null;
        _contentView?.Dispose();
        _contentView = null;
        foreach (var root in _screen.GetComponentsInChildren<RectTransform>(true)
                     .Where(rect => rect.name is "QuestMapGraph" or "QuestMapTraderSurfaceMount")
                     .ToArray())
        {
            UnityEngine.Object.Destroy(root.gameObject);
        }
        if (_nativeWorkspaceSuppressor is not null)
        {
            _nativeWorkspaceSuppressor.enabled = false;
            UnityEngine.Object.Destroy(_nativeWorkspaceSuppressor);
        }
        if (_vanillaList is not null) _vanillaList.gameObject.SetActive(_vanillaListWasActive);
        if (_nativeQuestView is not null) _nativeQuestView.gameObject.SetActive(_nativeQuestViewWasActive);
        QuestMapDebugLog.Info(_log, $"QUESTMAP_TRADER_WORKSPACE_DISPOSE screen={_screen.GetInstanceID()}; trader={_trader.Id}; vanillaRestored=True");
    }

    public string RefreshOverlay(QuestProfileOverlay overlay)
        => RefreshOverlay(overlay, true, "trader-overlay-refreshed");

    public string RefreshQuest(QuestProfileOverlay overlay, string questId)
        => RefreshOverlay(overlay, false, $"trader-quest-refreshed:{questId}");

    public string RebuildTopology(QuestGraphTopology topology, QuestGraphLayout layout, QuestProfileOverlay overlay)
    {
        if (_disposed || !_mounted) return "screen-inactive";
        _topology = topology;
        _layout = layout;
        _overlay = overlay;
        RebuildView(true);
        return "trader-workspace-topology-rebuilt";
    }

    public string ApplyRepeatableTopologyDelta(QuestGraphTopology topology, QuestGraphLayout layout, QuestProfileOverlay overlay)
    {
        if (_disposed || !_mounted || _contentView is null) return "screen-inactive";
        _topology = topology;
        _layout = layout;
        _overlay = overlay;
        var next = BuildProjection();
        if (_selectedQuestId is not null && !next.NodesById.ContainsKey(_selectedQuestId)) ClearSelection();

        if (_contentView is InProgressQuestTableView table)
        {
            table.ApplyTopologyDelta(topology, overlay, next, next.Nodes, _selectedQuestId);
        }
        else if (_contentView is QuestGraphView graph
                 && !graph.ApplyTopologyDelta(next, topology, overlay, _selectedQuestId))
        {
            RebuildView(true);
            return "trader-workspace-topology-delta-fell-back";
        }
        _projection = next;
        RefreshSelectedDetails();
        return "trader-workspace-topology-delta-applied";
    }

    private string RefreshOverlay(QuestProfileOverlay overlay, bool refreshAllNativeControls, string result)
    {
        if (_disposed || !_mounted || _contentView is null) return "screen-inactive";
        _overlay = overlay;
        var next = BuildProjection();
        if (_selectedQuestId is not null && !next.NodesById.ContainsKey(_selectedQuestId)) ClearSelection();

        if (_contentView is InProgressQuestTableView table)
        {
            table.RefreshOverlay(overlay, _selectedQuestId, next, refreshAllNativeControls);
        }
        else if (_contentView is QuestGraphView graph)
        {
            if (!graph.ApplyTopologyDelta(next, _topology!, overlay, _selectedQuestId)) RebuildView(true);
        }
        _projection = next;
        RefreshSelectedDetails();
        return result;
    }

    private void BuildView(GraphViewportState? liveViewport, bool loadPersisted)
    {
        if (_surfaceParent is null || _topology is null || _layout is null || _overlay is null)
            throw new InvalidOperationException("Trader workspace dependencies are not ready.");

        var mount = CreateSurfaceMount();
        try
        {
            _projection = BuildProjection();
            var scope = $"trader-workspace-v1:{_trader.Id}:{_mode}";
            _viewStateScope = QuestGraphViewStateStore.Scope(_topology.Version, _overlay.ProfileId, scope);
            PersistedQuestGraphViewState persisted = default;
            var hasPersisted = loadPersisted && QuestGraphViewStateStore.TryLoad(_viewStateScope, out persisted);
            var viewport = liveViewport ?? (hasPersisted ? persisted.Viewport : null);

            _contentView = _mode == GlobalQuestGraphMode.InProgress
                ? InProgressQuestTableView.Create(
                    mount, _projection, _topology, _overlay, _projection.Nodes, _sortCriteria,
                    _hideCompletedTasks, _expandedQuestIds, SelectQuest, ClearSelection, ToggleSort,
                    ToggleExpansion, _log, _assetCache, _favoriteQuestService, _tracking,
                    MutationsAllowed, CreateHandoverAction, CanAcceptQuest, AcceptQuestAsync,
                    CanCompleteQuest, CompleteQuestAsync, CanReplaceQuest, ReplaceQuestAsync,
                    HandleQuestMutation, viewport, HeaderHeight, QuestTableSectionMode.TraderStatus, _trader.Id)
                : QuestGraphView.Create(
                    mount, "QUEST MAP", _projection, _topology, _overlay, SelectQuest,
                    PersistViewport, _log, _debugLogging, _assetCache, viewport, BuildCanvasActions(),
                    true, HandleBackgroundClick, true, FocusQuest, HeaderHeight);

            BuildChrome();
            if (_selectedQuestId is not null && _projection.NodesById.ContainsKey(_selectedQuestId))
            {
                _contentView.SetSelected(_selectedQuestId);
                ShowSelectedDetails();
            }
        }
        finally
        {
            mount.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(mount.gameObject);
        }
    }

    private GlobalQuestGraphProjection BuildProjection()
    {
        if (_topology is null || _layout is null || _overlay is null)
            throw new InvalidOperationException("Trader workspace data is not ready.");
        return GlobalQuestGraphProjectionBuilder.Build(
            _topology, _layout, _overlay,
            new GlobalQuestGraphOptions(
                _mode, _showAllFuture, _hideFinished, _levelEligibleOnly, null, _trader.Id,
                _search, _focusQuestId, QuestRouteFilter.None, _selectedQuestId, null, true,
                TraderTasksContext: _mode == GlobalQuestGraphMode.InProgress,
                TraderGraphContext: _mode == GlobalQuestGraphMode.Full,
                HideUnavailableTraderTasks: _mode == GlobalQuestGraphMode.InProgress && _hideUnavailable));
    }

    private void RebuildView(bool preserveViewport)
    {
        var viewport = preserveViewport ? _contentView?.CaptureViewportState() : null;
        PersistViewport();
        _detailPane?.Dispose();
        _detailPane = null;
        _contentView?.Dispose();
        _contentView = null;
        _detailsButton = null;
        _detailsButtonImage = null;
        BuildView(viewport, !preserveViewport);
        if (_vanillaList is not null) _vanillaList.gameObject.SetActive(false);
        if (_nativeQuestView is not null) _nativeQuestView.gameObject.SetActive(false);
    }

    private void ShowMode(GlobalQuestGraphMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _focusQuestId = null;
        RebuildView(false);
    }

    private void SelectQuest(string questId)
    {
        if (_contentView is null || _topology?.NodesById.ContainsKey(questId) != true) return;
        if (string.Equals(_selectedQuestId, questId, StringComparison.Ordinal))
        {
            if (_mode == GlobalQuestGraphMode.InProgress) ClearSelection();
            else ShowSelectedDetails();
            return;
        }

        _selectedQuestId = questId;
        SaveSharedSelection();
        if (_mode == GlobalQuestGraphMode.Full)
        {
            var next = BuildProjection();
            if (_projection is null || !SameMembership(_projection, next)) RebuildView(true);
            else _contentView.SetSelected(questId);
        }
        else _contentView.SetSelected(questId);
        ShowSelectedDetails();
        PersistViewport();
    }

    private void FocusQuest(string questId)
    {
        _selectedQuestId = questId;
        SaveSharedSelection();
        _focusQuestId = questId;
        RebuildView(true);
    }

    private void HandleBackgroundClick()
    {
        if (_focusQuestId is not null)
        {
            _focusQuestId = null;
            RebuildView(true);
            return;
        }
        ClearSelection();
    }

    private void ClearSelection()
    {
        if (_selectedQuestId is null && _focusQuestId is null) return;
        _selectedQuestId = null;
        SaveSharedSelection();
        _detailsVisible = false;
        _detailPane?.Hide();
        UpdateDetailsButton();
        if (_focusQuestId is not null)
        {
            _focusQuestId = null;
            RebuildView(true);
        }
        else _contentView?.SetSelected(null);
        PersistViewport();
    }

    private void ToggleDetails()
    {
        if (!_customDetailsEnabled || _selectedQuestId is null) return;
        _detailsVisible = !_detailsVisible;
        if (_detailsVisible) ShowSelectedDetails();
        else _detailPane?.Hide();
        UpdateDetailsButton();
    }

    private void ShowSelectedDetails()
    {
        if (!_customDetailsEnabled || _selectedQuestId is null || _contentView is null || _topology is null || _overlay is null)
            return;
        _detailPane ??= QuestDetailsPane.Create(
            _contentView.Root.parent as RectTransform
                ?? throw new InvalidOperationException("Trader workspace root has no overlay parent."),
            _contentView.Root.Find("Viewport") as RectTransform
                ?? throw new InvalidOperationException("Trader workspace viewport was not created."),
            _session, _inventoryController, _questController, _assetCache, _log,
            _showHiddenRewards, _defaultDetailsToSummary, HandleQuestMutation, _trader.Id, 2f / 3f);
        _detailPane.Show(_topology, _overlay, _selectedQuestId);
        _detailsVisible = true;
        _detailPane.ShowRoot();
        UpdateDetailsButton();
    }

    private void RefreshSelectedDetails()
    {
        if (_detailsVisible && _selectedQuestId is not null) ShowSelectedDetails();
    }

    private void BuildChrome()
    {
        if (_contentView is null || _overlay is null) return;
        var header = _contentView.Root.Find("Header") as RectTransform
            ?? throw new InvalidOperationException("Trader workspace header was not created.");
        QuestWorkspaceChrome.AddButton(header, "TasksTab", "TASKS", 8, -4, 76,
            _mode == GlobalQuestGraphMode.InProgress, () => ShowMode(GlobalQuestGraphMode.InProgress));
        QuestWorkspaceChrome.AddButton(header, "QuestMapTab", "QUEST MAP", 88, -4, 92,
            _mode == GlobalQuestGraphMode.Full, () => ShowMode(GlobalQuestGraphMode.Full));
        QuestWorkspaceChrome.AddSeparator(header, "ViewSeparator", 186);
        BuildSearch(header);
        QuestWorkspaceChrome.AddSeparator(header, "SearchSeparator", 454);

        if (_mode == GlobalQuestGraphMode.InProgress)
        {
            QuestWorkspaceChrome.AddButton(header, "CompletedTasks", _hideCompletedTasks ? "✓: HIDDEN" : "✓: SHOWN",
                464, -4, 96, _hideCompletedTasks, ToggleCompletedTasks);
            QuestWorkspaceChrome.AddButton(header, "Level", $"≤ {_overlay.Level}", 564, -4, 62,
                _levelEligibleOnly, ToggleLevel);
            QuestWorkspaceChrome.AddButton(header, "Unavailable", _hideUnavailable ? "UNAVAILABLE: HIDDEN" : "UNAVAILABLE: SHOWN",
                630, -4, 144, _hideUnavailable, ToggleUnavailable);
        }
        else
        {
            QuestWorkspaceChrome.AddButton(header, "Future", "FUTURE", 464, -4, 66, _showAllFuture, ToggleFuture);
            QuestWorkspaceChrome.AddButton(header, "Finished", "✓", 534, -4, 40, !_hideFinished, ToggleFinished);
            QuestWorkspaceChrome.AddButton(header, "Level", $"≤ {_overlay.Level}", 578, -4, 62,
                _levelEligibleOnly, ToggleLevel);
        }

        if (_customDetailsEnabled)
        {
            _detailsButtonImage = QuestWorkspaceChrome.AddRightButton(
                header, "QuestDescription", "QUEST DESCRIPTION", 8, 144, _detailsVisible, ToggleDetails,
                _selectedQuestId is not null);
            _detailsButton = _detailsButtonImage.GetComponent<Button>();
        }
        UpdateDetailsButton();
    }

    private void BuildSearch(RectTransform header)
    {
        var root = UnityUiFactory.CreateRect("GraphSearch", header);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
        root.anchoredPosition = new Vector2(196, -4);
        root.sizeDelta = new Vector2(220, 32);
        root.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.115f, 0.11f, 1);
        var viewport = UnityUiFactory.CreateRect("Text Area", root);
        UnityUiFactory.Stretch(viewport, 8, 8, 3, 3);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = UnityUiFactory.AddText(viewport.gameObject, _search, 12, TextAlignmentOptions.MidlineLeft, Color.white);
        var placeholder = UnityUiFactory.AddText(viewport.gameObject, "QUEST OR ID", 12,
            TextAlignmentOptions.MidlineLeft, new Color(0.55f, 0.57f, 0.58f, 1));
        var input = root.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.SetTextWithoutNotify(_search);
        input.onEndEdit.AddListener(value =>
        {
            var next = value?.Trim() ?? string.Empty;
            if (string.Equals(next, _search, StringComparison.Ordinal)) return;
            _search = next;
            SaveSharedFilters();
            RebuildView(true);
        });
        QuestWorkspaceChrome.AddButton(header, "ClearSearch", "×", 420, -4, 30,
            !string.IsNullOrEmpty(_search), () =>
            {
                if (string.IsNullOrEmpty(_search)) return;
                _search = string.Empty;
                SaveSharedFilters();
                RebuildView(true);
            });
    }

    private void UpdateDetailsButton()
    {
        if (_detailsButtonImage is not null)
            _detailsButtonImage.color = _detailsVisible ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control;
        if (_detailsButton is not null) _detailsButton.interactable = _selectedQuestId is not null;
    }

    private IReadOnlyList<QuestGraphHeaderAction> BuildCanvasActions() =>
    [
        new QuestGraphHeaderAction("Focus", _focusQuestId is null ? "FOCUS CHAIN" : "UNFOCUS", 92,
            _focusQuestId is not null, ToggleFocus),
        new QuestGraphHeaderAction("Clear", "CLEAR SELECTION", 106, _selectedQuestId is not null, ClearSelection),
    ];

    private void ToggleFocus()
    {
        _focusQuestId = _focusQuestId is null ? _selectedQuestId : null;
        if (_focusQuestId is not null || _selectedQuestId is not null) RebuildView(true);
    }

    private void ToggleFuture() { _showAllFuture = !_showAllFuture; SaveSharedFilters(); RebuildView(true); }
    private void ToggleFinished() { _hideFinished = !_hideFinished; SaveSharedFilters(); RebuildView(true); }
    private void ToggleLevel() { _levelEligibleOnly = !_levelEligibleOnly; SaveSharedFilters(); RebuildView(true); }
    private void ToggleCompletedTasks() { _hideCompletedTasks = !_hideCompletedTasks; SaveSharedFilters(); RebuildView(true); }
    private void ToggleUnavailable() { _hideUnavailable = !_hideUnavailable; SaveSharedFilters(); RebuildView(true); }

    private void LoadSharedFilters()
    {
        if (_filterProfileId is null || !SharedFiltersByProfile.TryGetValue(_filterProfileId, out var state)) return;
        _showAllFuture = state.ShowAllFuture;
        _hideFinished = state.HideFinished;
        _levelEligibleOnly = state.LevelEligibleOnly;
        _hideCompletedTasks = state.HideCompletedTasks;
        _hideUnavailable = state.HideUnavailable;
        _search = state.Search;
    }

    private void SaveSharedFilters()
    {
        if (_filterProfileId is null) return;
        SharedFiltersByProfile[_filterProfileId] = new TraderTasksFilterState(
            _showAllFuture,
            _hideFinished,
            _levelEligibleOnly,
            _hideCompletedTasks,
            _hideUnavailable,
            _search);
    }

    private void LoadSharedSelection()
    {
        if (_filterProfileId is null
            || !SharedSelectionByProfileAndTrader.TryGetValue(SelectionScope(_filterProfileId, _trader.Id), out var questId)
            || _topology?.NodesById.ContainsKey(questId) != true)
            return;
        _selectedQuestId = questId;
    }

    private void SaveSharedSelection()
    {
        if (_filterProfileId is null) return;
        var scope = SelectionScope(_filterProfileId, _trader.Id);
        if (string.IsNullOrWhiteSpace(_selectedQuestId)) SharedSelectionByProfileAndTrader.Remove(scope);
        else SharedSelectionByProfileAndTrader[scope] = _selectedQuestId;
    }

    private static string SelectionScope(string profileId, string traderId) => $"{profileId}|{traderId}";

    private void HandleQuestMutation(string questId, QuestDetailsActionKind kind)
    {
        if (_disposed) return;
        // EFT's native transaction can ask the owning QuestsScreen to show its
        // stock QuestView again. The transaction remains authoritative, but
        // the trader replacement owns presentation while mounted. Suppress a
        // few late-layout frames as well as hiding it immediately so deferred
        // native observers cannot surface the old detail workspace behind us.
        _nativeWorkspaceSuppressor?.SuppressForFrames(4);
        _requestQuestRefresh(questId, kind);
    }

    private void ToggleSort(QuestTableSortColumn column)
    {
        var next = InProgressQuestTableSorter.Toggle(_sortCriteria, column);
        _sortCriteria.Clear();
        _sortCriteria.AddRange(next);
        if (_contentView is InProgressQuestTableView table && _projection is not null)
            table.UpdatePresentation(_projection, _hideCompletedTasks);
    }

    private void ToggleExpansion(string questId)
    {
        if (!_expandedQuestIds.Add(questId)) _expandedQuestIds.Remove(questId);
        if (_contentView is InProgressQuestTableView table) table.RefreshExpansion(questId);
    }

    private bool MutationsAllowed() => !InRaidQuestContext.TryCapture(out _);

    private NativeQuestHandoverAction? CreateHandoverAction(RectTransform parent, string questId, string objectiveId)
    {
        if (!MutationsAllowed()) return null;
        var quest = FindLiveQuest(questId);
        if (quest?.QuestStatus != EQuestStatus.Started) return null;
        return NativeQuestTableActions.TryCreateHandover(parent, quest, objectiveId, _questController, _inventoryController);
    }

    private bool CanAcceptQuest(string questId)
    {
        if (!MutationsAllowed() || _topology?.NodesById.TryGetValue(questId, out var node) != true
            || NativeQuestTableActions.IsRaidOnlyTrader(node.TraderId)) return false;
        return FindLiveQuest(questId)?.QuestStatus == EQuestStatus.AvailableForStart;
    }

    private async Task AcceptQuestAsync(RectTransform parent, string questId)
    {
        if (!CanAcceptQuest(questId) || _topology is null) return;
        await NativeQuestTableActions.AcceptAsync(parent, _session, _inventoryController, _questController,
            FindLiveQuest(questId)!, _topology.NodesById[questId].TraderId, _log);
    }

    private bool CanCompleteQuest(string questId)
    {
        if (!MutationsAllowed() || _topology?.NodesById.TryGetValue(questId, out var node) != true
            || NativeQuestTableActions.IsRaidOnlyTrader(node.TraderId)) return false;
        return FindLiveQuest(questId)?.QuestStatus == EQuestStatus.AvailableForFinish;
    }

    private async Task CompleteQuestAsync(RectTransform parent, string questId)
    {
        if (!CanCompleteQuest(questId) || _topology is null) return;
        await NativeQuestTableActions.CompleteAsync(parent, _session, _inventoryController, _questController,
            FindLiveQuest(questId)!, _topology.NodesById[questId].TraderId, _log);
    }

    private bool CanReplaceQuest(string questId)
    {
        if (!MutationsAllowed() || _topology?.NodesById.TryGetValue(questId, out var node) != true
            || node.RepeatableKind is not ("Daily" or "Weekly")) return false;
        return FindLiveQuest(questId)?.IsChangeAllowed == true;
    }

    private async Task ReplaceQuestAsync(RectTransform parent, string questId)
    {
        if (!CanReplaceQuest(questId) || _topology is null) return;
        await NativeQuestTableActions.ReplaceAsync(parent, _session, _inventoryController, _questController,
            FindLiveQuest(questId)!, _topology.NodesById[questId].TraderId, _log);
    }

    private QuestClass? FindLiveQuest(string questId) => _questController.Quests.LastOrDefault(
        quest => string.Equals(quest.Id, questId, StringComparison.Ordinal));

    private void PersistViewport()
    {
        if (_contentView is null || string.IsNullOrWhiteSpace(_viewStateScope)) return;
        QuestGraphViewStateStore.Save(_viewStateScope!, _contentView.CaptureViewportState(), null);
    }

    private void ResolveSurfaceBounds()
    {
        if (_screen.transform is RectTransform screenRect && screenRect.rect.width > 0 && screenRect.rect.height > HeaderHeight)
        {
            // QuestsScreen is the complete trader task workspace below the
            // trader selector. Filling it replaces the list, stock detail, and
            // native action strip without covering the trader navigation.
            _surfaceParent = screenRect;
            var nativeListRect = _vanillaList?.transform as RectTransform;
            var nativeDetailRect = _nativeQuestView?.transform as RectTransform;
            if (nativeListRect is not null && nativeDetailRect is not null)
            {
                var nativeBounds = LocalUnion(screenRect, nativeListRect, nativeDetailRect);
                _surfaceBounds = Rect.MinMaxRect(
                    screenRect.rect.xMin,
                    Mathf.Clamp(nativeBounds.yMin, screenRect.rect.yMin, screenRect.rect.yMax - HeaderHeight),
                    screenRect.rect.xMax,
                    screenRect.rect.yMax);
            }
            else _surfaceBounds = screenRect.rect;
            return;
        }

        var listRect = _vanillaList?.transform as RectTransform
            ?? throw new InvalidOperationException("The trader quest list has no RectTransform.");
        var detailRect = _nativeQuestView?.transform as RectTransform
            ?? throw new InvalidOperationException("The trader quest detail has no RectTransform.");
        _surfaceParent = FindCommonRectAncestor(listRect, detailRect)
            ?? throw new InvalidOperationException("The trader quest list and detail pane have no common RectTransform ancestor.");
        _surfaceBounds = LocalUnion(_surfaceParent, listRect, detailRect);
        if (_surfaceBounds.width <= 0 || _surfaceBounds.height <= HeaderHeight)
            throw new InvalidOperationException($"The trader task surface is invalid: {_surfaceBounds.width:0.#}x{_surfaceBounds.height:0.#}.");
    }

    private RectTransform CreateSurfaceMount()
    {
        if (_surfaceParent is null) throw new InvalidOperationException("Trader surface parent is unavailable.");
        var mount = UnityUiFactory.CreateRect("QuestMapTraderSurfaceMount", _surfaceParent);
        mount.anchorMin = mount.anchorMax = Vector2.zero;
        mount.pivot = Vector2.zero;
        mount.anchoredPosition = new Vector2(
            _surfaceBounds.xMin - _surfaceParent.rect.xMin,
            _surfaceBounds.yMin - _surfaceParent.rect.yMin);
        mount.sizeDelta = _surfaceBounds.size;
        mount.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        mount.SetAsLastSibling();
        return mount;
    }

    private static GClass3794? ResolveFavoriteQuestService(params object[] owners)
    {
        foreach (var owner in owners)
        {
            for (var type = owner.GetType(); type is not null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(GClass3794).IsAssignableFrom(field.FieldType)) continue;
                    try
                    {
                        if (field.GetValue(owner) is GClass3794 service) return service;
                    }
                    catch
                    {
                        // Continue across version-pinned fields until the live service is found.
                    }
                }
            }
        }
        return null;
    }

    private static bool SameMembership(GlobalQuestGraphProjection left, GlobalQuestGraphProjection right) =>
        left.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal)
            .SetEquals(right.Nodes.Select(node => node.Id));

    private static RectTransform? FindCommonRectAncestor(params RectTransform[] transforms)
    {
        for (Transform? candidate = transforms[0]; candidate is not null; candidate = candidate.parent)
        {
            if (candidate is RectTransform rect && transforms.All(transform => transform == rect || transform.IsChildOf(rect)))
                return rect;
        }
        return null;
    }

    private static Rect LocalUnion(RectTransform parent, params RectTransform[] transforms)
    {
        var corners = new Vector3[4];
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var transform in transforms)
        {
            transform.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var local = parent.InverseTransformPoint(corner);
                minimum = Vector2.Min(minimum, local);
                maximum = Vector2.Max(maximum, local);
            }
        }
        return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
    }
}

internal readonly struct TraderTasksFilterState
{
    public TraderTasksFilterState(
        bool showAllFuture,
        bool hideFinished,
        bool levelEligibleOnly,
        bool hideCompletedTasks,
        bool hideUnavailable,
        string search)
    {
        ShowAllFuture = showAllFuture;
        HideFinished = hideFinished;
        LevelEligibleOnly = levelEligibleOnly;
        HideCompletedTasks = hideCompletedTasks;
        HideUnavailable = hideUnavailable;
        Search = search;
    }

    public bool ShowAllFuture { get; }
    public bool HideFinished { get; }
    public bool LevelEligibleOnly { get; }
    public bool HideCompletedTasks { get; }
    public bool HideUnavailable { get; }
    public string Search { get; }
}

internal sealed class TraderNativeWorkspaceSuppressor : MonoBehaviour
{
    private QuestsListView? _list;
    private QuestView? _details;
    private int _remainingFrames;

    public void Bind(QuestsListView list, QuestView details)
    {
        _list = list;
        _details = details;
        enabled = false;
    }

    public void SuppressForFrames(int frames)
    {
        _remainingFrames = Math.Max(_remainingFrames, frames);
        HideNativeWorkspace();
        enabled = true;
    }

    private void LateUpdate()
    {
        HideNativeWorkspace();
        if (--_remainingFrames <= 0)
        {
            _remainingFrames = 0;
            enabled = false;
        }
    }

    private void HideNativeWorkspace()
    {
        if (_list != null && _list.gameObject.activeSelf) _list.gameObject.SetActive(false);
        if (_details != null && _details.gameObject.activeSelf) _details.gameObject.SetActive(false);
    }
}
