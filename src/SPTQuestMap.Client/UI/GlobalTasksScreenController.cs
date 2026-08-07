using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class GlobalTasksScreenController : IDisposable
{
    private const float MinimumCharacterNavigationInset = 40f;
    private const float CharacterNavigationInset = 44f;

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
    private static readonly FieldInfo ScreenTooltipField = RequiredField("_tooltip");
    private static readonly FieldInfo TasksDescriptionField = RequiredTasksPanelField("_notesTaskDescription");
    private static readonly FieldInfo TasksTooltipField = RequiredTasksPanelField("simpleTooltip_0");

    private readonly TasksScreen _screen;
    private readonly InventoryController _inventoryController;
    private readonly AbstractQuestControllerClass _questController;
    private readonly ISession _session;
    private readonly ManualLogSource _log;
    private readonly bool _debugLogging;
    private readonly Dictionary<GameObject, bool> _nativeControlStates = new();
    private TasksPanel? _tasksPanel;
    private GameObject? _tasksDescription;
    private SimpleTooltip? _screenTooltip;
    private SimpleTooltip? _tasksTooltip;
    private RectTransform? _notesPart;
    private RectTransform? _questItemsPart;
    private Toggle? _notesToggle;
    private Toggle? _questItemsToggle;
    private Func<QuestClass, bool>? _questsAdditionalFilter;
    private QuestGraphTopology? _topology;
    private QuestGraphLayout? _layout;
    private QuestProfileOverlay? _overlay;
    private QuestGraphView? _graphView;
    private GlobalQuestGraphProjection? _projection;
    private RectTransform? _selectionSummary;
    private TextMeshProUGUI? _selectionSummaryText;
    private Image? _notesButtonImage;
    private Image? _questItemsButtonImage;
    private NativeOverlayMode _nativeOverlayMode;
    private GlobalQuestGraphMode _mode = GlobalQuestGraphMode.InProgress;
    private bool _showAllFuture;
    private bool _hideFinished;
    private bool _levelEligibleOnly = true;
    private string? _activeStatusFilter;
    private string? _traderId;
    private string _search = string.Empty;
    private string? _focusQuestId;
    private string? _selectedQuestId;
    private string? _viewStateScope;
    private bool _tasksPanelWasActive;
    private bool _tasksDescriptionWasActive;
    private float _surfaceTopInset;
    private string _surfaceTopSource = "native-union";
    private bool _mounted;
    private bool _disposed;

    public GlobalTasksScreenController(
        TasksScreen screen,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        ISession session,
        ManualLogSource log,
        bool debugLogging)
    {
        _screen = screen;
        _inventoryController = inventoryController;
        _questController = questController;
        _session = session;
        _log = log;
        _debugLogging = debugLogging;
    }

    public void Mount(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay,
        bool forceFailure)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlobalTasksScreenController));
        if (_mounted) throw new InvalidOperationException("The global Tasks graph is already mounted for this screen instance.");
        if (forceFailure) throw new InvalidOperationException("Forced global Tasks graph initialization failure.");

        _tasksPanel = TasksPanelField.GetValue(_screen) as TasksPanel
            ?? throw new InvalidOperationException("TasksScreen._tasksPanel was null.");
        _notesPart = (NotesPartField.GetValue(_screen) as GameObject)?.transform as RectTransform
            ?? throw new InvalidOperationException("TasksScreen._notesPart has no RectTransform.");
        _questItemsPart = (QuestItemsPartField.GetValue(_screen) as GameObject)?.transform as RectTransform
            ?? throw new InvalidOperationException("TasksScreen._questItemsPart has no RectTransform.");
        _tasksDescription = TasksDescriptionField.GetValue(_tasksPanel) as GameObject
            ?? throw new InvalidOperationException("TasksPanel._notesTaskDescription was null.");
        _screenTooltip = ScreenTooltipField.GetValue(_screen) as SimpleTooltip;
        _tasksTooltip = TasksTooltipField.GetValue(_tasksPanel) as SimpleTooltip;
        _questsAdditionalFilter = QuestsAdditionalFilterField.GetValue(_screen) as Func<QuestClass, bool>;
        _tasksPanelWasActive = _tasksPanel.gameObject.activeSelf;
        _tasksDescriptionWasActive = _tasksDescription.activeSelf;
        _topology = topology;
        _layout = layout;
        _overlay = overlay;
        LoadSettings();

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
        _log.LogInfo(
            "QUESTMAP_M06_MOUNT " +
            $"screen={_screen.GetInstanceID()}; mode={_mode}; nodes={_projection?.Nodes.Count ?? 0}; " +
            $"edges={_projection?.Edges.Count ?? 0}; nativeNotes=True; nativeQuestItems=True; fullSurface=True; nativeSelectorsUnmodified=True");
    }

    public string RefreshOverlay(QuestProfileOverlay overlay)
    {
        if (_disposed || !_mounted || _topology is null || _layout is null || _graphView is null) return "screen-inactive";
        _overlay = overlay;
        var nextProjection = BuildProjection();
        var membershipChanged = _projection is null
            || !_projection.Nodes.Select(node => node.Id).SequenceEqual(nextProjection.Nodes.Select(node => node.Id), StringComparer.Ordinal)
            || !_projection.Edges.SequenceEqual(nextProjection.Edges);
        if (membershipChanged)
        {
            RebuildGraph(true);
            return "global-projection-rebuilt";
        }

        _graphView.RefreshOverlay(overlay, _selectedQuestId);
        return "global-overlay-refreshed";
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

    public void Dispose()
    {
        if (_disposed) return;
        PersistCurrentState();
        QuestGraphViewStateStore.Flush();
        _disposed = true;
        foreach (var pair in _nativeControlStates) pair.Key.SetActive(pair.Value);
        _nativeControlStates.Clear();
        _graphView?.Dispose();
        _graphView = null;
        _selectionSummary = null;
        _selectionSummaryText = null;
        _notesButtonImage = null;
        _questItemsButtonImage = null;
        foreach (var ownedRoot in _screen.GetComponentsInChildren<RectTransform>(true)
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
        _log.LogInfo($"QUESTMAP_M06_DISPOSE screen={(_screen == null ? 0 : _screen.GetInstanceID())}; vanillaRestored=True");
    }

    private void ShowMode(GlobalQuestGraphMode mode)
    {
        if (_disposed || !_mounted || _tasksPanel is null) return;
        if (_mode != mode)
        {
            PersistCurrentState();
            _mode = mode;
            _focusQuestId = null;
            RebuildGraph(false);
        }
        ClearNativeOverlays();
        HideNativeTasksWorkspace();
        if (_graphView is not null) _graphView.Root.gameObject.SetActive(true);
        _log.LogInfo($"QUESTMAP_M06_VIEW screen={_screen.GetInstanceID()}; view={mode}; native=False");
    }

    private void RebuildGraph(bool preserveViewport)
    {
        if (_tasksPanel is null || _disposed) return;
        try
        {
            var viewport = preserveViewport ? _graphView?.CaptureViewportState() : null;
            PersistCurrentState();
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
        _viewStateScope = QuestGraphViewStateStore.Scope(_topology.Version, _overlay.ProfileId, $"global-v2:{_mode}");
        PersistedQuestGraphViewState persisted = default;
        var hasPersisted = loadPersisted && QuestGraphViewStateStore.TryLoad(_viewStateScope, out persisted);
        if (hasPersisted && _projection.NodesById.ContainsKey(persisted.SelectedQuestId ?? string.Empty))
        {
            _selectedQuestId = persisted.SelectedQuestId;
        }
        if (_selectedQuestId is not null && !_projection.NodesById.ContainsKey(_selectedQuestId))
        {
            _selectedQuestId = null;
            _focusQuestId = null;
        }

        var initialViewport = liveViewport ?? (hasPersisted ? persisted.Viewport : null);
        _graphView = QuestGraphView.Create(
            mountRect,
            BuildTitle(),
            _projection,
            _topology,
            _overlay,
            SelectQuest,
            PersistCurrentState,
            _log,
            _debugLogging,
            initialViewport,
            BuildCanvasActions(),
            true,
            HandleBackgroundClick,
            true,
            FocusQuest);
        Canvas.ForceUpdateCanvases();
        var viewportSize = _graphView.ViewportSize;
        var rootSize = _graphView.Root.rect.size;
        _log.LogInfo(
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
            UpdateSelectionSummary();
        }
    }

    private GlobalQuestGraphProjection BuildProjection()
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
                _activeStatusFilter,
                _traderId,
                _search,
                _focusQuestId,
                QuestRouteFilter.None,
                _selectedQuestId));
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
        var title = _mode == GlobalQuestGraphMode.InProgress ? "IN PROGRESS" : "QUEST MAP";
        if (!string.IsNullOrWhiteSpace(_search)) title += $"  ·  SEARCH: {_search}";
        return title;
    }

    private void SelectQuest(string questId)
    {
        if (_graphView is null || _topology is null || !_topology.NodesById.ContainsKey(questId)) return;
        if (string.Equals(_selectedQuestId, questId, StringComparison.Ordinal)) return;
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
        UpdateSelectionSummary();
        PersistCurrentState();
        var live = _overlay?.QuestsById.TryGetValue(questId, out var state) == true && state.HasLiveQuest;
        _log.LogInfo($"QUESTMAP_M06_SELECT view={_mode}; quest={questId}; live={live}; detail=graph-selection");
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
        RebuildGraph(true);
    }

    private void ToggleFinished()
    {
        _hideFinished = !_hideFinished;
        RebuildGraph(true);
    }

    private void ToggleLevel()
    {
        _levelEligibleOnly = !_levelEligibleOnly;
        RebuildGraph(true);
    }

    private void CycleActiveStatus()
    {
        _activeStatusFilter = _activeStatusFilter switch
        {
            null => "STARTED",
            "STARTED" => "READY",
            "READY" => "RETRY",
            _ => null,
        };
        RebuildGraph(true);
    }

    private void ClearSelection()
    {
        if (_selectedQuestId is null && _focusQuestId is null) return;
        _selectedQuestId = null;
        _focusQuestId = null;
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
        RebuildGraph(true);
    }

    private void BuildGlobalChrome()
    {
        if (_graphView is null || _topology is null) return;
        var header = _graphView.Root.Find("Header") as RectTransform
            ?? throw new InvalidOperationException("QuestMap global header was not created.");

        AddHeaderButton(header, "InProgressTab", "IN PROGRESS", 12, -4, 112,
            _mode == GlobalQuestGraphMode.InProgress, () => ShowMode(GlobalQuestGraphMode.InProgress));
        AddHeaderButton(header, "QuestMapTab", "QUEST MAP", 130, -4, 100,
            _mode == GlobalQuestGraphMode.Full, () => ShowMode(GlobalQuestGraphMode.Full));
        _notesButtonImage = AddRightHeaderButton(header, "Notes", "NOTES", 116, 72,
            _nativeOverlayMode == NativeOverlayMode.Notes, () => ToggleNativeOverlay(true));
        _questItemsButtonImage = AddRightHeaderButton(header, "QuestItems", "QUEST ITEMS", 6, 104,
            _nativeOverlayMode == NativeOverlayMode.QuestItems, () => ToggleNativeOverlay(false));

        BuildSearchControl(header);
        if (_mode == GlobalQuestGraphMode.Full)
        {
            AddHeaderButton(header, "Future", "FUTURE", 12, -70, 68, _showAllFuture, ToggleFuture, 23);
            AddHeaderButton(header, "Finished", "✓", 86, -70, 42, !_hideFinished, ToggleFinished, 23);
            AddHeaderButton(header, "Level", $"≤ {_overlay?.Level ?? 0}", 134, -70, 66, _levelEligibleOnly, ToggleLevel, 23);
        }
        else
        {
            AddHeaderButton(header, "Status", _activeStatusFilter is null ? "STATUS: ALL" : $"STATUS: {_activeStatusFilter}",
                12, -70, 118, _activeStatusFilter is not null, CycleActiveStatus, 23);
        }

        var separator = UnityUiFactory.CreateRect("FilterSeparator", header);
        separator.anchorMin = separator.anchorMax = separator.pivot = new Vector2(0, 1);
        separator.anchoredPosition = new Vector2(282, -43);
        separator.sizeDelta = new Vector2(1, 48);
        separator.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
        BuildTraderStrip(header);
        UpdateNativeOverlayButtons();
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
            RebuildGraph(true);
        });

        AddHeaderButton(header, "ClearSearch", "×", 240, -42, 30, !string.IsNullOrEmpty(_search), () =>
        {
            if (string.IsNullOrEmpty(_search)) return;
            _search = string.Empty;
            RebuildGraph(true);
        }, 24);
    }

    private void BuildTraderStrip(RectTransform header)
    {
        if (_graphView is null || _topology is null) return;
        var traders = _topology.Traders
            .Where(trader => _topology.Nodes.Any(node => string.Equals(node.TraderId, trader.Id, StringComparison.Ordinal)))
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
                selected ? new Color(0.96f, 0.84f, 0.39f, 1) : QuestGraphPalette.MutedText);
            traderLabel.enableWordWrapping = false;
            button.onClick.AddListener(() => SetTrader(id));
        }
    }

    private static Image AddHeaderButton(
        RectTransform parent, string name, string label, float x, float y, float width, bool active, Action action, float height = 32)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        UnityUiFactory.AddText(rect.gameObject, label, height <= 24 ? 10 : 12, TextAlignmentOptions.Center, Color.white);
        button.onClick.AddListener(() => action());
        return rect.GetComponent<Image>();
    }

    private static Image AddRightHeaderButton(
        RectTransform parent, string name, string label, float right, float width, bool active, Action action)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-right, -4);
        rect.sizeDelta = new Vector2(width, 32);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        UnityUiFactory.AddText(rect.gameObject, label, 12, TextAlignmentOptions.Center, Color.white);
        button.onClick.AddListener(() => action());
        return rect.GetComponent<Image>();
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
        if (_overlay is not null)
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
                    QuestRouteFilter.None));
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
    }

    private void BuildSelectionSummary()
    {
        if (_graphView is null) return;
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
        var requested = notes ? NativeOverlayMode.Notes : NativeOverlayMode.QuestItems;
        ApplyNativeOverlayMode(_nativeOverlayMode == requested ? NativeOverlayMode.None : requested, true);
        _log.LogInfo(
            $"QUESTMAP_M06_OVERLAY requested={(notes ? "notes" : "quest-items")}; mode={_nativeOverlayMode}; " +
            $"notesVisible={_notesPart.gameObject.activeSelf}; questItemsVisible={_questItemsPart.gameObject.activeSelf}");
    }

    private void ClearNativeOverlays()
    {
        _nativeOverlayMode = NativeOverlayMode.None;
        _notesToggle?.SetIsOnWithoutNotify(false);
        _questItemsToggle?.SetIsOnWithoutNotify(false);
        if (_notesPart is not null) _notesPart.gameObject.SetActive(false);
        if (_questItemsPart is not null) _questItemsPart.gameObject.SetActive(false);
        UpdateNativeOverlayButtons();
    }

    private void ApplyNativeOverlayMode(NativeOverlayMode mode, bool invokeNative)
    {
        if (_notesPart is null || _questItemsPart is null || _notesToggle is null || _questItemsToggle is null) return;
        if (invokeNative && _nativeOverlayMode == NativeOverlayMode.Notes && mode != NativeOverlayMode.Notes)
            _notesToggle.isOn = false;
        if (invokeNative && _nativeOverlayMode == NativeOverlayMode.QuestItems && mode != NativeOverlayMode.QuestItems)
            _questItemsToggle.isOn = false;
        _notesToggle.SetIsOnWithoutNotify(false);
        _questItemsToggle.SetIsOnWithoutNotify(false);

        if (invokeNative && mode == NativeOverlayMode.Notes) _notesToggle.isOn = true;
        if (invokeNative && mode == NativeOverlayMode.QuestItems) _questItemsToggle.isOn = true;

        // Native toggle-group callbacks may select the opposite branch while a
        // toggle is turned off. The custom state is authoritative, so settle
        // both native toggles and roots explicitly after invoking EFT's opener.
        _notesToggle.SetIsOnWithoutNotify(mode == NativeOverlayMode.Notes);
        _questItemsToggle.SetIsOnWithoutNotify(mode == NativeOverlayMode.QuestItems);
        _notesPart.gameObject.SetActive(mode == NativeOverlayMode.Notes);
        _questItemsPart.gameObject.SetActive(mode == NativeOverlayMode.QuestItems);
        _nativeOverlayMode = mode;

        if (_graphView?.Root.parent is Transform parent)
        {
            _graphView.Root.SetAsLastSibling();
            if (mode == NativeOverlayMode.Notes) DirectChildUnder(parent, _notesPart).SetAsLastSibling();
            if (mode == NativeOverlayMode.QuestItems) DirectChildUnder(parent, _questItemsPart).SetAsLastSibling();
        }
        UpdateNativeOverlayButtons();
    }

    private void UpdateNativeOverlayButtons()
    {
        if (_notesButtonImage is not null)
            _notesButtonImage.color = _nativeOverlayMode == NativeOverlayMode.Notes ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control;
        if (_questItemsButtonImage is not null)
            _questItemsButtonImage.color = _nativeOverlayMode == NativeOverlayMode.QuestItems ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control;
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
        if (_nativeOverlayMode == NativeOverlayMode.Notes && _notesPart.gameObject.activeSelf) notesBranch.SetAsLastSibling();
        if (_nativeOverlayMode == NativeOverlayMode.QuestItems && _questItemsPart.gameObject.activeSelf) questItemsBranch.SetAsLastSibling();
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

    private enum NativeOverlayMode
    {
        None,
        Notes,
        QuestItems,
    }
}
