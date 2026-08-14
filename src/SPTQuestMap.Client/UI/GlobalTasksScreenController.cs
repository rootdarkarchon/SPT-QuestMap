using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Client.Localization;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class GlobalTasksScreenController : IDisposable
{
    private const float MinimumCharacterNavigationInset = 40f;
    private const float CharacterNavigationInset = 44f;
    private const string FallbackLocationBannerUrl = "/files/banners/norvinskzone.png";

    private static readonly FieldInfo TasksPanelField = RequiredField("_tasksPanel");
    private static readonly FieldInfo DefaultToggleField = RequiredField("_defaultQuestsToggleSpawner");
    private static readonly FieldInfo DailyToggleField = RequiredField("_dailyQuestsToggleSpawner");
    private static readonly FieldInfo NotesToggleField = RequiredField("_notesToggleSpawner");
    private static readonly FieldInfo QuestItemsToggleField = RequiredField("_questItemsToggleSpawner");
    private static readonly FieldInfo QuestsAdditionalFilterField = RequiredField("_questsAdditionalFilter");
    private static readonly FieldInfo NotesPartField = RequiredField("_notesPart");
    private static readonly FieldInfo QuestItemsPartField = RequiredField("_questItemsPart");
    private static readonly FieldInfo SearchField = RequiredField("_searchField");
    private static readonly FieldInfo ScreenTooltipField = RequiredField("_tooltip");
    private static readonly FieldInfo TasksDescriptionField = RequiredTasksPanelField("_notesTaskDescription");
    private static readonly FieldInfo TasksTooltipField = RequiredTasksPanelField("simpleTooltip_0");
    private static readonly FieldInfo FavoriteQuestServiceField = RequiredTasksPanelField("gclass3794_0");

    private readonly TasksScreen _screen;
    private readonly NativeQuestWorkspaceContext _workspace;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly QuestTrackingService _tracking;
    private readonly Action<string, QuestDetailsActionKind> _requestQuestRefresh;
    private readonly Dictionary<GameObject, bool> _nativeControlStates = new();
    private TasksPanel? _tasksPanel;
    private GameObject? _tasksDescription;
    private SimpleTooltip? _screenTooltip;
    private SimpleTooltip? _tasksTooltip;
    private GClass3794? _favoriteQuestService;
    private RectTransform? _notesPart;
    private RectTransform? _questItemsPart;
    private RectTransform? _nativeSearch;
    private RectTransformSnapshot? _notesPartLayout;
    private RectTransformSnapshot? _questItemsPartLayout;
    private RectTransformSnapshot? _nativeSearchLayout;
    private RectTransform? _notesBackdrop;
    private RectTransform? _questItemsBackdrop;
    private readonly Dictionary<RectTransform, RectTransformSnapshot> _questItemsScrollLayouts = new();
    private RectMask2D? _ownedQuestItemsMask;
    private Toggle? _notesToggle;
    private Toggle? _questItemsToggle;
    private Func<QuestClass, bool>? _questsAdditionalFilter;
    private QuestGraphTopology? _topology;
    private QuestGraphLayout? _layout;
    private QuestProfileOverlay? _overlay;
    private IGlobalTasksContentView? _graphView;
    private GlobalQuestGraphProjection? _projection;
    private RectTransform? _selectionSummary;
    private TextMeshProUGUI? _selectionSummaryText;
    private Image? _notesButtonImage;
    private Image? _questItemsButtonImage;
    private TMP_Text? _questItemsButtonLabel;
    private GameObject? _questItemsWarningBadge;
    private TMP_Text? _questItemsWarningCount;
    private Image? _questDescriptionButtonImage;
    private Button? _questDescriptionButton;
    private QuestDetailsPane? _detailPane;
    private GlobalOverlayMode _overlayMode;
    private GlobalQuestGraphMode _mode = GlobalQuestGraphMode.InProgress;
    private bool _raidInProgressOnly;
    private bool _showAllFuture;
    private bool _hideFinished;
    private bool _levelEligibleOnly = true;
    private string? _traderId;
    private string _search = string.Empty;
    private readonly HashSet<string> _inProgressLocationIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MapFilterVisual> _inProgressMapVisuals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _inProgressMapAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<QuestTableSortCriterion> _inProgressSortCriteria = [];
    private readonly HashSet<string> _expandedInProgressQuestIds = new(StringComparer.Ordinal);
    private bool _hideCompletedInProgressTasks;
    private bool _includeAvailableRepeatables;
    private bool _inProgressLocationsInitialized;
    private string? _focusQuestId;
    private string? _selectedQuestId;
    private string? _viewStateScope;
    private bool _tasksPanelWasActive;
    private bool _tasksDescriptionWasActive;
    private float _surfaceTopInset;
    private string _surfaceTopSource = "native-union";
    private bool _mounted;
    private bool _visible;
    private bool _inventoryUpdatesSubscribed;
    private bool _disposed;

    public GlobalTasksScreenController(
        TasksScreen screen,
        NativeQuestWorkspaceContext workspace,
        QuestAssetSpriteCache assetCache,
        QuestTrackingService tracking,
        Action<string, QuestDetailsActionKind> requestQuestRefresh)
    {
        _screen = screen;
        _workspace = workspace;
        _assetCache = assetCache;
        _tracking = tracking;
        _requestQuestRefresh = requestQuestRefresh;
    }

    private InventoryController InventoryController => _workspace.InventoryController;
    private AbstractQuestControllerClass QuestController => _workspace.QuestController;
    private ISession Session => _workspace.Session;
    private ManualLogSource Log => _workspace.Log;
    private const bool CustomDetailsEnabled = true;

    public void Mount(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlobalTasksScreenController));
        if (_mounted) throw new InvalidOperationException("The global Tasks graph is already mounted for this screen instance.");
        _tasksPanel = TasksPanelField.GetValue(_screen) as TasksPanel
            ?? throw new InvalidOperationException("TasksScreen._tasksPanel was null.");
        _notesPart = (NotesPartField.GetValue(_screen) as GameObject)?.transform as RectTransform
            ?? throw new InvalidOperationException("TasksScreen._notesPart has no RectTransform.");
        _questItemsPart = (QuestItemsPartField.GetValue(_screen) as GameObject)?.transform as RectTransform
            ?? throw new InvalidOperationException("TasksScreen._questItemsPart has no RectTransform.");
        _nativeSearch = (SearchField.GetValue(_screen) as Component)?.transform as RectTransform
            ?? throw new InvalidOperationException("TasksScreen._searchField has no RectTransform.");
        _tasksDescription = TasksDescriptionField.GetValue(_tasksPanel) as GameObject
            ?? throw new InvalidOperationException("TasksPanel._notesTaskDescription was null.");
        _screenTooltip = ScreenTooltipField.GetValue(_screen) as SimpleTooltip;
        _tasksTooltip = TasksTooltipField.GetValue(_tasksPanel) as SimpleTooltip;
        _favoriteQuestService = FavoriteQuestServiceField.GetValue(_tasksPanel) as GClass3794
            ?? throw new InvalidOperationException("TasksPanel native favorite-quest service was null.");
        _questsAdditionalFilter = QuestsAdditionalFilterField.GetValue(_screen) as Func<QuestClass, bool>;
        _tasksPanelWasActive = _tasksPanel.gameObject.activeSelf;
        _tasksDescriptionWasActive = _tasksDescription.activeSelf;
        _topology = topology;
        _layout = layout;
        _overlay = overlay;
        LoadSettings();
        if (InRaidQuestContext.TryCapture(out var raidContext))
        {
            _raidInProgressOnly = true;
            _mode = GlobalQuestGraphMode.InProgress;
            _focusQuestId = null;
            ApplyInRaidDefaults(raidContext);
        }
        else InitializeInProgressLocations();

        var defaultToggle = ResolveSingleToggle(DefaultToggleField, "default quests");
        var dailyToggle = ResolveSingleToggle(DailyToggleField, "daily quests");
        _notesToggle = ResolveSingleToggle(NotesToggleField, "notes");
        _questItemsToggle = ResolveSingleToggle(QuestItemsToggleField, "quest items");
        HideNativeControl(DefaultToggleField);
        HideNativeControl(DailyToggleField);
        HideNativeControl(NotesToggleField);
        HideNativeControl(QuestItemsToggleField);
        ClearNativeOverlays();

        BuildGraphOnFullSurface(null, false);
        HideNativeTasksWorkspace();
        _mounted = true;
        _visible = true;
        SubscribeInventoryUpdates();
        QuestMapDebugLog.Info(Log,
            "QUESTMAP_M06_MOUNT " +
            $"screen={_screen.GetInstanceID()}; mode={_mode}; nodes={_projection?.Nodes.Count ?? 0}; " +
            $"edges={_projection?.Edges.Count ?? 0}; nativeNotes=True; nativeQuestItems=True; fullSurface=True; nativeSelectorsUnmodified=True; " +
            $"raidInProgressOnly={_raidInProgressOnly}; favoriteQuestService=native; assetCache=shared; cachedAssets={_assetCache.CachedCount}; " +
            $"pendingAssets={_assetCache.PendingCount}; activeAssetRequests={_assetCache.ActiveRequestCount}; " +
            $"queuedAssets={_assetCache.QueuedCount}; networkRequests={_assetCache.NetworkRequests}; cacheHits={_assetCache.CacheHits}");
    }

    public bool CanResume => !_disposed && _mounted && _graphView?.Root != null;

    public bool IsVisible => !_disposed && _visible;

    public string Resume(
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        ISession session,
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay)
    {
        if (!CanResume) return "screen-cache-invalid";
        var runtimeContextChanged = RebindRuntimeContext(session, inventoryController, questController);

        _favoriteQuestService = FavoriteQuestServiceField.GetValue(_tasksPanel) as GClass3794
            ?? throw new InvalidOperationException("TasksPanel native favorite-quest service was null while resuming.");
        if (_graphView is InProgressQuestTableView existingTable)
            existingTable.RebindFavoriteQuestService(_favoriteQuestService);

        var modeChanged = UpdateRaidMode();
        var topologyChanged = _topology is null || _layout is null
            || !string.Equals(_topology.Version, topology.Version, StringComparison.Ordinal)
            || !string.Equals(_layout.TopologyVersion, layout.TopologyVersion, StringComparison.Ordinal);
        if (topologyChanged || modeChanged)
        {
            _topology = topology;
            _layout = layout;
            _overlay = overlay;
            RebuildGraph(true);
        }
        else
        {
            RefreshOverlay(overlay);
        }

        HideNativeControl(DefaultToggleField);
        HideNativeControl(DailyToggleField);
        HideNativeControl(NotesToggleField);
        HideNativeControl(QuestItemsToggleField);
        ClearNativeOverlays();
        HideNativeTasksWorkspace();
        if (_graphView is not null)
        {
            _graphView.Root.gameObject.SetActive(true);
            ConfigureNativeSidePanels();
            PlaceGraphBehindNativeSidePanels();
            if (_selectedQuestId is not null) ShowSelectedQuestDetails();
        }
        _visible = true;
        SubscribeInventoryUpdates();
        UpdateQuestItemsButtonState();
        var cachedRows = (_graphView as InProgressQuestTableView)?.CachedRowCount ?? 0;
        QuestMapDebugLog.Info(Log,
            "QUESTMAP_M06_RESUME " +
            $"screen={_screen.GetInstanceID()}; topologyChanged={topologyChanged}; modeChanged={modeChanged}; runtimeContextChanged={runtimeContextChanged}; " +
            $"mode={_mode}; cachedRows={cachedRows}; rootReused={!topologyChanged && !modeChanged}");
        return topologyChanged || modeChanged ? "global-content-rebuilt" : "global-screen-reused";
    }

    public void Suspend()
    {
        if (_disposed || !_mounted || !_visible) return;
        PersistCurrentState();
        QuestGraphViewStateStore.Flush();
        ClearNativeOverlays();
        _detailPane?.Dispose();
        _detailPane = null;
        if (_graphView is InProgressQuestTableView table) table.ReleaseNativeRuntimeBindings(false);
        if (_graphView?.Root != null) _graphView.Root.gameObject.SetActive(false);
        _visible = false;
        UnsubscribeInventoryUpdates();
        var cachedRows = (_graphView as InProgressQuestTableView)?.CachedRowCount ?? 0;
        QuestMapDebugLog.Info(Log, $"QUESTMAP_M06_SUSPEND screen={_screen.GetInstanceID()}; cached=True; rows={cachedRows}");
    }

    public string RefreshOverlay(QuestProfileOverlay overlay, bool refreshAllNativeControls = true)
    {
        if (_disposed || !_mounted || _topology is null || _layout is null || _graphView is null) return "screen-inactive";
        var locationsChanged = EnableLocationsForNewTaskStates(_overlay, overlay);
        _overlay = overlay;
        UpdateQuestItemsButtonState();
        var nextMembership = BuildProjection(membershipOnly: true);
        var membershipChanged = _projection is null || !SameMembership(_projection, nextMembership);
        if (_graphView is InProgressQuestTableView table)
        {
            _projection = nextMembership;
            if (_selectedQuestId is not null && !_projection.NodesById.ContainsKey(_selectedQuestId))
            {
                _selectedQuestId = null;
                CloseQuestDetails();
            }
            table.RefreshOverlay(overlay, _selectedQuestId, nextMembership, refreshAllNativeControls);
            if (membershipChanged || locationsChanged) RebuildInProgressChrome();
            UpdateSelectionDetails();
            return membershipChanged ? "global-projection-updated" : "global-overlay-refreshed";
        }
        if (membershipChanged)
        {
            RebuildGraph(true);
            return "global-projection-rebuilt";
        }

        _graphView.RefreshOverlay(overlay, _selectedQuestId);
        // Full-map overlay refreshes previously updated only the card surface.
        // Keep the open details pane bound to the same authoritative overlay as
        // the graph so native accept/handover/turn-in changes appear in-place.
        UpdateSelectionDetails();
        return "global-overlay-refreshed";
    }

    public string RefreshQuest(QuestProfileOverlay overlay, string questId)
        => RefreshQuests(overlay, new[] { questId });

    public string RefreshQuests(QuestProfileOverlay overlay, IReadOnlyCollection<string> questIds)
    {
        if (_disposed || !_mounted || _graphView is null) return "screen-inactive";
        var changedQuestIds = questIds.Distinct(StringComparer.Ordinal).ToArray();
        if (changedQuestIds.Length == 0) return "global-quests-unchanged";
        var locationsChanged = EnableLocationsForNewTaskStates(_overlay, overlay);
        _overlay = overlay;
        UpdateQuestItemsButtonState();
        var nextMembership = BuildProjection(membershipOnly: true);
        var membershipChanged = _projection is null || !SameMembership(_projection, nextMembership);
        if (membershipChanged)
        {
            _projection = nextMembership;
            if (_selectedQuestId is not null && !_projection.NodesById.ContainsKey(_selectedQuestId))
            {
                _selectedQuestId = null;
                _focusQuestId = null;
                CloseQuestDetails();
            }
            if (_graphView is InProgressQuestTableView table)
            {
                table.RefreshOverlay(overlay, _selectedQuestId, nextMembership, false);
                RebuildInProgressChrome();
            }
            else RebuildGraph(true);
            UpdateSelectionDetails();
            return "global-projection-updated";
        }
        if (_graphView is InProgressQuestTableView) _projection = nextMembership;
        var visibleChangedQuestIds = changedQuestIds
            .Where(questId => nextMembership.NodesById.ContainsKey(questId))
            .ToArray();
        _graphView.RefreshQuests(overlay, visibleChangedQuestIds);
        if (locationsChanged && _graphView is InProgressQuestTableView) RebuildInProgressChrome();
        if (_selectedQuestId is not null && changedQuestIds.Contains(_selectedQuestId, StringComparer.Ordinal))
            UpdateSelectionDetails();
        return visibleChangedQuestIds.Length == 0
            ? "global-quests-not-visible"
            : $"global-quests-refreshed:{visibleChangedQuestIds.Length}";
    }

    public string RebuildTopology(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay)
    {
        if (_disposed || !_mounted) return "screen-inactive";
        _topology = topology;
        _layout = layout;
        _overlay = overlay;
        RebuildGraph(true);
        return _selectedQuestId is null ? "global-selection-none" : "global-selection-preserved";
    }

    public string ApplyRepeatableTopologyDelta(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay)
    {
        if (_disposed || !_mounted || _graphView is null) return "screen-inactive";
        var stopwatch = Stopwatch.StartNew();
        _topology = topology;
        _layout = layout;
        _overlay = overlay;
        var nextProjection = BuildProjection();
        if (_selectedQuestId is not null && !nextProjection.NodesById.ContainsKey(_selectedQuestId))
        {
            _selectedQuestId = null;
            _focusQuestId = null;
            CloseQuestDetails();
        }

        var applied = _graphView switch
        {
            InProgressQuestTableView table => ApplyTableTopologyDelta(table, nextProjection),
            QuestGraphView graph => graph.ApplyTopologyDelta(nextProjection, topology, overlay, _selectedQuestId),
            _ => false,
        };
        if (!applied)
        {
            RebuildGraph(true);
            return "global-topology-delta-fallback";
        }

        _projection = nextProjection;
        if (_mode == GlobalQuestGraphMode.InProgress) RebuildInProgressChrome();
        UpdateSelectionDetails();
        stopwatch.Stop();
        QuestMapDebugLog.Info(Log,
            "QUESTMAP_M06_TOPOLOGY_DELTA " +
            $"mode={_mode}; nodes={nextProjection.Nodes.Count}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}; fullRebuild=False");
        return "global-topology-delta-applied";
    }

    private bool ApplyTableTopologyDelta(
        InProgressQuestTableView table,
        GlobalQuestGraphProjection nextProjection)
    {
        table.ApplyTopologyDelta(
            _topology!,
            _overlay!,
            nextProjection,
            BuildInProgressCacheProjection().Nodes,
            _selectedQuestId);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        PersistCurrentState();
        QuestGraphViewStateStore.Flush();
        _disposed = true;
        UnsubscribeInventoryUpdates();
        _visible = false;
        foreach (var pair in _nativeControlStates)
            if (pair.Key != null) pair.Key.SetActive(pair.Value);
        _nativeControlStates.Clear();
        _inProgressMapVisuals.Clear();
        _detailPane?.Dispose();
        _detailPane = null;
        _graphView?.Dispose();
        _graphView = null;
        _selectionSummary = null;
        _selectionSummaryText = null;
        _notesButtonImage = null;
        _questItemsButtonImage = null;
        _questItemsButtonLabel = null;
        _questItemsWarningBadge = null;
        _questItemsWarningCount = null;
        _questDescriptionButtonImage = null;
        _questDescriptionButton = null;
        RestoreNativeSidePanelLayout();
        foreach (var ownedRoot in _screen == null
                     ? Array.Empty<RectTransform>()
                     : _screen.GetComponentsInChildren<RectTransform>(true)
                         .Where(rect => rect.name == "QuestMapGraph")
                         .ToArray())
        {
            UnityEngine.Object.Destroy(ownedRoot.gameObject);
        }
        if (_tasksPanel is not null)
        {
            _tasksPanel.gameObject.SetActive(_tasksPanelWasActive);
        }
        if (_tasksDescription is not null) _tasksDescription.SetActive(_tasksDescriptionWasActive);
        QuestMapDebugLog.Info(Log, $"QUESTMAP_M06_DISPOSE screen={(_screen == null ? 0 : _screen.GetInstanceID())}; vanillaRestored=True");
    }

    private void ShowMode(GlobalQuestGraphMode mode)
    {
        if (_disposed || !_mounted || _tasksPanel is null) return;
        if (_raidInProgressOnly && mode == GlobalQuestGraphMode.Full)
        {
            QuestMapDebugLog.Info(Log, $"QUESTMAP_M06_VIEW screen={_screen.GetInstanceID()}; view=Full; blocked=True; reason=in-raid-in-progress-only");
            return;
        }
        if (_mode != mode)
        {
            PersistCurrentState();
            _mode = mode;
            _focusQuestId = null;
            if (_mode == GlobalQuestGraphMode.InProgress) InitializeInProgressLocations();
            RebuildGraph(false);
        }
        ClearNativeOverlays();
        HideNativeTasksWorkspace();
        if (_graphView is not null)
        {
            _graphView.Root.gameObject.SetActive(true);
            if (CustomDetailsEnabled && _selectedQuestId is not null) ShowSelectedQuestDetails();
        }
        QuestMapDebugLog.Info(Log, $"QUESTMAP_M06_VIEW screen={_screen.GetInstanceID()}; view={mode}; native=False");
    }

    private void RebuildGraph(bool preserveViewport)
    {
        if (_tasksPanel is null || _disposed) return;
        try
        {
            var viewport = preserveViewport ? _graphView?.CaptureViewportState() : null;
            PersistCurrentState();
            _detailPane?.Dispose();
            _detailPane = null;
            _graphView?.Dispose();
            _graphView = null;
            BuildGraphOnFullSurface(viewport, !preserveViewport);
            HideNativeTasksWorkspace();
        }
        catch (Exception exception)
        {
            Log.LogError($"QUESTMAP_M06_ERROR phase=post-mount-rebuild; {exception}");
            Dispose();
            _tasksPanel?.Show(InventoryController, QuestController, Session, _questsAdditionalFilter);
            if (_tasksPanel is not null) _tasksPanel.gameObject.SetActive(true);
            Log.LogWarning($"QUESTMAP_M06_STATE screen={_screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=post-mount rebuild failed; vanillaRestored=True");
        }
    }

    private void BuildGraphOnFullSurface(GraphViewportState? viewport, bool loadPersisted)
    {
        var mount = CreateFullSurfaceMount();
        try
        {
            BuildGraph(mount, viewport, loadPersisted);
            ConfigureNativeSidePanels();
            PlaceGraphBehindNativeSidePanels();
        }
        finally
        {
            mount.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(mount.gameObject);
        }
    }

    private void BuildGraph(RectTransform mountRect, GraphViewportState? liveViewport, bool loadPersisted)
    {
        if (_topology is null || _overlay is null) throw new InvalidOperationException("Global graph data is not ready.");
        _projection = BuildProjection();
        var viewScope = _mode == GlobalQuestGraphMode.InProgress ? "global-v3:InProgress" : "global-v2:Full";
        _viewStateScope = QuestGraphViewStateStore.Scope(_topology.Version, _overlay.ProfileId, viewScope);
        PersistedQuestGraphViewState persisted = default;
        var hasPersisted = loadPersisted && QuestGraphViewStateStore.TryLoad(_viewStateScope, out persisted);
        // Pan/zoom is view-scoped; selection is controller-scoped. Restoring a
        // per-tab selection here resurrected stale quests after the other tab
        // had explicitly deselected them and also selected a row on first open.
        if (_selectedQuestId is not null && !_projection.NodesById.ContainsKey(_selectedQuestId))
        {
            _selectedQuestId = null;
            _focusQuestId = null;
        }

        var initialViewport = liveViewport ?? (hasPersisted ? persisted.Viewport : null);
        _graphView = _mode == GlobalQuestGraphMode.InProgress
            ? InProgressQuestTableView.Create(
                mountRect,
                _projection,
                _topology,
                _overlay,
                BuildInProgressCacheProjection().Nodes,
                _inProgressSortCriteria,
                _hideCompletedInProgressTasks,
                _expandedInProgressQuestIds,
                SelectQuest,
                ClearSelection,
                ToggleInProgressSort,
                ToggleInProgressQuestExpansion,
                 Log,
                 _assetCache,
                 _favoriteQuestService ?? throw new InvalidOperationException("Native favorite-quest service is unavailable."),
                 _tracking,
                 _workspace,
                  _requestQuestRefresh,
                  initialViewport,
                  locationIds: _inProgressLocationIds)
            : QuestGraphView.Create(
                mountRect,
                BuildTitle(),
                _projection,
                _topology,
                _overlay,
                SelectQuest,
                PersistCurrentState,
                Log,
                _workspace.DebugLogging,
                _assetCache,
                initialViewport,
                BuildCanvasActions(),
                true,
                HandleBackgroundClick,
                true,
                FocusQuest);
        Canvas.ForceUpdateCanvases();
        var viewportSize = _graphView.ViewportSize;
        var rootSize = _graphView.Root.rect.size;
        QuestMapDebugLog.Info(Log,
            "QUESTMAP_M06_GEOMETRY " +
            $"surface={FormatSize(mountRect.rect.size)}; parent={FormatSize((mountRect.parent as RectTransform)?.rect.size ?? Vector2.zero)}; " +
            $"topInset={_surfaceTopInset:0.##}; topSource={_surfaceTopSource}; root={FormatSize(rootSize)}; viewport={FormatSize(viewportSize)}; activeNodes={_graphView.ActiveNodeCount}");
        if (viewportSize.x < 100 || viewportSize.y < 100 || _projection.Nodes.Count > 0 && _graphView.ActiveNodeCount == 0)
        {
            throw new InvalidOperationException(
                $"Global graph geometry did not settle: root={FormatSize(rootSize)}, viewport={FormatSize(viewportSize)}, activeNodes={_graphView.ActiveNodeCount}.");
        }
        BuildSelectionSummary();
        BuildGlobalChrome();
        if (_selectedQuestId is not null)
        {
            _graphView.SetSelected(_selectedQuestId);
            UpdateSelectionDetails();
            if (CustomDetailsEnabled) ShowSelectedQuestDetails();
        }
    }

    private GlobalQuestGraphProjection BuildProjection(
        bool applyLocationFilter = true,
        bool applyTraderFilter = true,
        bool membershipOnly = false)
    {
        if (_topology is null || _layout is null || _overlay is null) throw new InvalidOperationException("Global graph data is not ready.");
        var options = new GlobalQuestGraphOptions(
            _mode,
            _showAllFuture,
            _hideFinished,
            _levelEligibleOnly,
            null,
            applyTraderFilter ? _traderId : null,
            _search,
            _focusQuestId,
            QuestRouteFilter.None,
            _selectedQuestId,
            applyLocationFilter && _mode == GlobalQuestGraphMode.InProgress ? _inProgressLocationIds : null,
            _includeAvailableRepeatables,
            ExcludeReadyToFinish: _raidInProgressOnly);
        return membershipOnly || _mode == GlobalQuestGraphMode.InProgress
            ? GlobalQuestGraphProjectionBuilder.BuildMembershipOnly(_topology, _layout, _overlay, options)
            : GlobalQuestGraphProjectionBuilder.Build(_topology, _layout, _overlay, options);
    }

    private GlobalQuestGraphProjection BuildInProgressCacheProjection()
    {
        if (_topology is null || _layout is null || _overlay is null)
            throw new InvalidOperationException("Global graph data is not ready.");
        return GlobalQuestGraphProjectionBuilder.BuildMembershipOnly(
            _topology,
            _layout,
            _overlay,
            new GlobalQuestGraphOptions(
                GlobalQuestGraphMode.InProgress,
                false,
                false,
                false,
                null,
                null,
                string.Empty,
                null,
                QuestRouteFilter.None,
                IncludeAvailableRepeatables: true));
    }

    private static bool SameMembership(GlobalQuestGraphProjection left, GlobalQuestGraphProjection right) =>
        left.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal)
            .SetEquals(right.Nodes.Select(node => node.Id));

    private void InitializeInProgressLocations()
    {
        if (_inProgressLocationsInitialized || _topology is null || _layout is null || _overlay is null) return;
        foreach (var node in BuildProjection(applyLocationFilter: false, membershipOnly: true).Nodes)
            AddNodeLocationFilters(_inProgressLocationIds, node);
        _inProgressLocationsInitialized = true;
    }

    private bool EnableLocationsForNewTaskStates(QuestProfileOverlay? previous, QuestProfileOverlay current)
    {
        if (_raidInProgressOnly || previous is null || _topology is null) return false;
        var changed = false;
        foreach (var node in _topology.Nodes)
        {
            var before = QuestGraphRules.ClassifyProfileDisplayState(_topology, node, previous);
            var after = QuestGraphRules.ClassifyProfileDisplayState(_topology, node, current);
            if (IsTaskLocationTrigger(before) || !IsTaskLocationTrigger(after)) continue;
            changed |= AddNodeLocationFilters(_inProgressLocationIds, node);
        }
        if (changed) RefreshInProgressMapVisuals();
        return changed;
    }

    private static bool IsTaskLocationTrigger(QuestMapDisplayStateKind state) => state is
        QuestMapDisplayStateKind.Available or QuestMapDisplayStateKind.InProgress
        or QuestMapDisplayStateKind.ReadyToFinish or QuestMapDisplayStateKind.RestartableFailure;

    private void ApplyInRaidDefaults(InRaidQuestContext context)
    {
        _traderId = null;
        _inProgressLocationIds.Clear();
        _inProgressLocationIds.UnionWith(context.DefaultLocationIds(_topology!.MapAliases));
        _inProgressLocationsInitialized = true;
        QuestMapDebugLog.Info(Log,
            "QUESTMAP_M06_RAID_FILTERS " +
            $"location={context.LocationId}; trader=all; locations={string.Join(",", _inProgressLocationIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase))}");
    }

    private bool UpdateRaidMode()
    {
        var priorMode = _mode;
        var wasRaidOnly = _raidInProgressOnly;
        if (InRaidQuestContext.TryCapture(out var raidContext))
        {
            if (!wasRaidOnly) PersistCurrentState();
            _raidInProgressOnly = true;
            _mode = GlobalQuestGraphMode.InProgress;
            _focusQuestId = null;
            if (!wasRaidOnly) ApplyInRaidDefaults(raidContext);
        }
        else if (wasRaidOnly)
        {
            _raidInProgressOnly = false;
            _inProgressLocationsInitialized = false;
            _inProgressLocationIds.Clear();
            LoadSettings();
            InitializeInProgressLocations();
        }
        return priorMode != _mode || wasRaidOnly != _raidInProgressOnly;
    }

    private IReadOnlyList<QuestGraphHeaderAction> BuildCanvasActions()
    {
        return
        [
            new QuestGraphHeaderAction("Focus", _focusQuestId is null ? ClientLocale.Text("label.focusChain") : ClientLocale.Text("label.unfocus"), 92, _focusQuestId is not null, ToggleFocus,
                () => _selectedQuestId is null
                    ? ClientLocale.Text("tooltip.noQuestSelected")
                    : _focusQuestId is null
                        ? ClientLocale.Format("tooltip.focus", ClientLocale.Arg("quest", SelectedQuestName()))
                        : ClientLocale.Text("tooltip.unfocus"),
                enabled: _selectedQuestId is not null, selectionDependent: true),
            new QuestGraphHeaderAction("Clear", ClientLocale.Text("label.clearSelection"), 106, _selectedQuestId is not null, ClearSelection,
                () => ClientLocale.Text(_selectedQuestId is null ? "tooltip.noQuestSelected" : "tooltip.clearSelection"),
                enabled: _selectedQuestId is not null, selectionDependent: true),
        ];
    }

    private string BuildTitle()
    {
        var title = _mode == GlobalQuestGraphMode.InProgress ? ClientLocale.Text("label.tasks") : ClientLocale.Text("label.questMap");
        if (!string.IsNullOrWhiteSpace(_search))
            title = ClientLocale.Format("common.searchTitle", ClientLocale.Arg("title", title), ClientLocale.Arg("search", _search));
        return title;
    }

    private void SelectQuest(string questId)
    {
        if (_graphView is null || _topology is null || !_topology.NodesById.ContainsKey(questId)) return;
        if (string.Equals(_selectedQuestId, questId, StringComparison.Ordinal))
        {
            if (_mode == GlobalQuestGraphMode.InProgress)
            {
                ToggleQuestDetails();
                return;
            }
            if (CustomDetailsEnabled) ShowSelectedQuestDetails();
            return;
        }
        _selectedQuestId = questId;
        if (_mode == GlobalQuestGraphMode.Full)
        {
            var nextProjection = BuildProjection(membershipOnly: true);
            var membershipChanged = _projection is null
                || !_projection.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(nextProjection.Nodes.Select(node => node.Id));
            if (membershipChanged) RebuildGraph(true);
            else _graphView.SetSelected(questId);
        }
        else
        {
            _graphView.SetSelected(questId);
        }
        UpdateSelectionDetails();
        if (CustomDetailsEnabled) ShowSelectedQuestDetails();
        PersistCurrentState();
        var live = _overlay?.QuestsById.TryGetValue(questId, out var state) == true && state.HasLiveQuest;
        QuestMapDebugLog.Info(Log, $"QUESTMAP_M06_SELECT view={_mode}; quest={questId}; live={live}; detail=graph-selection");
    }

    private void FocusQuest(string questId)
    {
        if (_graphView is null || _topology is null || !_topology.NodesById.ContainsKey(questId)) return;
        _selectedQuestId = questId;
        _focusQuestId = questId;
        RebuildGraph(true);
    }

    private void ToggleFuture()
    {
        _showAllFuture = !_showAllFuture;
        if (!UpdateInProgressPresentation(true, true)) RebuildGraph(true);
    }

    private void ToggleFinished()
    {
        _hideFinished = !_hideFinished;
        if (!UpdateInProgressPresentation(true, true)) RebuildGraph(true);
    }

    private void ToggleLevel()
    {
        _levelEligibleOnly = !_levelEligibleOnly;
        if (!UpdateInProgressPresentation(true, true)) RebuildGraph(true);
    }

    private void ClearSelection()
    {
        if (_selectedQuestId is null && _focusQuestId is null) return;
        if (_mode == GlobalQuestGraphMode.InProgress && _focusQuestId is null)
        {
            _selectedQuestId = null;
            _graphView?.SetSelected(null);
            CloseQuestDetails();
            UpdateSelectionDetails();
            PersistCurrentState();
            return;
        }
        _selectedQuestId = null;
        _focusQuestId = null;
        CloseQuestDetails();
        RebuildGraph(true);
    }

    private void HandleBackgroundClick()
    {
        if (_focusQuestId is not null)
        {
            _focusQuestId = null;
            RebuildGraph(true);
            return;
        }
        ClearSelection();
    }

    private void ToggleFocus()
    {
        _focusQuestId = _focusQuestId is null ? _selectedQuestId : null;
        if (_focusQuestId is not null || _selectedQuestId is not null) RebuildGraph(true);
    }

    private void SetTrader(string? traderId)
    {
        _traderId = traderId;
        if (!UpdateInProgressPresentation(true, true)) RebuildGraph(true);
    }

    private void BuildGlobalChrome()
    {
        if (_graphView is null || _topology is null) return;
        _inProgressMapVisuals.Clear();
        var header = _graphView.Root.Find("Header") as RectTransform
            ?? throw new InvalidOperationException("QuestMap global header was not created.");

        AddHeaderButton(header, "InProgressTab", ClientLocale.Text("label.tasks"), 12, -4, 112,
            _mode == GlobalQuestGraphMode.InProgress, () => ShowMode(GlobalQuestGraphMode.InProgress),
            tooltip: _mode == GlobalQuestGraphMode.InProgress ? null : () => ClientLocale.Text("tooltip.openTasks"));
        AddHeaderButton(header, "QuestMapTab", ClientLocale.Text("label.questMap"), 130, -4, 100,
            _mode == GlobalQuestGraphMode.Full, () => ShowMode(GlobalQuestGraphMode.Full), enabled: !_raidInProgressOnly,
            tooltip: _mode == GlobalQuestGraphMode.Full ? null : () => ClientLocale.Text("tooltip.openQuestMap"));
        _notesButtonImage = AddRightHeaderButton(header, "Notes", ClientLocale.Text("label.notes"), 116, 72,
            _overlayMode == GlobalOverlayMode.Notes, () => ToggleNativeOverlay(true),
            () => ClientLocale.Text(_overlayMode == GlobalOverlayMode.Notes ? "tooltip.closeNotes" : "tooltip.openNotes"));
        _questItemsButtonImage = AddRightHeaderButton(header, "QuestItems", ClientLocale.Text("label.questItems"), 6, 104,
            _overlayMode == GlobalOverlayMode.QuestItems, () => ToggleNativeOverlay(false),
            () => QuestItemsTooltip(), allowTwoLines: true);
        BuildQuestItemsWarningBadge();
        if (CustomDetailsEnabled)
        {
            var detailSeparator = UnityUiFactory.CreateRect("DetailOverlaySeparator", header);
            detailSeparator.anchorMin = detailSeparator.anchorMax = detailSeparator.pivot = new Vector2(1, 1);
            detailSeparator.anchoredPosition = new Vector2(-198, -4);
            detailSeparator.sizeDelta = new Vector2(1, 32);
            detailSeparator.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
            _questDescriptionButtonImage = AddRightHeaderButton(
                header,
                "QuestDescription",
                ClientLocale.Text("label.questDescription"),
                208,
                144,
                _overlayMode == GlobalOverlayMode.QuestDescription,
                ToggleQuestDetails,
                () => QuestDescriptionTooltip());
            _questDescriptionButton = _questDescriptionButtonImage.GetComponent<Button>();
        }

        BuildSearchControl(header);
        if (_mode == GlobalQuestGraphMode.Full)
        {
            AddHeaderButton(header, "Future", ClientLocale.Text("label.future"), 12, -70, 68, _showAllFuture, ToggleFuture, 23,
                tooltip: () => ClientLocale.Text(_showAllFuture ? "tooltip.showNextFuture" : "tooltip.showAllFuture"));
            AddHeaderButton(header, "Finished", ClientLocale.Text("label.finished"), 86, -70, 42, !_hideFinished, ToggleFinished, 23,
                tooltip: () => ClientLocale.Text(_hideFinished ? "tooltip.showFinished" : "tooltip.hideFinished"));
            AddHeaderButton(header, "Level", ClientLocale.Format("common.levelFilter", ClientLocale.Arg("level", _overlay?.Level ?? 0)), 134, -70, 66, _levelEligibleOnly, ToggleLevel, 23,
                tooltip: () => ClientLocale.Format(_levelEligibleOnly ? "tooltip.showAllLevels" : "tooltip.limitLevel", ClientLocale.Arg("level", _overlay?.Level ?? 0)));
        }
        else
        {
            AddHeaderButton(header, "Repeatables", ClientLocale.Text(_includeAvailableRepeatables ? "label.repeatablesAll" : "label.repeatablesActive"), 12, -70, 86,
                _includeAvailableRepeatables, ToggleRepeatables, 23,
                tooltip: () => ClientLocale.Text(_includeAvailableRepeatables ? "tooltip.showActiveRepeatables" : "tooltip.showAllRepeatables"));
            AddHeaderButton(header, "HideCompletedTasks",
                ClientLocale.Text(_hideCompletedInProgressTasks ? "label.completedHidden" : "label.completedShown"), 104, -70, 100,
                _hideCompletedInProgressTasks, ToggleCompletedTasks, 23,
                tooltip: () => ClientLocale.Text(_hideCompletedInProgressTasks ? "tooltip.showCompletedTasks" : "tooltip.hideCompletedTasks"));
        }

        var separator = UnityUiFactory.CreateRect("FilterSeparator", header);
        separator.anchorMin = separator.anchorMax = separator.pivot = new Vector2(0, 1);
        separator.anchoredPosition = new Vector2(282, -43);
        separator.sizeDelta = new Vector2(1, 48);
        separator.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
        BuildTraderStrip(header);
        if (_mode == GlobalQuestGraphMode.InProgress) BuildInProgressMapStrip(header);
        UpdateOverlayButtons();
    }

    private void BuildInProgressMapStrip(RectTransform header)
    {
        if (_graphView is null) return;
        _inProgressMapAliases.Clear();
        var relevantNodes = BuildProjection(false).Nodes;
        var taskLocations = relevantNodes
            .SelectMany(NodeTaskLocations)
            .Where(map => !string.IsNullOrWhiteSpace(map.Id))
            .ToArray();
        var locations = relevantNodes
            .SelectMany(FilterMapChoices)
            .Where(map => !string.IsNullOrWhiteSpace(map.Id))
            .GroupBy(map => map.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var canonical = group.First();
                return new MapFilterChoice(
                    canonical.Id,
                    canonical.Name,
                    canonical.BannerImageUrl,
                    group.Select(map => map.Id)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray());
            })
            .OrderBy(location => location.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var noLocation = taskLocations.FirstOrDefault(map => map.Id.Equals(
            QuestObjectiveMapRules.NoLocationFilterId, StringComparison.OrdinalIgnoreCase));
        var any = taskLocations.FirstOrDefault(map => map.Id.Equals(
            QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase));
        var transition = taskLocations.FirstOrDefault(map => map.Id.Equals(
            QuestObjectiveMapRules.TransitionFilterId, StringComparison.OrdinalIgnoreCase));
        const float startX = 12;
        const float y = -103;
        const float width = 116;
        const float height = 32;
        const float gap = 6;
        var nextIndex = 0;
        AddMapResetChoice(nextIndex++);
        AddSpecialMapChoice(noLocation);
        AddSpecialMapChoice(any);
        AddSpecialMapChoice(transition);
        for (var index = 0; index < locations.Length; index++)
        {
            var location = locations[index];
            AddMapChoice(location.Name, location.Id, location.BannerImageUrl ?? FallbackLocationBannerUrl, location.Aliases, nextIndex + index,
                location.Aliases.Any(_inProgressLocationIds.Contains));
        }

        void AddSpecialMapChoice(QuestMapReference? map)
        {
            if (map is null) return;
            AddMapChoice(map.Name, map.Id, map.BannerImageUrl, [map.Id], nextIndex,
                _inProgressLocationIds.Contains(map.Id));
            nextIndex++;
        }

        void AddMapResetChoice(int index)
        {
            var root = UnityUiFactory.CreateRect("Map-Reset", header);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
            root.anchoredPosition = new Vector2(startX + index * (width + gap), y);
            root.sizeDelta = new Vector2(width, height);
            var button = UnityUiFactory.AddButton(root.gameObject, QuestGraphPalette.Control);
            var text = UnityUiFactory.AddText(root.gameObject, ClientLocale.Text("label.reset"), 11,
                TextAlignmentOptions.Center, Color.white);
            text.fontStyle = FontStyles.Bold;
            UnityUiFactory.FitSingleLine(text, 8f, 5f);
            button.onClick.AddListener(ResetLocationFilters);
            QuestMapNativeTooltips.Bind(root.gameObject, () => ClientLocale.Text(
                _raidInProgressOnly ? "tooltip.resetRaidLocations" : "tooltip.resetAllLocations"));
        }

        void AddMapChoice(string label, string? id, string? bannerUrl, string[] aliases, int index, bool selected)
        {
            var root = UnityUiFactory.CreateRect($"Map-{id}", header);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
            root.anchoredPosition = new Vector2(startX + index * (width + gap), y);
            root.sizeDelta = new Vector2(width, height);
            root.gameObject.AddComponent<RectMask2D>();
            var button = UnityUiFactory.AddButton(root.gameObject,
                selected ? QuestGraphPalette.ControlActive : new Color(0.08f, 0.09f, 0.09f, 1));
            var background = (Image)button.targetGraphic;
            Image? banner = null;

            if (!string.IsNullOrWhiteSpace(bannerUrl))
            {
                var imageRoot = UnityUiFactory.CreateRect("Banner", root);
                UnityUiFactory.Stretch(imageRoot, 0, 0, 0, 0);
                banner = imageRoot.gameObject.AddComponent<Image>();
                banner.color = new Color(1, 1, 1, selected ? 0.48f : 0.28f);
                banner.raycastTarget = false;
                var fitter = imageRoot.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                imageRoot.gameObject.SetActive(false);
                _graphView.AssetCache.Request(bannerUrl, sprite =>
                {
                    if (banner == null || sprite is null) return;
                    banner.sprite = sprite;
                    banner.type = Image.Type.Simple;
                    banner.preserveAspect = false;
                    fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
                    imageRoot.gameObject.SetActive(true);
                });
            }

            var shade = UnityUiFactory.CreateRect("Shade", root);
            UnityUiFactory.Stretch(shade, 0, 0, 0, 0);
            var shadeImage = shade.gameObject.AddComponent<Image>();
            shadeImage.color = selected
                ? new Color(0.10f, 0.08f, 0.02f, 0.32f)
                : new Color(0, 0, 0, 0.48f);
            var text = UnityUiFactory.AddText(root.gameObject, label, 11, TextAlignmentOptions.Center, Color.white);
            text.fontStyle = FontStyles.Bold;
            UnityUiFactory.FitSingleLine(text, 8f, 5f);
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = selected ? QuestGraphPalette.Selected : QuestGraphPalette.Border;
            outline.effectDistance = selected ? new Vector2(2, -2) : Vector2.one;
            if (id is not null)
            {
                _inProgressMapVisuals[id] = new MapFilterVisual(background, banner, shadeImage, outline);
                _inProgressMapAliases[id] = aliases;
            }
            button.onClick.AddListener(() =>
            {
                if (_workspace.InvertLocationFilterMouseButtons()) SelectOnlyInProgressLocation(id);
                else ToggleInProgressLocation(id);
            });
            QuestMapNativeTooltips.Bind(root.gameObject, () => InProgressLocationTooltip(id, label));

            var pointerClicks = root.gameObject.AddComponent<EventTrigger>();
            var pointerClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            pointerClick.callback.AddListener(eventData =>
            {
                if (eventData is PointerEventData { button: PointerEventData.InputButton.Right })
                {
                    if (_workspace.InvertLocationFilterMouseButtons()) ToggleInProgressLocation(id);
                    else SelectOnlyInProgressLocation(id);
                }
            });
            pointerClicks.triggers.Add(pointerClick);
        }
    }

    private static IEnumerable<QuestMapReference> FilterMapChoices(QuestGraphNode node)
    {
        foreach (var map in NodeTaskLocations(node))
        {
            if (map.Id.Equals(QuestObjectiveMapRules.NoLocationFilterId, StringComparison.OrdinalIgnoreCase)
                || map.Id.Equals(QuestObjectiveMapRules.AnyFilterId, StringComparison.OrdinalIgnoreCase)
                || map.Id.Equals(QuestObjectiveMapRules.TransitionFilterId, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return map;
        }
    }

    private static bool AddNodeLocationFilters(HashSet<string> target, QuestGraphNode node)
    {
        var changed = false;
        foreach (var map in NodeTaskLocations(node)) changed |= target.Add(map.Id);
        return changed;
    }

    private static IEnumerable<QuestMapReference> NodeTaskLocations(QuestGraphNode node)
    {
        var locations = node.Objectives
            .SelectMany(objective => objective.TaskLocations)
            .Where(map => !string.IsNullOrWhiteSpace(map.Id))
            .GroupBy(map => map.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (locations.Length > 0) return locations;
        return node.Objectives.Count == 0 ? [node.TaskLocation] : [];
    }

    private bool UpdateInProgressPresentation(
        bool projectionChanged,
        bool rebuildChrome,
        bool rebuildTraderStrip = false)
    {
        if (_disposed || _mode != GlobalQuestGraphMode.InProgress
            || _graphView is not InProgressQuestTableView table) return false;

        var stopwatch = Stopwatch.StartNew();
        var nextProjection = projectionChanged ? BuildProjection() : _projection;
        if (nextProjection is null) return false;
        _projection = nextProjection;
        if (_selectedQuestId is not null && !_projection.NodesById.ContainsKey(_selectedQuestId))
        {
            _selectedQuestId = null;
            _focusQuestId = null;
            CloseQuestDetails();
        }

        table.UpdatePresentation(_projection, _hideCompletedInProgressTasks, _inProgressLocationIds);
        if (rebuildChrome) RebuildInProgressChrome();
        else if (rebuildTraderStrip) RebuildInProgressTraderStrip();
        UpdateSelectionDetails();
        PersistCurrentState();
        stopwatch.Stop();
        QuestMapDebugLog.Info(Log,
            "QUESTMAP_M06_PRESENTATION_UPDATE " +
            $"projectionChanged={projectionChanged}; chromeChanged={rebuildChrome}; rows={_projection.Nodes.Count}; " +
            $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
        return true;
    }

    private void RebuildInProgressChrome()
    {
        if (_graphView?.Root.Find("Header") is not RectTransform header) return;
        _inProgressMapVisuals.Clear();
        foreach (Transform child in header)
        {
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }
        BuildGlobalChrome();
    }

    private void RebuildInProgressTraderStrip()
    {
        if (_graphView?.Root.Find("Header") is not RectTransform header) return;
        foreach (var traderRoot in header.Cast<Transform>()
                     .Where(child => child.name.StartsWith("Trader-", StringComparison.Ordinal))
                     .ToArray())
        {
            traderRoot.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(traderRoot.gameObject);
        }
        BuildTraderStrip(header);
    }

    private void ToggleInProgressLocation(string? locationId)
    {
        if (locationId is null) return;
        var aliases = LocationAliases(locationId);
        var selected = aliases.Any(_inProgressLocationIds.Contains);
        foreach (var alias in aliases)
        {
            if (selected) _inProgressLocationIds.Remove(alias);
            else _inProgressLocationIds.Add(alias);
        }
        RefreshInProgressMapVisuals();
        if (!UpdateInProgressPresentation(true, false, true)) RebuildGraph(true);
    }

    private void SelectOnlyInProgressLocation(string? locationId)
    {
        if (locationId is null) return;
        var aliases = LocationAliases(locationId);
        var selectedChoices = _inProgressMapAliases.Values.Count(choiceAliases =>
            choiceAliases.Any(_inProgressLocationIds.Contains));
        var restoreAll = selectedChoices == 1 && aliases.Any(_inProgressLocationIds.Contains);
        var next = restoreAll
            ? _inProgressMapAliases.Values.SelectMany(choiceAliases => choiceAliases)
            : aliases.AsEnumerable();
        var nextIds = next.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (nextIds.SetEquals(_inProgressLocationIds)) return;
        _inProgressLocationIds.Clear();
        _inProgressLocationIds.UnionWith(nextIds);
        RefreshInProgressMapVisuals();
        if (!UpdateInProgressPresentation(true, false, true)) RebuildGraph(true);
    }

    private string InProgressLocationTooltip(string? locationId, string label)
    {
        if (locationId is null) return string.Empty;
        var aliases = LocationAliases(locationId);
        var selected = aliases.Any(_inProgressLocationIds.Contains);
        var selectedChoices = _inProgressMapAliases.Values.Count(choiceAliases =>
            choiceAliases.Any(_inProgressLocationIds.Contains));
        var key = selected && selectedChoices == 1
            ? "tooltip.clearLocationRestoreAll"
            : selected
                ? "tooltip.clearLocation"
                : "tooltip.selectLocation";
        if (_workspace.InvertLocationFilterMouseButtons())
        {
            key = selected && selectedChoices == 1
                ? "tooltip.locationRestoreAllInverted"
                : selected
                    ? "tooltip.locationSelectedInverted"
                    : "tooltip.locationUnselectedInverted";
        }
        return ClientLocale.Format(key, ClientLocale.Arg("location", label));
    }

    private void RefreshInProgressMapVisuals()
    {
        foreach (var pair in _inProgressMapVisuals)
        {
            var selected = LocationAliases(pair.Key).Any(_inProgressLocationIds.Contains);
            pair.Value.Background.color = selected
                ? QuestGraphPalette.ControlActive
                : new Color(0.08f, 0.09f, 0.09f, 1);
            if (pair.Value.Banner is not null)
                pair.Value.Banner.color = new Color(1, 1, 1, selected ? 0.48f : 0.28f);
            pair.Value.Shade.color = selected
                ? new Color(0.10f, 0.08f, 0.02f, 0.32f)
                : new Color(0, 0, 0, 0.48f);
            pair.Value.Outline.effectColor = selected
                ? QuestGraphPalette.Selected
                : QuestGraphPalette.Border;
            pair.Value.Outline.effectDistance = selected ? new Vector2(2, -2) : Vector2.one;
        }
    }

    private string[] LocationAliases(string locationId) =>
        _inProgressMapAliases.GetValueOrDefault(locationId) ?? [locationId];

    private void ToggleInProgressSort(QuestTableSortColumn column)
    {
        var next = InProgressQuestTableSorter.Toggle(_inProgressSortCriteria, column);
        _inProgressSortCriteria.Clear();
        _inProgressSortCriteria.AddRange(next);
        if (!UpdateInProgressPresentation(false, false)) RebuildGraph(true);
    }

    private void ToggleCompletedTasks()
    {
        _hideCompletedInProgressTasks = !_hideCompletedInProgressTasks;
        if (!UpdateInProgressPresentation(false, true)) RebuildGraph(true);
    }

    private void ToggleRepeatables()
    {
        _includeAvailableRepeatables = !_includeAvailableRepeatables;
        if (!UpdateInProgressPresentation(true, true)) RebuildGraph(true);
    }

    private void ResetLocationFilters()
    {
        _inProgressLocationIds.Clear();
        if (_raidInProgressOnly && InRaidQuestContext.TryCapture(out var raidContext)) ApplyInRaidDefaults(raidContext);
        else
        {
            foreach (var node in BuildProjection(applyLocationFilter: false, membershipOnly: true).Nodes)
                AddNodeLocationFilters(_inProgressLocationIds, node);
            _inProgressLocationsInitialized = true;
        }
        RefreshInProgressMapVisuals();
        if (!UpdateInProgressPresentation(true, false, true)) RebuildGraph(true);
    }

    private void ToggleInProgressQuestExpansion(string questId)
    {
        if (!_expandedInProgressQuestIds.Add(questId)) _expandedInProgressQuestIds.Remove(questId);
        if (_graphView is InProgressQuestTableView table)
        {
            table.RefreshExpansion(questId);
            PersistCurrentState();
            return;
        }
        RebuildGraph(true);
    }

    private void BuildSearchControl(RectTransform header)
    {
        var root = UnityUiFactory.CreateRect("GraphSearch", header);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
        root.anchoredPosition = new Vector2(12, -42);
        root.sizeDelta = new Vector2(222, 24);
        root.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.115f, 0.11f, 1);
        var viewport = UnityUiFactory.CreateRect("Text Area", root);
        UnityUiFactory.Stretch(viewport, 8, 8, 2, 2);
        viewport.gameObject.AddComponent<RectMask2D>();
        var text = UnityUiFactory.AddText(viewport.gameObject, _search, 12, TextAlignmentOptions.MidlineLeft, Color.white);
        var placeholder = UnityUiFactory.AddText(viewport.gameObject, ClientLocale.Text("label.searchQuestTraderOrId"), 12,
            TextAlignmentOptions.MidlineLeft, new Color(0.55f, 0.57f, 0.58f, 1));
        UnityUiFactory.FitSingleLine(placeholder, 9f, 0f);
        var input = root.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = viewport;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.SetTextWithoutNotify(_search);
        input.onEndEdit.AddListener(value =>
        {
            if (_disposed) return;
            var next = value?.Trim() ?? string.Empty;
            if (string.Equals(_search, next, StringComparison.Ordinal)) return;
            _search = next;
            if (!UpdateInProgressPresentation(true, true)) RebuildGraph(true);
        });
        QuestMapNativeTooltips.Bind(root.gameObject, () => ClientLocale.Text("tooltip.search"));

        AddHeaderButton(header, "ClearSearch", "×", 240, -42, 30, !string.IsNullOrEmpty(_search), () =>
        {
            if (string.IsNullOrEmpty(_search)) return;
            _search = string.Empty;
            if (!UpdateInProgressPresentation(true, true)) RebuildGraph(true);
        }, 24, tooltip: () => ClientLocale.Text("tooltip.clearSearch"));
    }

    private void BuildTraderStrip(RectTransform header)
    {
        if (_graphView is null || _topology is null) return;
        var relevantTraderIds = BuildProjection(applyTraderFilter: false, membershipOnly: true).Nodes
            .Select(node => node.TraderId)
            .ToHashSet(StringComparer.Ordinal);
        var traders = _topology.Traders
            .Where(trader => relevantTraderIds.Contains(trader.Id))
            .OrderBy(trader => TraderRank(trader.Id))
            .ThenBy(trader => trader.Name, StringComparer.Ordinal)
            .ToArray();
        AddTraderChoice("/files/trader/avatar/unknown.png", ClientLocale.Text("common.all"), null, 0);
        for (var index = 0; index < traders.Length; index++)
            AddTraderChoice(traders[index].ImageUrl, traders[index].Name, traders[index].Id, index + 1);

        void AddTraderChoice(string? imageUrl, string label, string? id, int index)
        {
            var root = UnityUiFactory.CreateRect($"Trader-{id ?? "all"}", header);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
            root.anchoredPosition = new Vector2(296 + index * 55, -40);
            root.sizeDelta = new Vector2(51, 52);
            var selected = string.Equals(_traderId, id, StringComparison.Ordinal);
            var button = UnityUiFactory.AddButton(root.gameObject, selected ? QuestGraphPalette.ControlActive : Color.clear);

            var portraitRoot = UnityUiFactory.CreateRect("Portrait", root);
            portraitRoot.anchorMin = portraitRoot.anchorMax = portraitRoot.pivot = new Vector2(0.5f, 1);
            portraitRoot.anchoredPosition = new Vector2(0, -1);
            portraitRoot.sizeDelta = new Vector2(34, 34);
            portraitRoot.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.15f, 0.14f, 1);
            var fallback = UnityUiFactory.AddText(portraitRoot.gameObject, id is null ? string.Empty : UnityUiFactory.Initials(label), 9,
                TextAlignmentOptions.Center, QuestGraphPalette.Text);
            if (imageUrl is not null)
            {
                var imageRect = UnityUiFactory.CreateRect("Image", portraitRoot);
                UnityUiFactory.Stretch(imageRect, 1, 1, 1, 1);
                var image = imageRect.gameObject.AddComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.gameObject.SetActive(false);
                _graphView.AssetCache.Request(imageUrl, sprite =>
                {
                    if (image == null || sprite is null) return;
                    image.sprite = sprite;
                    image.gameObject.SetActive(true);
                    if (fallback != null) fallback.gameObject.SetActive(false);
                });
            }
            var labelRect = UnityUiFactory.CreateRect("Name", root);
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 0);
            labelRect.anchoredPosition = new Vector2(0, 1);
            labelRect.sizeDelta = new Vector2(51, 14);
            var traderLabel = UnityUiFactory.AddText(labelRect.gameObject, label, 8, TextAlignmentOptions.Center,
                selected ? QuestGraphPalette.Selected : QuestGraphPalette.MutedText);
            traderLabel.enableWordWrapping = false;
            button.onClick.AddListener(() => SetTrader(id));
            QuestMapNativeTooltips.Bind(root.gameObject, () => id is null
                ? ClientLocale.Text("tooltip.clearTrader")
                : ClientLocale.Format("tooltip.selectTrader", ClientLocale.Arg("trader", label)));
        }
    }

    private static Image AddHeaderButton(
        RectTransform parent, string name, string label, float x, float y, float width, bool active, Action action,
        float height = 32, bool enabled = true, Func<string>? tooltip = null)
    {
        return QuestWorkspaceChrome.AddButton(parent, name, label, x, y, width, active, action, height, enabled, tooltip);
    }

    private static Image AddRightHeaderButton(
        RectTransform parent, string name, string label, float right, float width, bool active, Action action,
        Func<string>? tooltip = null, bool allowTwoLines = false)
    {
        return QuestWorkspaceChrome.AddRightButton(parent, name, label, right, width, active, action,
            tooltip: tooltip, allowTwoLines: allowTwoLines);
    }

    private static int TraderRank(string traderId)
    {
        return QuestTraderOrder.Rank(traderId);
    }

    private Toggle ResolveSingleToggle(FieldInfo field, string description)
    {
        var owner = field.GetValue(_screen) as Component
            ?? throw new InvalidOperationException($"TasksScreen.{field.Name} was not a Component.");
        var toggles = owner.GetComponentsInChildren<Toggle>(true);
        var active = toggles.Where(toggle => toggle.gameObject.activeInHierarchy).ToArray();
        if (active.Length == 1) return active[0];
        var activeSelf = toggles.Where(toggle => toggle.gameObject.activeSelf).ToArray();
        if (activeSelf.Length == 1) return activeSelf[0];
        if (toggles.Length != 1)
        {
            var candidates = string.Join(", ", toggles.Select(toggle =>
                $"{GetPath(toggle.transform)}(activeSelf={toggle.gameObject.activeSelf},activeHierarchy={toggle.gameObject.activeInHierarchy})"));
            throw new InvalidOperationException(
                $"TasksScreen {description} spawner did not have one settled toggle; candidates={toggles.Length}: {candidates}");
        }
        return toggles[0];
    }

    private void HideNativeControl(FieldInfo field)
    {
        var owner = field.GetValue(_screen) as Component
            ?? throw new InvalidOperationException($"TasksScreen.{field.Name} was not a Component.");
        var branch = owner.gameObject;
        _nativeControlStates.TryAdd(branch, branch.activeSelf);
        branch.SetActive(false);
    }

    private void PersistCurrentState()
    {
        if (_graphView is null || string.IsNullOrWhiteSpace(_viewStateScope)) return;
        QuestGraphViewStateStore.Save(_viewStateScope!, _graphView.CaptureViewportState(), _selectedQuestId);
        if (_overlay is not null && !_raidInProgressOnly)
        {
            GlobalTasksGraphSettingsStore.Save(
                _overlay.ProfileId,
                new GlobalTasksGraphSettings(
                    _mode,
                    _showAllFuture,
                    _hideFinished,
                    _levelEligibleOnly,
                    _traderId,
                    _search,
                    _focusQuestId,
                    QuestRouteFilter.None,
                    [],
                    false,
                    _inProgressSortCriteria,
                    _hideCompletedInProgressTasks,
                    _includeAvailableRepeatables));
        }
    }

    private void LoadSettings()
    {
        if (_overlay is null || !GlobalTasksGraphSettingsStore.TryLoad(_overlay.ProfileId, out var settings)) return;
        _mode = settings.Mode;
        _showAllFuture = settings.ShowAllFuture;
        _hideFinished = settings.HideFinished;
        _levelEligibleOnly = settings.LevelEligibleOnly;
        _traderId = settings.TraderId;
        _search = settings.Search;
        _focusQuestId = settings.FocusQuestId;
        // Location filters are deliberately session/raid-local. Persisting them
        // made newly accepted quests disappear after a restart or raid boundary.
        _inProgressLocationIds.Clear();
        _inProgressLocationsInitialized = false;
        _inProgressSortCriteria.Clear();
        _inProgressSortCriteria.AddRange(settings.InProgressSortCriteria);
        _hideCompletedInProgressTasks = settings.HideCompletedInProgressTasks;
        _includeAvailableRepeatables = settings.IncludeAvailableRepeatables;
    }

    private void BuildSelectionSummary()
    {
        if (_graphView is null || CustomDetailsEnabled) return;
        _selectionSummary = UnityUiFactory.CreateRect("SelectionSummary", _graphView.Root);
        _selectionSummary.anchorMin = _selectionSummary.anchorMax = _selectionSummary.pivot = new Vector2(1, 0);
        _selectionSummary.anchoredPosition = new Vector2(-12, 12);
        _selectionSummary.sizeDelta = new Vector2(690, 70);
        _selectionSummary.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.075f, 0.075f, 0.96f);
        _selectionSummaryText = UnityUiFactory.AddText(
            _selectionSummary.gameObject,
            string.Empty,
            14,
            TextAlignmentOptions.MidlineLeft,
            new Color(0.88f, 0.88f, 0.82f, 1));
        _selectionSummaryText.margin = new Vector4(14, 6, 14, 6);
        _selectionSummary.gameObject.SetActive(false);
    }

    private void ToggleNativeOverlay(bool notes)
    {
        if (_disposed || !_mounted || _notesPart is null || _questItemsPart is null
            || _notesToggle is null || _questItemsToggle is null) return;
        var requested = notes ? GlobalOverlayMode.Notes : GlobalOverlayMode.QuestItems;
        ApplyOverlayMode(_overlayMode == requested ? GlobalOverlayMode.None : requested, true);
        QuestMapDebugLog.Info(Log,
            $"QUESTMAP_M06_OVERLAY requested={(notes ? "notes" : "quest-items")}; mode={_overlayMode}; " +
            $"notesVisible={_notesPart.gameObject.activeSelf}; questItemsVisible={_questItemsPart.gameObject.activeSelf}");
    }

    private void ToggleQuestDetails()
    {
        if (!CustomDetailsEnabled || _selectedQuestId is null) return;
        ApplyOverlayMode(
            _overlayMode == GlobalOverlayMode.QuestDescription
                ? GlobalOverlayMode.None
                : GlobalOverlayMode.QuestDescription,
            true);
    }

    private void ShowSelectedQuestDetails()
    {
        if (!CustomDetailsEnabled || _selectedQuestId is null || _graphView is null || _topology is null || _overlay is null)
            return;
        if (_detailPane is null)
        {
            _detailPane = QuestDetailsPane.Create(
                _graphView.Root.parent as RectTransform
                    ?? throw new InvalidOperationException("QuestMap global root has no overlay parent."),
                _graphView.Root.Find("Viewport") as RectTransform
                    ?? throw new InvalidOperationException("QuestMap global viewport was not created."),
                _workspace,
                _assetCache,
                _requestQuestRefresh);
        }
        _detailPane.Show(_topology, _overlay, _selectedQuestId);
        ApplyOverlayMode(GlobalOverlayMode.QuestDescription, true);
    }

    private void CloseQuestDetails()
    {
        if (_overlayMode == GlobalOverlayMode.QuestDescription) _overlayMode = GlobalOverlayMode.None;
        _detailPane?.Hide();
        UpdateOverlayButtons();
    }

    private void ClearNativeOverlays()
    {
        _overlayMode = GlobalOverlayMode.None;
        _notesToggle?.SetIsOnWithoutNotify(false);
        _questItemsToggle?.SetIsOnWithoutNotify(false);
        if (_notesPart is not null) _notesPart.gameObject.SetActive(false);
        if (_questItemsPart is not null) _questItemsPart.gameObject.SetActive(false);
        if (_notesBackdrop is not null) _notesBackdrop.gameObject.SetActive(false);
        if (_questItemsBackdrop is not null) _questItemsBackdrop.gameObject.SetActive(false);
        _detailPane?.Hide();
        UpdateOverlayButtons();
    }

    private void ApplyOverlayMode(GlobalOverlayMode mode, bool invokeNative)
    {
        if (_notesPart is null || _questItemsPart is null || _notesToggle is null || _questItemsToggle is null) return;
        if (mode == GlobalOverlayMode.QuestDescription && _selectedQuestId is null) mode = GlobalOverlayMode.None;
        if (invokeNative && _overlayMode == GlobalOverlayMode.Notes && mode != GlobalOverlayMode.Notes)
            _notesToggle.isOn = false;
        if (invokeNative && _overlayMode == GlobalOverlayMode.QuestItems && mode != GlobalOverlayMode.QuestItems)
            _questItemsToggle.isOn = false;
        _notesToggle.SetIsOnWithoutNotify(false);
        _questItemsToggle.SetIsOnWithoutNotify(false);

        if (invokeNative && mode == GlobalOverlayMode.Notes) _notesToggle.isOn = true;
        if (invokeNative && mode == GlobalOverlayMode.QuestItems) _questItemsToggle.isOn = true;

        // Native toggle-group callbacks may select the opposite branch while a
        // toggle is turned off. The custom state is authoritative, so settle
        // both native toggles and roots explicitly after invoking EFT's opener.
        _notesToggle.SetIsOnWithoutNotify(mode == GlobalOverlayMode.Notes);
        _questItemsToggle.SetIsOnWithoutNotify(mode == GlobalOverlayMode.QuestItems);
        _notesPart.gameObject.SetActive(mode == GlobalOverlayMode.Notes);
        _questItemsPart.gameObject.SetActive(mode == GlobalOverlayMode.QuestItems);
        if (_notesBackdrop is not null) _notesBackdrop.gameObject.SetActive(mode == GlobalOverlayMode.Notes);
        if (_questItemsBackdrop is not null) _questItemsBackdrop.gameObject.SetActive(mode == GlobalOverlayMode.QuestItems);
        if (mode != GlobalOverlayMode.QuestDescription) _detailPane?.Hide();
        _overlayMode = mode;

        if (_graphView?.Root.parent is Transform parent)
        {
            _graphView.Root.SetAsLastSibling();
            if (mode == GlobalOverlayMode.Notes)
            {
                _notesBackdrop?.SetAsLastSibling();
                DirectChildUnder(parent, _notesPart).SetAsLastSibling();
            }
            if (mode == GlobalOverlayMode.QuestItems)
            {
                ConstrainQuestItemsScrolls();
                _questItemsBackdrop?.SetAsLastSibling();
                DirectChildUnder(parent, _questItemsPart).SetAsLastSibling();
            }
        }
        if (mode == GlobalOverlayMode.QuestDescription) _detailPane?.ShowRoot();
        UpdateOverlayButtons();
    }

    private void UpdateOverlayButtons()
    {
        if (_notesButtonImage is not null)
            _notesButtonImage.color = _overlayMode == GlobalOverlayMode.Notes ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control;
        UpdateQuestItemsButtonState();
        if (_questDescriptionButtonImage is not null)
            _questDescriptionButtonImage.color = _overlayMode == GlobalOverlayMode.QuestDescription
                ? QuestGraphPalette.ControlActive
                : QuestGraphPalette.Control;
        if (_questDescriptionButton is not null) _questDescriptionButton.interactable = _selectedQuestId is not null;
    }

    private void BuildQuestItemsWarningBadge()
    {
        if (_questItemsButtonImage is null) return;
        var buttonRect = _questItemsButtonImage.rectTransform;
        _questItemsButtonLabel = buttonRect.GetComponentInChildren<TMP_Text>(true);
        if (_questItemsButtonLabel is not null)
        {
            _questItemsButtonLabel.margin = new Vector4(4, 0, 34, 0);
            _questItemsButtonLabel.fontSize = 11;
        }

        var badge = UnityUiFactory.CreateRect("CarriedQuestItemsWarning", buttonRect);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1, 0.5f);
        badge.anchoredPosition = new Vector2(-4, 0);
        badge.sizeDelta = new Vector2(34, 22);
        _questItemsWarningBadge = badge.gameObject;

        var countRect = UnityUiFactory.CreateRect("Count", badge);
        countRect.anchorMin = countRect.anchorMax = countRect.pivot = new Vector2(0, 0.5f);
        countRect.anchoredPosition = new Vector2(0, 0);
        countRect.sizeDelta = new Vector2(18, 20);
        _questItemsWarningCount = UnityUiFactory.AddText(
            countRect.gameObject,
            string.Empty,
            10,
            TextAlignmentOptions.Center,
            new Color(1f, 0.78f, 0.78f, 1f));
        _questItemsWarningCount.fontStyle = FontStyles.Bold;

        var triangleRect = UnityUiFactory.CreateRect("Triangle", badge);
        triangleRect.anchorMin = triangleRect.anchorMax = triangleRect.pivot = new Vector2(1, 0.5f);
        triangleRect.anchoredPosition = Vector2.zero;
        triangleRect.sizeDelta = new Vector2(16, 15);
        var triangle = triangleRect.gameObject.AddComponent<QuestItemWarningTriangleGraphic>();
        triangle.color = QuestGraphPalette.Failed;
        triangle.raycastTarget = false;
        var exclamation = UnityUiFactory.AddText(
            triangleRect.gameObject,
            "!",
            9,
            TextAlignmentOptions.Center,
            Color.white);
        exclamation.fontStyle = FontStyles.Bold;
        exclamation.raycastTarget = false;

        UpdateQuestItemsButtonState();
    }

    private void UpdateQuestItemsButtonState()
    {
        if (_questItemsButtonImage is null) return;
        var count = GetCarriedQuestItemCount();
        var warning = count > 0;
        if (_questItemsWarningBadge is not null) _questItemsWarningBadge.SetActive(warning);
        if (_questItemsWarningCount is not null) _questItemsWarningCount.text = count.ToString();
        _questItemsButtonImage.color = warning
            ? (_overlayMode == GlobalOverlayMode.QuestItems
                ? QuestGraphPalette.WarningControlActive
                : QuestGraphPalette.WarningControl)
            : (_overlayMode == GlobalOverlayMode.QuestItems
                ? QuestGraphPalette.ControlActive
                : QuestGraphPalette.Control);
    }

    private int GetCarriedQuestItemCount()
    {
        return QuestController.Profile.Inventory.QuestRaidItems?.Grid?.Items?.Count() ?? 0;
    }

    private void OnInventoryProfileUpdate()
    {
        if (!_disposed && _visible) UpdateQuestItemsButtonState();
    }

    private bool RebindRuntimeContext(
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController)
    {
        var inventoryChanged = !ReferenceEquals(InventoryController, inventoryController);
        if (inventoryChanged) UnsubscribeInventoryUpdates();
        var changed = _workspace.Rebind(session, inventoryController, questController);
        if (changed)
        {
            _detailPane?.AbandonNativeRuntime();
            _detailPane = null;
            if (_graphView is InProgressQuestTableView table) table.ReleaseNativeRuntimeBindings(true);
        }
        SubscribeInventoryUpdates();
        return changed;
    }

    private void SubscribeInventoryUpdates()
    {
        if (_inventoryUpdatesSubscribed) return;
        InventoryController.OnProfileUpdate += OnInventoryProfileUpdate;
        _inventoryUpdatesSubscribed = true;
    }

    private void UnsubscribeInventoryUpdates()
    {
        if (!_inventoryUpdatesSubscribed) return;
        InventoryController.OnProfileUpdate -= OnInventoryProfileUpdate;
        _inventoryUpdatesSubscribed = false;
    }

    private void HideNativeTasksWorkspace()
    {
        _tasksPanel?.Close();
        if (_tasksPanel is not null) _tasksPanel.gameObject.SetActive(false);
        _tasksDescription?.SetActive(false);
        _screenTooltip?.Close();
        _tasksTooltip?.Close();
    }

    private RectTransform CreateFullSurfaceMount()
    {
        if (_tasksPanel is null || _notesPart is null || _questItemsPart is null)
            throw new InvalidOperationException("Global Tasks surface roots are not ready.");
        var tasksPart = _tasksPanel.transform.parent as RectTransform
            ?? throw new InvalidOperationException("TasksPanel parent has no RectTransform.");
        var nativeSelectors = new[] { DefaultToggleField, DailyToggleField, NotesToggleField, QuestItemsToggleField }
            .Select(field => (field.GetValue(_screen) as Component)?.transform as RectTransform)
            .Where(rect => rect is not null)
            .Cast<RectTransform>()
            .ToArray();
        var surfaces = new[] { tasksPart, _notesPart, _questItemsPart }.Concat(nativeSelectors).ToArray();
        var commonParent = FindCommonRectAncestor(surfaces)
            ?? throw new InvalidOperationException("Tasks, Notes, Quest Items, and their selectors do not share a RectTransform ancestor.");
        var bounds = LocalUnion(commonParent, surfaces);
        var contentTop = commonParent.rect.yMax - CharacterNavigationInset;
        if (contentTop > bounds.yMax)
        {
            bounds.yMax = contentTop;
            _surfaceTopSource = "character-navigation-boundary";
        }
        else
        {
            _surfaceTopSource = "native-union";
        }
        bounds = Rect.MinMaxRect(commonParent.rect.xMin, bounds.yMin, commonParent.rect.xMax, bounds.yMax);
        _surfaceTopInset = commonParent.rect.yMax - bounds.yMax;
        if (_surfaceTopInset < MinimumCharacterNavigationInset)
        {
            throw new InvalidOperationException(
                $"Resolved global Tasks surface would cover the Character tab bar: topInset={_surfaceTopInset:0.##}; parentHeight={commonParent.rect.height:0.##}.");
        }
        if (bounds.width < tasksPart.rect.width || bounds.height < tasksPart.rect.height)
        {
            throw new InvalidOperationException(
                $"Resolved global Tasks surface {bounds.width:0.##}x{bounds.height:0.##} is smaller than TasksPart {tasksPart.rect.width:0.##}x{tasksPart.rect.height:0.##}.");
        }

        var mount = UnityUiFactory.CreateRect("QuestMapGlobalSurfaceMount", commonParent);
        mount.anchorMin = Vector2.zero;
        mount.anchorMax = Vector2.zero;
        mount.pivot = Vector2.zero;
        mount.anchoredPosition = new Vector2(bounds.xMin - commonParent.rect.xMin, bounds.yMin - commonParent.rect.yMin);
        mount.sizeDelta = bounds.size;
        mount.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        mount.SetAsLastSibling();
        return mount;
    }

    private void PlaceGraphBehindNativeSidePanels()
    {
        if (_graphView is null || _notesPart is null || _questItemsPart is null) return;
        if (_graphView.Root.parent is not RectTransform commonParent) return;
        var notesBranch = DirectChildUnder(commonParent, _notesPart);
        var questItemsBranch = DirectChildUnder(commonParent, _questItemsPart);
        _graphView.Root.SetAsLastSibling();
        if (_overlayMode == GlobalOverlayMode.Notes && _notesPart.gameObject.activeSelf)
        {
            _notesBackdrop?.SetAsLastSibling();
            notesBranch.SetAsLastSibling();
        }
        if (_overlayMode == GlobalOverlayMode.QuestItems && _questItemsPart.gameObject.activeSelf)
        {
            _questItemsBackdrop?.SetAsLastSibling();
            questItemsBranch.SetAsLastSibling();
        }
        if (_overlayMode == GlobalOverlayMode.QuestDescription) _detailPane?.ShowRoot();
    }

    private void ConfigureNativeSidePanels()
    {
        if (_graphView is null || _notesPart is null || _questItemsPart is null || _nativeSearch is null) return;
        var viewport = _graphView.Root.Find("Viewport") as RectTransform
            ?? throw new InvalidOperationException("QuestMap global viewport was not created.");

        _notesPartLayout ??= RectTransformSnapshot.Capture(_notesPart);
        _questItemsPartLayout ??= RectTransformSnapshot.Capture(_questItemsPart);
        _nativeSearchLayout ??= RectTransformSnapshot.Capture(_nativeSearch);

        AlignNativePanel(_notesPart, viewport, _notesPartLayout.Value.ParentSize.x);
        AlignNativePanel(_questItemsPart, viewport, _questItemsPartLayout.Value.ParentSize.x);
        AlignNativeSearch(_nativeSearch, _notesPart);
        var overlayParent = _graphView.Root.parent as RectTransform
            ?? throw new InvalidOperationException("QuestMap global root has no RectTransform parent.");
        _notesBackdrop = EnsureNativeBackdrop(overlayParent, _notesPart, _notesBackdrop, "QuestMapNotesBackdrop");
        _questItemsBackdrop = EnsureNativeBackdrop(overlayParent, _questItemsPart, _questItemsBackdrop, "QuestMapQuestItemsBackdrop");
        _notesBackdrop.gameObject.SetActive(_overlayMode == GlobalOverlayMode.Notes);
        _questItemsBackdrop.gameObject.SetActive(_overlayMode == GlobalOverlayMode.QuestItems);
        ConstrainQuestItemsScrolls();

        QuestMapDebugLog.Info(Log,
            "QUESTMAP_M06_NATIVE_BOUNDS " +
            $"canvas={FormatSize(viewport.rect.size)}; notes={FormatSize(_notesPart.rect.size)}; " +
            $"questItems={FormatSize(_questItemsPart.rect.size)}; search={FormatSize(_nativeSearch.rect.size)}; rightAligned=True; backdrop=True");
    }

    private static void AlignNativePanel(RectTransform panel, RectTransform viewport, float originalWidth)
    {
        if (panel.parent is not RectTransform parent)
            throw new InvalidOperationException($"Native panel {panel.name} has no RectTransform parent.");
        var bounds = BoundsInParent(viewport, parent);
        var width = Mathf.Min(Mathf.Max(1f, originalWidth), bounds.width);
        ApplyLocalRect(panel, parent, Rect.MinMaxRect(bounds.xMax - width, bounds.yMin, bounds.xMax, bounds.yMax));
    }

    private static void AlignNativeSearch(RectTransform search, RectTransform notesPanel)
    {
        if (search.parent is not RectTransform parent)
            throw new InvalidOperationException("Native Notes search has no RectTransform parent.");
        var bounds = BoundsInParent(notesPanel, parent);
        var current = BoundsInParent(search, parent);
        const float inset = 8f;
        var width = Mathf.Min(current.width, Mathf.Max(1f, bounds.width - inset * 2));
        var height = Mathf.Min(current.height, Mathf.Max(1f, bounds.height - inset * 2));
        ApplyLocalRect(search, parent, Rect.MinMaxRect(
            bounds.xMin + inset,
            bounds.yMax - inset - height,
            bounds.xMin + inset + width,
            bounds.yMax - inset));
    }

    private static RectTransform EnsureNativeBackdrop(
        RectTransform overlayParent,
        RectTransform panel,
        RectTransform? existing,
        string name)
    {
        var backdrop = existing ?? UnityUiFactory.CreateRect(name, overlayParent);
        ApplyLocalRect(backdrop, overlayParent, BoundsInParent(panel, overlayParent));
        var layout = backdrop.GetComponent<LayoutElement>() ?? backdrop.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        if (existing is not null) return backdrop;
        var gradient = backdrop.gameObject.AddComponent<QuestMapPanelGradient>();
        gradient.raycastTarget = false;
        gradient.SetColors(new Color(0.025f, 0.029f, 0.032f, 1f), new Color(0.075f, 0.084f, 0.086f, 1f));
        return backdrop;
    }

    private void ConstrainQuestItemsScrolls()
    {
        if (_questItemsPart is null) return;
        Canvas.ForceUpdateCanvases();
        foreach (var scroll in _questItemsPart.GetComponentsInChildren<ScrollRect>(true))
        {
            if (scroll.transform is not RectTransform rect || rect.parent is not RectTransform parent) continue;
            var panelBounds = BoundsInParent(_questItemsPart, parent);
            var current = BoundsInParent(rect, parent);
            const float inset = 8f;
            var minimumBottom = panelBounds.yMin + inset;
            if (current.yMin >= minimumBottom || current.yMax <= minimumBottom) continue;
            if (!_questItemsScrollLayouts.ContainsKey(rect))
                _questItemsScrollLayouts.Add(rect, RectTransformSnapshot.Capture(rect));
            ApplyLocalRect(rect, parent, Rect.MinMaxRect(current.xMin, minimumBottom, current.xMax, current.yMax));
        }
        if (_questItemsPart.GetComponent<RectMask2D>() is null && _ownedQuestItemsMask is null)
            _ownedQuestItemsMask = _questItemsPart.gameObject.AddComponent<RectMask2D>();
        Canvas.ForceUpdateCanvases();
    }

    private void RestoreNativeSidePanelLayout()
    {
        if (_notesBackdrop is not null) UnityEngine.Object.Destroy(_notesBackdrop.gameObject);
        if (_questItemsBackdrop is not null) UnityEngine.Object.Destroy(_questItemsBackdrop.gameObject);
        _notesBackdrop = null;
        _questItemsBackdrop = null;
        foreach (var pair in _questItemsScrollLayouts) pair.Value.Restore(pair.Key);
        _questItemsScrollLayouts.Clear();
        if (_ownedQuestItemsMask is not null) UnityEngine.Object.Destroy(_ownedQuestItemsMask);
        _ownedQuestItemsMask = null;
        if (_notesPart is not null && _notesPartLayout.HasValue) _notesPartLayout.Value.Restore(_notesPart);
        if (_questItemsPart is not null && _questItemsPartLayout.HasValue) _questItemsPartLayout.Value.Restore(_questItemsPart);
        if (_nativeSearch is not null && _nativeSearchLayout.HasValue) _nativeSearchLayout.Value.Restore(_nativeSearch);
    }

    private static Rect BoundsInParent(RectTransform source, RectTransform parent)
    {
        var corners = new Vector3[4];
        source.GetWorldCorners(corners);
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            var local = parent.InverseTransformPoint(corner);
            minimum = Vector2.Min(minimum, local);
            maximum = Vector2.Max(maximum, local);
        }
        return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
    }

    private static void ApplyLocalRect(RectTransform target, RectTransform parent, Rect bounds)
    {
        target.anchorMin = Vector2.zero;
        target.anchorMax = Vector2.zero;
        target.pivot = Vector2.zero;
        target.anchoredPosition = new Vector2(bounds.xMin - parent.rect.xMin, bounds.yMin - parent.rect.yMin);
        target.sizeDelta = bounds.size;
        target.localScale = Vector3.one;
        target.localRotation = Quaternion.identity;
    }

    private static Transform DirectChildUnder(Transform ancestor, Transform descendant)
    {
        var current = descendant;
        while (current.parent is not null && !ReferenceEquals(current.parent, ancestor))
        {
            current = current.parent;
        }
        if (!ReferenceEquals(current.parent, ancestor))
        {
            throw new InvalidOperationException($"{descendant.name} is not below the resolved global Tasks surface parent {ancestor.name}.");
        }
        return current;
    }

    private static RectTransform? FindCommonRectAncestor(params RectTransform[] transforms)
    {
        for (Transform? candidate = transforms[0]; candidate is not null; candidate = candidate.parent)
        {
            if (candidate is not RectTransform rect) continue;
            if (transforms.All(transform => transform == rect || transform.IsChildOf(rect))) return rect;
        }
        return null;
    }

    private static Rect LocalUnion(RectTransform commonParent, params RectTransform[] transforms)
    {
        var corners = new Vector3[4];
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var transform in transforms)
        {
            transform.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var local = commonParent.InverseTransformPoint(corner);
                minimum = Vector2.Min(minimum, local);
                maximum = Vector2.Max(maximum, local);
            }
        }
        return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
    }

    private void UpdateSelectionDetails()
    {
        UpdateSelectionSummary();
        UpdateOverlayButtons();
        if (!CustomDetailsEnabled || _detailPane is null || _topology is null || _overlay is null || _selectedQuestId is null)
        {
            if (_selectedQuestId is null) CloseQuestDetails();
            return;
        }
        _detailPane.Show(_topology, _overlay, _selectedQuestId);
    }

    private void UpdateSelectionSummary()
    {
        if (_selectionSummary is null || _selectionSummaryText is null || _topology is null || _overlay is null || _selectedQuestId is null
            || !_topology.NodesById.TryGetValue(_selectedQuestId, out var node))
        {
            if (_selectionSummary is not null) _selectionSummary.gameObject.SetActive(false);
            return;
        }

        QuestLiveState? liveState = null;
        _overlay?.QuestsById.TryGetValue(node.Id, out liveState);
        var displayState = QuestGraphRules.ClassifyProfileDisplayState(_topology, node, _overlay!);
        var status = QuestGraphCardNodeView.StateLabel(displayState);
        var progressPercent = displayState == QuestMapDisplayStateKind.InProgress && liveState is not null
            ? QuestGraphRules.CalculateObjectiveProgressPercent(liveState.Objectives)
            : null;
        if (progressPercent.HasValue)
            status += ClientLocale.Format("common.inlineDetail", ClientLocale.Arg("detail",
                ClientLocale.Format("common.approximatePercent", ClientLocale.Arg("percent", progressPercent.Value))));
        var completed = liveState?.Objectives.Count(objective => objective.Complete) ?? 0;
        var objectiveSummary = node.Objectives.Count == 0
            ? ClientLocale.Text("common.noObjectivesShort")
            : ClientLocale.Format("common.objectivesCount", ClientLocale.Arg("complete", completed),
                ClientLocale.Arg("total", node.Objectives.Count));
        var routes = new List<string>();
        if (_topology.CollectorPathQuestIds.Contains(node.Id)) routes.Add(ClientLocale.Text("common.collector"));
        if (_topology.LightkeeperPathQuestIds.Contains(node.Id)) routes.Add(ClientLocale.Text("common.lightkeeper"));
        var routeText = routes.Count == 0
            ? string.Empty
            : ClientLocale.Format("common.inlineDetailWide", ClientLocale.Arg("detail",
                ClientLocale.Format("common.route", ClientLocale.Arg("routes",
                    string.Join(ClientLocale.Text("common.routeSeparator"), routes)))));
        _selectionSummaryText.text = ClientLocale.Format("selection.summary",
            ClientLocale.Arg("quest", node.Name.ToUpperInvariant()),
            ClientLocale.Arg("trader", node.TraderName),
            ClientLocale.Arg("status", status),
            ClientLocale.Arg("route", routeText),
            ClientLocale.Arg("objectives", objectiveSummary));
        _selectionSummary.gameObject.SetActive(true);
        _selectionSummary.SetAsLastSibling();
    }

    private static FieldInfo RequiredField(string name) => AccessTools.Field(typeof(TasksScreen), name)
        ?? throw new MissingFieldException(typeof(TasksScreen).FullName, name);

    private static FieldInfo RequiredTasksPanelField(string name) => AccessTools.Field(typeof(TasksPanel), name)
        ?? throw new MissingFieldException(typeof(TasksPanel).FullName, name);

    private static string FormatSize(Vector2 value) => $"{value.x:0.##}x{value.y:0.##}";

    private string SelectedQuestName() =>
        _selectedQuestId is not null && _topology?.NodesById.TryGetValue(_selectedQuestId, out var node) == true
            ? node.Name
            : ClientLocale.Text("label.questDescription");

    private string QuestDescriptionTooltip()
    {
        if (_selectedQuestId is null) return ClientLocale.Text("tooltip.noQuestSelected");
        return _overlayMode == GlobalOverlayMode.QuestDescription
            ? ClientLocale.Text("tooltip.closeDetails")
            : ClientLocale.Format("tooltip.openDetails", ClientLocale.Arg("quest", SelectedQuestName()));
    }

    private string QuestItemsTooltip()
    {
        var count = GetCarriedQuestItemCount();
        if (count > 0)
            return ClientLocale.Format(
                _overlayMode == GlobalOverlayMode.QuestItems ? "tooltip.closeQuestItemsCount" : "tooltip.openQuestItemsCount",
                ClientLocale.Arg("count", count));
        return ClientLocale.Text(_overlayMode == GlobalOverlayMode.QuestItems
            ? "tooltip.closeQuestItems"
            : "tooltip.openQuestItems");
    }

    private static string GetPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current is not null; current = current.parent) names.Push(current.name);
        return string.Join("/", names.ToArray());
    }

    private enum GlobalOverlayMode
    {
        None,
        QuestDescription,
        Notes,
        QuestItems,
    }

    private sealed class MapFilterChoice
    {
        public MapFilterChoice(string id, string name, string? bannerImageUrl, string[] aliases)
        {
            Id = id;
            Name = name;
            BannerImageUrl = bannerImageUrl;
            Aliases = aliases;
        }

        public string Id { get; }
        public string Name { get; }
        public string? BannerImageUrl { get; }
        public string[] Aliases { get; }
    }

    private sealed class MapFilterVisual
    {
        public MapFilterVisual(Image background, Image? banner, Image shade, Outline outline)
        {
            Background = background;
            Banner = banner;
            Shade = shade;
            Outline = outline;
        }

        public Image Background { get; }
        public Image? Banner { get; }
        public Image Shade { get; }
        public Outline Outline { get; }
    }

    private readonly struct RectTransformSnapshot
    {
        private readonly Vector2 _anchorMin;
        private readonly Vector2 _anchorMax;
        private readonly Vector2 _pivot;
        private readonly Vector2 _anchoredPosition;
        private readonly Vector2 _sizeDelta;
        private readonly Vector3 _localScale;
        private readonly Quaternion _localRotation;

        private RectTransformSnapshot(
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Vector3 localScale,
            Quaternion localRotation,
            Vector2 parentSize)
        {
            _anchorMin = anchorMin;
            _anchorMax = anchorMax;
            _pivot = pivot;
            _anchoredPosition = anchoredPosition;
            _sizeDelta = sizeDelta;
            _localScale = localScale;
            _localRotation = localRotation;
            ParentSize = parentSize;
        }

        public Vector2 ParentSize { get; }

        public static RectTransformSnapshot Capture(RectTransform rect)
        {
            var parentSize = rect.parent is RectTransform parent
                ? BoundsInParent(rect, parent).size
                : rect.rect.size;
            return new RectTransformSnapshot(
                rect.anchorMin,
                rect.anchorMax,
                rect.pivot,
                rect.anchoredPosition,
                rect.sizeDelta,
                rect.localScale,
                rect.localRotation,
                parentSize);
        }

        public void Restore(RectTransform rect)
        {
            rect.anchorMin = _anchorMin;
            rect.anchorMax = _anchorMax;
            rect.pivot = _pivot;
            rect.anchoredPosition = _anchoredPosition;
            rect.sizeDelta = _sizeDelta;
            rect.localScale = _localScale;
            rect.localRotation = _localRotation;
        }
    }
}

internal sealed class QuestItemWarningTriangleGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        var rect = GetPixelAdjustedRect();
        helper.AddVert(new Vector2(rect.center.x, rect.yMax), color, Vector2.zero);
        helper.AddVert(new Vector2(rect.xMax, rect.yMin), color, Vector2.zero);
        helper.AddVert(new Vector2(rect.xMin, rect.yMin), color, Vector2.zero);
        helper.AddTriangle(0, 1, 2);
    }
}
