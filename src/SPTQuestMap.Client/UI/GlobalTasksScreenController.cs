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

    private static readonly string[] TraderOrder =
    [
        "54cb50c76803fa8b248b4571", "54cb57776803fa99248b456e", "579dc571d53a0658a154fbec",
        "58330581ace78e27b8b10cee", "5935c25fb3acc3127c3d8cd9", "5a7c2eca46aef81a7ca2145d",
        "5ac3b934156ae10c4430e83c", "5c0647fdd443bc2504c2d371", "6617beeaa9cfa777ca915b7c",
        "638f541a29ffd1183d187f57", "656f0f98d80a697f855d34b1",
    ];
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
    private InventoryController _inventoryController;
    private AbstractQuestControllerClass _questController;
    private ISession _session;
    private readonly ManualLogSource _log;
    private readonly bool _debugLogging;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly QuestTrackingService _tracking;
    private readonly bool _customDetailsEnabled;
    private readonly Func<bool> _showHiddenRewards;
    private readonly Func<bool> _defaultDetailsToSummary;
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
    private bool _disposed;

    public GlobalTasksScreenController(
        TasksScreen screen,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        ISession session,
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
        _inventoryController = inventoryController;
        _questController = questController;
        _session = session;
        _log = log;
        _debugLogging = debugLogging;
        _assetCache = assetCache;
        _tracking = tracking;
        _defaultDetailsToSummary = defaultDetailsToSummary;
        _requestQuestRefresh = requestQuestRefresh;
        _customDetailsEnabled = customDetailsEnabled;
        _showHiddenRewards = showHiddenRewards;
    }

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
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_MOUNT " +
            $"screen={_screen.GetInstanceID()}; mode={_mode}; nodes={_projection?.Nodes.Count ?? 0}; " +
            $"edges={_projection?.Edges.Count ?? 0}; nativeNotes=True; nativeQuestItems=True; fullSurface=True; nativeSelectorsUnmodified=True; " +
            $"raidInProgressOnly={_raidInProgressOnly}; favoriteQuestService=native; assetCache=shared; cachedAssets={_assetCache.CachedCount}; " +
            $"pendingAssets={_assetCache.PendingCount}; networkRequests={_assetCache.NetworkRequests}; cacheHits={_assetCache.CacheHits}");
    }

    public bool CanResume => !_disposed && _mounted && _graphView?.Root != null;

    public string Resume(
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        ISession session,
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay)
    {
        if (!CanResume) return "screen-cache-invalid";
        _inventoryController = inventoryController;
        _questController = questController;
        _session = session;
        _overlay = overlay;

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
            if (_customDetailsEnabled && _selectedQuestId is not null) ShowSelectedQuestDetails();
        }
        _visible = true;
        var cachedRows = (_graphView as InProgressQuestTableView)?.CachedRowCount ?? 0;
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_RESUME " +
            $"screen={_screen.GetInstanceID()}; topologyChanged={topologyChanged}; modeChanged={modeChanged}; " +
            $"mode={_mode}; cachedRows={cachedRows}; rootReused={!topologyChanged && !modeChanged}");
        return topologyChanged || modeChanged ? "global-content-rebuilt" : "global-screen-reused";
    }

    public void Suspend()
    {
        if (_disposed || !_mounted || !_visible) return;
        PersistCurrentState();
        QuestGraphViewStateStore.Flush();
        ClearNativeOverlays();
        if (_graphView?.Root != null) _graphView.Root.gameObject.SetActive(false);
        _visible = false;
        var cachedRows = (_graphView as InProgressQuestTableView)?.CachedRowCount ?? 0;
        QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_SUSPEND screen={_screen.GetInstanceID()}; cached=True; rows={cachedRows}");
    }

    public string RefreshOverlay(QuestProfileOverlay overlay, bool refreshAllNativeControls = true)
    {
        if (_disposed || !_mounted || _topology is null || _layout is null || _graphView is null) return "screen-inactive";
        var locationsChanged = EnableLocationsForNewTaskStates(_overlay, overlay);
        _overlay = overlay;
        var nextProjection = BuildProjection();
        var membershipChanged = _projection is null
            || !_projection.Nodes.Select(node => node.Id).SequenceEqual(nextProjection.Nodes.Select(node => node.Id), StringComparer.Ordinal)
            || !_projection.Edges.SequenceEqual(nextProjection.Edges);
        if (_graphView is InProgressQuestTableView table)
        {
            _projection = nextProjection;
            if (_selectedQuestId is not null && !_projection.NodesById.ContainsKey(_selectedQuestId))
            {
                _selectedQuestId = null;
                CloseQuestDetails();
            }
            table.RefreshOverlay(overlay, _selectedQuestId, nextProjection, refreshAllNativeControls);
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
    {
        if (_disposed || !_mounted || _graphView is null) return "screen-inactive";
        _overlay = overlay;
        var nextProjection = BuildProjection();
        var membershipChanged = _projection is null
            || !_projection.Nodes.Select(node => node.Id).SequenceEqual(nextProjection.Nodes.Select(node => node.Id), StringComparer.Ordinal)
            || !_projection.Edges.SequenceEqual(nextProjection.Edges);
        if (membershipChanged)
        {
            _projection = nextProjection;
            if (_selectedQuestId is not null && !_projection.NodesById.ContainsKey(_selectedQuestId))
            {
                _selectedQuestId = null;
                _focusQuestId = null;
                CloseQuestDetails();
            }
            if (_graphView is InProgressQuestTableView table)
            {
                table.RefreshOverlay(overlay, _selectedQuestId, nextProjection, false);
                RebuildInProgressChrome();
            }
            else RebuildGraph(true);
            UpdateSelectionDetails();
            return "global-projection-updated";
        }
        if (_projection is null || !_projection.NodesById.ContainsKey(questId)) return "global-quest-not-visible";
        _graphView.RefreshQuest(overlay, questId);
        if (string.Equals(_selectedQuestId, questId, StringComparison.Ordinal)) UpdateSelectionDetails();
        return "global-quest-refreshed";
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
        QuestMapDebugLog.Info(_log,
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
        QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_DISPOSE screen={(_screen == null ? 0 : _screen.GetInstanceID())}; vanillaRestored=True");
    }

    private void ShowMode(GlobalQuestGraphMode mode)
    {
        if (_disposed || !_mounted || _tasksPanel is null) return;
        if (_raidInProgressOnly && mode == GlobalQuestGraphMode.Full)
        {
            QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_VIEW screen={_screen.GetInstanceID()}; view=Full; blocked=True; reason=in-raid-in-progress-only");
            return;
        }
        if (_mode != mode)
        {
            PersistCurrentState();
            _mode = mode;
            _focusQuestId = null;
            RebuildGraph(false);
        }
        ClearNativeOverlays();
        HideNativeTasksWorkspace();
        if (_graphView is not null)
        {
            _graphView.Root.gameObject.SetActive(true);
            if (_customDetailsEnabled && _selectedQuestId is not null) ShowSelectedQuestDetails();
        }
        QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_VIEW screen={_screen.GetInstanceID()}; view={mode}; native=False");
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
            _log.LogError($"QUESTMAP_M06_ERROR phase=post-mount-rebuild; {exception}");
            Dispose();
            _tasksPanel?.Show(_inventoryController, _questController, _session, _questsAdditionalFilter);
            if (_tasksPanel is not null) _tasksPanel.gameObject.SetActive(true);
            _log.LogWarning($"QUESTMAP_M06_STATE screen={_screen.GetInstanceID()}; active=False; safelyDisabled=True; reason=post-mount rebuild failed; vanillaRestored=True");
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
                _log,
                _assetCache,
                 _favoriteQuestService ?? throw new InvalidOperationException("Native favorite-quest service is unavailable."),
                 _tracking,
                 QuestMutationsAllowed,
                 CreateTableHandoverAction,
                  CanAcceptTableQuest,
                  AcceptTableQuestAsync,
                  CanCompleteTableQuest,
                  CompleteTableQuestAsync,
                  CanReplaceRepeatable,
                 ReplaceTableQuestAsync,
                 _requestQuestRefresh,
                 initialViewport)
            : QuestGraphView.Create(
                mountRect,
                BuildTitle(),
                _projection,
                _topology,
                _overlay,
                SelectQuest,
                PersistCurrentState,
                _log,
                _debugLogging,
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
        QuestMapDebugLog.Info(_log,
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
            if (_customDetailsEnabled) ShowSelectedQuestDetails();
        }
    }

    private GlobalQuestGraphProjection BuildProjection(bool applyLocationFilter = true, bool applyTraderFilter = true)
    {
        if (_topology is null || _layout is null || _overlay is null) throw new InvalidOperationException("Global graph data is not ready.");
        return GlobalQuestGraphProjectionBuilder.Build(
            _topology,
            _layout,
            _overlay,
            new GlobalQuestGraphOptions(
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
                ExcludeReadyToFinish: _raidInProgressOnly));
    }

    private GlobalQuestGraphProjection BuildInProgressCacheProjection()
    {
        if (_topology is null || _layout is null || _overlay is null)
            throw new InvalidOperationException("Global graph data is not ready.");
        return GlobalQuestGraphProjectionBuilder.Build(
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

    private void InitializeInProgressLocations()
    {
        if (_inProgressLocationsInitialized || _topology is null || _layout is null || _overlay is null) return;
        foreach (var node in BuildProjection(false).Nodes)
            _inProgressLocationIds.Add(node.Location.Any ? "any" : node.Location.Id);
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
            changed |= _inProgressLocationIds.Add(node.Location.Any ? "any" : node.Location.Id);
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
        _inProgressLocationIds.UnionWith(context.DefaultLocationIds());
        _inProgressLocationsInitialized = true;
        QuestMapDebugLog.Info(_log,
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
            new QuestGraphHeaderAction("Focus", _focusQuestId is null ? "FOCUS CHAIN" : "UNFOCUS", 92, _focusQuestId is not null, ToggleFocus),
            new QuestGraphHeaderAction("Clear", "CLEAR SELECTION", 106, _selectedQuestId is not null, ClearSelection),
        ];
    }

    private string BuildTitle()
    {
        var title = _mode == GlobalQuestGraphMode.InProgress ? "TASKS" : "QUEST MAP";
        if (!string.IsNullOrWhiteSpace(_search)) title += $"  ·  SEARCH: {_search}";
        return title;
    }

    private void SelectQuest(string questId)
    {
        if (_graphView is null || _topology is null || !_topology.NodesById.ContainsKey(questId)) return;
        if (string.Equals(_selectedQuestId, questId, StringComparison.Ordinal))
        {
            if (_mode == GlobalQuestGraphMode.InProgress)
            {
                ClearSelection();
                return;
            }
            if (_customDetailsEnabled) ShowSelectedQuestDetails();
            return;
        }
        _selectedQuestId = questId;
        if (_mode == GlobalQuestGraphMode.Full)
        {
            var nextProjection = BuildProjection();
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
        if (_customDetailsEnabled) ShowSelectedQuestDetails();
        PersistCurrentState();
        var live = _overlay?.QuestsById.TryGetValue(questId, out var state) == true && state.HasLiveQuest;
        QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_SELECT view={_mode}; quest={questId}; live={live}; detail=graph-selection");
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

        AddHeaderButton(header, "InProgressTab", "TASKS", 12, -4, 112,
            _mode == GlobalQuestGraphMode.InProgress, () => ShowMode(GlobalQuestGraphMode.InProgress));
        AddHeaderButton(header, "QuestMapTab", "QUEST MAP", 130, -4, 100,
            _mode == GlobalQuestGraphMode.Full, () => ShowMode(GlobalQuestGraphMode.Full), enabled: !_raidInProgressOnly);
        _notesButtonImage = AddRightHeaderButton(header, "Notes", "NOTES", 116, 72,
            _overlayMode == GlobalOverlayMode.Notes, () => ToggleNativeOverlay(true));
        _questItemsButtonImage = AddRightHeaderButton(header, "QuestItems", "QUEST ITEMS", 6, 104,
            _overlayMode == GlobalOverlayMode.QuestItems, () => ToggleNativeOverlay(false));
        if (_customDetailsEnabled)
        {
            var detailSeparator = UnityUiFactory.CreateRect("DetailOverlaySeparator", header);
            detailSeparator.anchorMin = detailSeparator.anchorMax = detailSeparator.pivot = new Vector2(1, 1);
            detailSeparator.anchoredPosition = new Vector2(-198, -4);
            detailSeparator.sizeDelta = new Vector2(1, 32);
            detailSeparator.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
            _questDescriptionButtonImage = AddRightHeaderButton(
                header,
                "QuestDescription",
                "QUEST DESCRIPTION",
                208,
                144,
                _overlayMode == GlobalOverlayMode.QuestDescription,
                ToggleQuestDetails);
            _questDescriptionButton = _questDescriptionButtonImage.GetComponent<Button>();
        }

        BuildSearchControl(header);
        if (_mode == GlobalQuestGraphMode.Full)
        {
            AddHeaderButton(header, "Future", "FUTURE", 12, -70, 68, _showAllFuture, ToggleFuture, 23);
            AddHeaderButton(header, "Finished", "✓", 86, -70, 42, !_hideFinished, ToggleFinished, 23);
            AddHeaderButton(header, "Level", $"≤ {_overlay?.Level ?? 0}", 134, -70, 66, _levelEligibleOnly, ToggleLevel, 23);
        }
        else
        {
            AddHeaderButton(header, "Repeatables", _includeAvailableRepeatables ? "↻ ALL" : "↻ ACTIVE", 12, -70, 86,
                _includeAvailableRepeatables, ToggleRepeatables, 23);
            AddHeaderButton(header, "HideCompletedTasks",
                _hideCompletedInProgressTasks ? "✓: HIDDEN" : "✓: SHOWN", 104, -70, 100,
                _hideCompletedInProgressTasks, ToggleCompletedTasks, 23);
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
        var relevantNodes = BuildProjection(false).Nodes;
        var locations = relevantNodes
            .Select(node => node.Location)
            .Where(location => !location.Any && !string.IsNullOrWhiteSpace(location.Id))
            .GroupBy(location => location.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(location => location.Name ?? location.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(location => location.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var hasAny = relevantNodes.Any(node => node.Location.Any);
        const float startX = 12;
        const float y = -103;
        const float width = 116;
        const float height = 32;
        const float gap = 6;
        var nextIndex = 0;
        if (hasAny)
        {
            AddMapChoice("Any", "any", FallbackLocationBannerUrl, nextIndex, _inProgressLocationIds.Contains("any"));
            nextIndex++;
        }
        for (var index = 0; index < locations.Length; index++)
        {
            var location = locations[index];
            AddMapChoice(location.Name ?? location.Id, location.Id, location.BannerImageUrl ?? FallbackLocationBannerUrl, nextIndex + index,
                _inProgressLocationIds.Contains(location.Id));
        }

        void AddMapChoice(string label, string? id, string? bannerUrl, int index, bool selected)
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
            text.enableWordWrapping = false;
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = selected ? QuestGraphPalette.Selected : QuestGraphPalette.Border;
            outline.effectDistance = selected ? new Vector2(2, -2) : Vector2.one;
            if (id is not null)
                _inProgressMapVisuals[id] = new MapFilterVisual(background, banner, shadeImage, outline);
            button.onClick.AddListener(() => ToggleInProgressLocation(id));

            var pointerClicks = root.gameObject.AddComponent<EventTrigger>();
            var pointerClick = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            pointerClick.callback.AddListener(eventData =>
            {
                if (eventData is PointerEventData { button: PointerEventData.InputButton.Right })
                    SelectOnlyInProgressLocation(id);
            });
            pointerClicks.triggers.Add(pointerClick);
        }
    }

    private bool UpdateInProgressPresentation(bool projectionChanged, bool rebuildChrome)
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

        table.UpdatePresentation(_projection, _hideCompletedInProgressTasks);
        if (rebuildChrome) RebuildInProgressChrome();
        UpdateSelectionDetails();
        PersistCurrentState();
        stopwatch.Stop();
        QuestMapDebugLog.Info(_log,
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

    private void ToggleInProgressLocation(string? locationId)
    {
        if (locationId is null) return;
        if (!_inProgressLocationIds.Add(locationId)) _inProgressLocationIds.Remove(locationId);
        RefreshInProgressMapVisuals();
        if (!UpdateInProgressPresentation(true, false)) RebuildGraph(true);
    }

    private void SelectOnlyInProgressLocation(string? locationId)
    {
        if (locationId is null
            || (_inProgressLocationIds.Count == 1 && _inProgressLocationIds.Contains(locationId))) return;
        _inProgressLocationIds.Clear();
        _inProgressLocationIds.Add(locationId);
        RefreshInProgressMapVisuals();
        if (!UpdateInProgressPresentation(true, false)) RebuildGraph(true);
    }

    private void RefreshInProgressMapVisuals()
    {
        foreach (var pair in _inProgressMapVisuals)
        {
            var selected = _inProgressLocationIds.Contains(pair.Key);
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

    private bool QuestMutationsAllowed() => !InRaidQuestContext.TryCapture(out _);

    private NativeQuestHandoverAction? CreateTableHandoverAction(
        RectTransform parent,
        string questId,
        string objectiveId)
    {
        if (!QuestMutationsAllowed()) return null;
        var quest = _questController.Quests.LastOrDefault(value => string.Equals(value.Id, questId, StringComparison.Ordinal));
        if (quest?.QuestStatus != EQuestStatus.Started) return null;
        return NativeQuestTableActions.TryCreateHandover(
            parent, quest, objectiveId, _questController, _inventoryController);
    }

    private bool CanReplaceRepeatable(string questId)
    {
        if (!QuestMutationsAllowed() || _topology?.NodesById.TryGetValue(questId, out var node) != true
            || node.RepeatableKind is not ("Daily" or "Weekly")) return false;
        return _questController.Quests.LastOrDefault(value => string.Equals(value.Id, questId, StringComparison.Ordinal))
            ?.IsChangeAllowed == true;
    }

    private bool CanAcceptTableQuest(string questId)
    {
        if (!QuestMutationsAllowed() || _topology?.NodesById.TryGetValue(questId, out var node) != true
            || NativeQuestTableActions.IsRaidOnlyTrader(node.TraderId)) return false;
        return _questController.Quests.LastOrDefault(value => string.Equals(value.Id, questId, StringComparison.Ordinal))
            ?.QuestStatus == EQuestStatus.AvailableForStart;
    }

    private async System.Threading.Tasks.Task AcceptTableQuestAsync(RectTransform parent, string questId)
    {
        if (!CanAcceptTableQuest(questId) || _topology is null) return;
        var quest = _questController.Quests.Last(value => string.Equals(value.Id, questId, StringComparison.Ordinal));
        await NativeQuestTableActions.AcceptAsync(
            parent, _session, _inventoryController, _questController, quest,
            _topology.NodesById[questId].TraderId, _log);
    }

    private bool CanCompleteTableQuest(string questId)
    {
        if (!QuestMutationsAllowed() || _topology?.NodesById.TryGetValue(questId, out var node) != true
            || NativeQuestTableActions.IsRaidOnlyTrader(node.TraderId)) return false;
        return _questController.Quests.LastOrDefault(value => string.Equals(value.Id, questId, StringComparison.Ordinal))
            ?.QuestStatus == EQuestStatus.AvailableForFinish;
    }

    private async System.Threading.Tasks.Task CompleteTableQuestAsync(RectTransform parent, string questId)
    {
        if (!CanCompleteTableQuest(questId) || _topology is null) return;
        var quest = _questController.Quests.Last(value => string.Equals(value.Id, questId, StringComparison.Ordinal));
        await NativeQuestTableActions.CompleteAsync(
            parent, _session, _inventoryController, _questController, quest,
            _topology.NodesById[questId].TraderId, _log);
    }

    private async System.Threading.Tasks.Task ReplaceTableQuestAsync(RectTransform parent, string questId)
    {
        if (!CanReplaceRepeatable(questId) || _topology is null) return;
        var quest = _questController.Quests.Last(value => string.Equals(value.Id, questId, StringComparison.Ordinal));
        await NativeQuestTableActions.ReplaceAsync(
            parent, _session, _inventoryController, _questController, quest,
            _topology.NodesById[questId].TraderId, _log);
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
        var placeholder = UnityUiFactory.AddText(viewport.gameObject, "QUEST, TRADER, OR ID", 12,
            TextAlignmentOptions.MidlineLeft, new Color(0.55f, 0.57f, 0.58f, 1));
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

        AddHeaderButton(header, "ClearSearch", "×", 240, -42, 30, !string.IsNullOrEmpty(_search), () =>
        {
            if (string.IsNullOrEmpty(_search)) return;
            _search = string.Empty;
            if (!UpdateInProgressPresentation(true, true)) RebuildGraph(true);
        }, 24);
    }

    private void BuildTraderStrip(RectTransform header)
    {
        if (_graphView is null || _topology is null) return;
        var relevantTraderIds = BuildProjection(true, false).Nodes
            .Select(node => node.TraderId)
            .ToHashSet(StringComparer.Ordinal);
        var traders = _topology.Traders
            .Where(trader => relevantTraderIds.Contains(trader.Id))
            .OrderBy(trader => TraderRank(trader.Id))
            .ThenBy(trader => trader.Name, StringComparer.Ordinal)
            .ToArray();
        AddTraderChoice("/files/trader/avatar/unknown.png", "All", null, 0);
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
            var fallback = UnityUiFactory.AddText(portraitRoot.gameObject, id is null ? string.Empty : Initials(label), 9,
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
        }
    }

    private static Image AddHeaderButton(
        RectTransform parent, string name, string label, float x, float y, float width, bool active, Action action,
        float height = 32, bool enabled = true)
    {
        return QuestWorkspaceChrome.AddButton(parent, name, label, x, y, width, active, action, height, enabled);
    }

    private static Image AddRightHeaderButton(
        RectTransform parent, string name, string label, float right, float width, bool active, Action action)
    {
        return QuestWorkspaceChrome.AddRightButton(parent, name, label, right, width, active, action);
    }

    private static int TraderRank(string traderId)
    {
        var rank = Array.IndexOf(TraderOrder, traderId);
        return rank < 0 ? int.MaxValue : rank;
    }

    private static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        return words.Length == 1
            ? words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant()
            : $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
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
        if (_graphView is null || _customDetailsEnabled) return;
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
        QuestMapDebugLog.Info(_log,
            $"QUESTMAP_M06_OVERLAY requested={(notes ? "notes" : "quest-items")}; mode={_overlayMode}; " +
            $"notesVisible={_notesPart.gameObject.activeSelf}; questItemsVisible={_questItemsPart.gameObject.activeSelf}");
    }

    private void ToggleQuestDetails()
    {
        if (!_customDetailsEnabled || _selectedQuestId is null) return;
        ApplyOverlayMode(
            _overlayMode == GlobalOverlayMode.QuestDescription
                ? GlobalOverlayMode.None
                : GlobalOverlayMode.QuestDescription,
            true);
    }

    private void ShowSelectedQuestDetails()
    {
        if (!_customDetailsEnabled || _selectedQuestId is null || _graphView is null || _topology is null || _overlay is null)
            return;
        if (_detailPane is null)
        {
            _detailPane = QuestDetailsPane.Create(
                _graphView.Root.parent as RectTransform
                    ?? throw new InvalidOperationException("QuestMap global root has no overlay parent."),
                _graphView.Root.Find("Viewport") as RectTransform
                    ?? throw new InvalidOperationException("QuestMap global viewport was not created."),
                _session,
                _inventoryController,
                _questController,
                _assetCache,
                _log,
                _showHiddenRewards,
                _defaultDetailsToSummary,
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
        if (_questItemsButtonImage is not null)
            _questItemsButtonImage.color = _overlayMode == GlobalOverlayMode.QuestItems ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control;
        if (_questDescriptionButtonImage is not null)
            _questDescriptionButtonImage.color = _overlayMode == GlobalOverlayMode.QuestDescription
                ? QuestGraphPalette.ControlActive
                : QuestGraphPalette.Control;
        if (_questDescriptionButton is not null) _questDescriptionButton.interactable = _selectedQuestId is not null;
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

        QuestMapDebugLog.Info(_log,
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
        if (!_customDetailsEnabled || _detailPane is null || _topology is null || _overlay is null || _selectedQuestId is null)
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
        if (progressPercent.HasValue) status += $" · ~{progressPercent.Value:0.#}%";
        var completed = liveState?.Objectives.Count(objective => objective.Complete) ?? 0;
        var objectiveSummary = node.Objectives.Count == 0
            ? "No objectives"
            : $"Objectives {completed}/{node.Objectives.Count}";
        var routes = new List<string>();
        if (_topology.CollectorPathQuestIds.Contains(node.Id)) routes.Add("Collector");
        if (_topology.LightkeeperPathQuestIds.Contains(node.Id)) routes.Add("Lightkeeper");
        var routeText = routes.Count == 0 ? string.Empty : $"  ·  {string.Join(" / ", routes)} route";
        _selectionSummaryText.text =
            $"{node.Name.ToUpperInvariant()}  ·  {node.TraderName}  ·  {status}{routeText}\n" +
            $"{objectiveSummary}  ·  Select Focus to isolate prerequisites and direct successors. Native transactions remain EFT-owned.";
        _selectionSummary.gameObject.SetActive(true);
        _selectionSummary.SetAsLastSibling();
    }

    private static FieldInfo RequiredField(string name) => AccessTools.Field(typeof(TasksScreen), name)
        ?? throw new MissingFieldException(typeof(TasksScreen).FullName, name);

    private static FieldInfo RequiredTasksPanelField(string name) => AccessTools.Field(typeof(TasksPanel), name)
        ?? throw new MissingFieldException(typeof(TasksPanel).FullName, name);

    private static string FormatSize(Vector2 value) => $"{value.x:0.##}x{value.y:0.##}";

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
