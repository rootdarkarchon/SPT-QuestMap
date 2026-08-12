using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Logging;
using EFT;
using EFT.Quests;
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

internal sealed class InProgressQuestTableView : IGlobalTasksContentView
{
    private const float GlobalHeaderHeight = 140f;
    private const float TableHeaderHeight = 38f;
    private const float NonTaskContentHeight = 78f;
    private const float MinimumRowHeight = NonTaskContentHeight + 4f;
    private const float TaskRowHeight = 30f;
    private const float CompactTaskRowHeight = 20f;
    private const float FilteredTaskSummaryHeight = 20f;
    private static readonly Color RowUnderlay = new(0.035f, 0.041f, 0.044f, 0.52f);
    private static readonly Color CellSurface = new(0.09f, 0.102f, 0.108f, 0.96f);
    private const int CollapsedTaskCount = 4;
    private const string FallbackLocationBannerUrl = "/files/banners/norvinskzone.png";

    private readonly RectTransform _viewport;
    private readonly RectTransform _scrollViewport;
    private readonly RectTransform _content;
    private readonly RectTransform _nativeHostRoot;
    private readonly ScrollRect _scrollRect;
    private QuestGraphTopology _topology;
    private GlobalQuestGraphProjection _projection;
    private readonly IReadOnlyList<QuestTableSortCriterion> _sortCriteria;
    private bool _hideCompletedTasks;
    private readonly IReadOnlyCollection<string> _expandedQuestIds;
    private readonly Action<string> _onSelected;
    private readonly Action _onBackgroundClick;
    private readonly Action<QuestTableSortColumn> _onSortChanged;
    private readonly Action<string> _onExpansionChanged;
    private GClass3794? _favoriteQuestService;
    private readonly QuestTrackingService _tracking;
    private readonly NativeQuestWorkspaceContext _workspace;
    private readonly QuestObjectiveSkipVisibility _skipVisibility;
    private readonly Action<string, QuestDetailsActionKind> _onQuestMutated;
    private readonly QuestTableSectionMode _sectionMode;
    private readonly string? _contextTraderId;
    private IReadOnlyCollection<string>? _locationIds;
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, QuestTableRowBackgroundVisual> _rowBackgrounds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuestTableRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<QuestTableSortColumn, SortHeader> _sortHeaders = new();
    private readonly List<RectTransform> _sectionHeaders = new();
    private QuestProfileOverlay _overlay;
    private string? _selectedQuestId;
    private bool _disposed;

    private InProgressQuestTableView(
        RectTransform root,
        RectTransform viewport,
        RectTransform scrollViewport,
        RectTransform content,
        RectTransform nativeHostRoot,
        ScrollRect scrollRect,
        QuestGraphTopology topology,
        GlobalQuestGraphProjection projection,
        QuestProfileOverlay overlay,
        IReadOnlyList<QuestTableSortCriterion> sortCriteria,
        bool hideCompletedTasks,
        IReadOnlyCollection<string> expandedQuestIds,
        Action<string> onSelected,
        Action onBackgroundClick,
        Action<QuestTableSortColumn> onSortChanged,
        Action<string> onExpansionChanged,
        ManualLogSource log,
        QuestAssetSpriteCache assetCache,
        GClass3794? favoriteQuestService,
        QuestTrackingService tracking,
        NativeQuestWorkspaceContext workspace,
        Action<string, QuestDetailsActionKind> onQuestMutated,
        QuestTableSectionMode sectionMode,
        string? contextTraderId,
        IReadOnlyCollection<string>? locationIds)
    {
        Root = root;
        _viewport = viewport;
        _scrollViewport = scrollViewport;
        _content = content;
        _nativeHostRoot = nativeHostRoot;
        _scrollRect = scrollRect;
        _topology = topology;
        _projection = projection;
        _overlay = overlay;
        _sortCriteria = sortCriteria;
        _hideCompletedTasks = hideCompletedTasks;
        _expandedQuestIds = expandedQuestIds;
        _onSelected = onSelected;
        _onBackgroundClick = onBackgroundClick;
        _onSortChanged = onSortChanged;
        _onExpansionChanged = onExpansionChanged;
        _log = log;
        AssetCache = assetCache;
        _favoriteQuestService = favoriteQuestService;
        _tracking = tracking;
        _workspace = workspace;
        _skipVisibility = root.gameObject.AddComponent<QuestObjectiveSkipVisibility>();
        _skipVisibility.Bind(workspace.TaskSkippingEnabled, workspace.TaskSkipModifier);
        _onQuestMutated = onQuestMutated;
        _sectionMode = sectionMode;
        _contextTraderId = contextTraderId;
        _locationIds = CopyLocationIds(locationIds);
        _tracking.RefreshFavoriteSnapshot(_overlay.ProfileId, _topology.Nodes, IsFavorite, false);
        if (_favoriteQuestService is not null)
            _favoriteQuestService.OnFavoriteQuestAddedOrRemoved += OnFavoriteQuestAddedOrRemoved;
        _tracking.TrackingChanged += OnTrackingChanged;
        scrollViewport.gameObject.AddComponent<QuestTableBackgroundClickHandler>().Bind(onBackgroundClick);
    }

    public RectTransform Root { get; }
    public int ActiveNodeCount => _projection.Nodes.Count;
    public int CachedRowCount => _rows.Count;
    public Vector2 ViewportSize => _viewport.rect.size;
    public QuestAssetSpriteCache AssetCache { get; }
    private bool ForceExpandedTasks => _sectionMode == QuestTableSectionMode.TraderStatus;
    private bool ShowFavoriteColumn => _sectionMode != QuestTableSectionMode.TraderStatus;

    public static InProgressQuestTableView Create(
        RectTransform mountRect,
        GlobalQuestGraphProjection projection,
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        IReadOnlyList<QuestGraphNode> cacheNodes,
        IReadOnlyList<QuestTableSortCriterion> sortCriteria,
        bool hideCompletedTasks,
        IReadOnlyCollection<string> expandedQuestIds,
        Action<string> onSelected,
        Action onBackgroundClick,
        Action<QuestTableSortColumn> onSortChanged,
        Action<string> onExpansionChanged,
        ManualLogSource log,
        QuestAssetSpriteCache assetCache,
        GClass3794? favoriteQuestService,
        QuestTrackingService tracking,
        NativeQuestWorkspaceContext workspace,
        Action<string, QuestDetailsActionKind> onQuestMutated,
        GraphViewportState? initialViewport = null,
        float headerHeight = GlobalHeaderHeight,
        QuestTableSectionMode sectionMode = QuestTableSectionMode.Global,
        string? contextTraderId = null,
        IReadOnlyCollection<string>? locationIds = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var root = UnityUiFactory.CreateRect("QuestMapGraph", mountRect.parent);
        UnityUiFactory.CopyRect(mountRect, root);
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        root.gameObject.AddComponent<Image>().color = new Color(0.075f, 0.095f, 0.105f, 0.10f);

        var globalHeader = UnityUiFactory.CreateRect("Header", root);
        globalHeader.anchorMin = new Vector2(0, 1);
        globalHeader.anchorMax = new Vector2(1, 1);
        globalHeader.pivot = new Vector2(0.5f, 1);
        globalHeader.offsetMin = new Vector2(0, -headerHeight);
        globalHeader.offsetMax = Vector2.zero;
        globalHeader.gameObject.AddComponent<Image>().color = new Color(0.055f, 0.07f, 0.075f, 0.99f);

        var nativeHostRoot = UnityUiFactory.CreateRect("NativeActionHosts", root);
        UnityUiFactory.Stretch(nativeHostRoot);
        nativeHostRoot.gameObject.SetActive(false);

        var viewport = UnityUiFactory.CreateRect("Viewport", root);
        UnityUiFactory.Stretch(viewport, 8, 8, headerHeight + 4, 8);
        viewport.gameObject.AddComponent<Image>().color = Color.clear;

        var tableHeader = UnityUiFactory.CreateRect("TableHeader", viewport);
        tableHeader.anchorMin = new Vector2(0, 1);
        tableHeader.anchorMax = new Vector2(1, 1);
        tableHeader.pivot = new Vector2(0.5f, 1);
        tableHeader.sizeDelta = new Vector2(0, TableHeaderHeight);
        tableHeader.offsetMax = new Vector2(-14, 0);
        tableHeader.gameObject.AddComponent<Image>().color = new Color(0.105f, 0.12f, 0.125f, 1f);

        var scrollViewport = UnityUiFactory.CreateRect("TableScrollViewport", viewport);
        UnityUiFactory.Stretch(scrollViewport, 0, 14, TableHeaderHeight + 2, 0);
        scrollViewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        scrollViewport.gameObject.AddComponent<RectMask2D>();

        var content = UnityUiFactory.CreateRect("TableContent", scrollViewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        var scrollRect = scrollViewport.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = scrollViewport;
        scrollRect.content = content;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.scrollSensitivity = 42f;

        var scrollbar = CreateScrollbar(viewport);
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scrollRect.verticalScrollbarSpacing = 2;

        var view = new InProgressQuestTableView(root, viewport, scrollViewport, content, nativeHostRoot, scrollRect, topology,
            projection, overlay, sortCriteria, hideCompletedTasks, expandedQuestIds, onSelected, onBackgroundClick, onSortChanged,
            onExpansionChanged, log, assetCache, favoriteQuestService, tracking, workspace,
            onQuestMutated, sectionMode,
            contextTraderId, locationIds);
        view.BuildHeader(tableHeader);
        view.RebuildAllRows(cacheNodes);
        Canvas.ForceUpdateCanvases();
        if (initialViewport.HasValue) view.RestoreViewport(initialViewport.Value);
        stopwatch.Stop();
        QuestMapDebugLog.Info(log,
            "QUESTMAP_M06_TABLE_RENDER " +
            $"rows={view.ActiveNodeCount}; cachedRows={view._rows.Count}; sorts={string.Join(",", sortCriteria.Select(SortToken))}; " +
            $"firstRenderMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
        return view;
    }

    public GraphViewportState CaptureViewportState() => new(1f, _content.anchoredPosition);

    public void RefreshOverlay(QuestProfileOverlay overlay, string? selectedQuestId)
        => RefreshOverlay(overlay, selectedQuestId, _projection);

    public void RefreshOverlay(
        QuestProfileOverlay overlay,
        string? selectedQuestId,
        GlobalQuestGraphProjection projection,
        bool refreshAllNativeControls = true)
    {
        if (_disposed) return;
        var stopwatch = Stopwatch.StartNew();
        var viewport = CaptureViewportState();
        var priorOverlay = _overlay;
        _overlay = overlay;
        _selectedQuestId = selectedQuestId;
        _projection = projection;
        var changedIds = _rows.Keys
            .Where(id => RowStateChanged(priorOverlay, overlay, id))
            .Concat(refreshAllNativeControls
                ? projection.Nodes.Where(HasNativeMutationControls).Select(node => node.Id)
                : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _content.gameObject.SetActive(false);
        try
        {
            foreach (var questId in changedIds) RebuildRow(questId);
            EnsureRows(projection.Nodes);
            LayoutRows();
            RefreshSortHeaders();
        }
        finally
        {
            _content.gameObject.SetActive(true);
        }
        RestoreViewport(viewport);
        stopwatch.Stop();
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_TABLE_LIVE_UPDATE " +
            $"changedRows={changedIds.Length}; rows={ActiveNodeCount}; cachedRows={_rows.Count}; " +
            $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
    }

    public void RefreshQuest(QuestProfileOverlay overlay, string questId)
        => RefreshQuests(overlay, new[] { questId });

    public void RefreshQuests(QuestProfileOverlay overlay, IReadOnlyCollection<string> questIds)
    {
        if (_disposed) return;
        _overlay = overlay;
        var changedIds = questIds
            .Where(_rows.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (changedIds.Length == 0) return;

        var stopwatch = Stopwatch.StartNew();
        var viewport = CaptureViewportState();
        _content.gameObject.SetActive(false);
        try
        {
            foreach (var questId in changedIds) RebuildRow(questId);
            LayoutRows();
            RefreshSortHeaders();
        }
        finally
        {
            _content.gameObject.SetActive(true);
        }
        SetSelected(_selectedQuestId);
        RestoreViewport(viewport);
        stopwatch.Stop();
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_TABLE_QUESTS_UPDATE " +
            $"changedRows={changedIds.Length}; relaidOut=True; " +
            $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
    }

    public void ApplyTopologyDelta(
        QuestGraphTopology topology,
        QuestProfileOverlay overlay,
        GlobalQuestGraphProjection projection,
        IReadOnlyList<QuestGraphNode> cacheNodes,
        string? selectedQuestId)
    {
        if (_disposed) return;
        var stopwatch = Stopwatch.StartNew();
        var viewport = CaptureViewportState();
        var cacheIds = cacheNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var removedIds = _rows.Keys.Where(id => !cacheIds.Contains(id)).ToArray();
        var addedIds = cacheIds.Where(id => !_rows.ContainsKey(id)).ToArray();

        _topology = topology;
        _overlay = overlay;
        _projection = projection;
        _selectedQuestId = selectedQuestId;
        _tracking.RefreshFavoriteSnapshot(_overlay.ProfileId, _topology.Nodes, IsFavorite, true);

        _content.gameObject.SetActive(false);
        try
        {
            foreach (var questId in removedIds)
            {
                var row = _rows[questId];
                row.DisposeNativeActions();
                row.Root.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(row.Root.gameObject);
                _rows.Remove(questId);
                _rowBackgrounds.Remove(questId);
            }
            EnsureRows(cacheNodes);
            LayoutRows();
            RefreshSortHeaders();
        }
        finally
        {
            _content.gameObject.SetActive(true);
        }
        RestoreViewport(viewport);
        stopwatch.Stop();
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_TABLE_TOPOLOGY_DELTA " +
            $"added={addedIds.Length}; removed={removedIds.Length}; rows={ActiveNodeCount}; cachedRows={_rows.Count}; " +
            $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}; fullRebuild=False");
    }

    public void UpdatePresentation(
        GlobalQuestGraphProjection projection,
        bool hideCompletedTasks,
        IReadOnlyCollection<string>? locationIds = null)
    {
        if (_disposed) return;
        var stopwatch = Stopwatch.StartNew();
        var viewport = CaptureViewportState();
        var taskVisibilityChanged = _hideCompletedTasks != hideCompletedTasks
            || !SameLocationIds(_locationIds, locationIds);
        _projection = projection;
        _hideCompletedTasks = hideCompletedTasks;
        _locationIds = CopyLocationIds(locationIds);

        _content.gameObject.SetActive(false);
        try
        {
            EnsureRows(projection.Nodes);
            if (taskVisibilityChanged)
            {
                foreach (var row in _rows.Values) RefreshTasks(row);
            }
            LayoutRows();
            RefreshSortHeaders();
        }
        finally
        {
            _content.gameObject.SetActive(true);
        }
        RestoreViewport(viewport);
        stopwatch.Stop();
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_TABLE_UPDATE " +
            $"rows={ActiveNodeCount}; cachedRows={_rows.Count}; tasksChanged={taskVisibilityChanged}; " +
            $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
    }

    public void RefreshExpansion(string questId)
    {
        if (_disposed || !_rows.TryGetValue(questId, out var row)) return;
        var stopwatch = Stopwatch.StartNew();
        var viewport = CaptureViewportState();
        _content.gameObject.SetActive(false);
        try
        {
            RefreshTasks(row);
            LayoutRows();
        }
        finally
        {
            _content.gameObject.SetActive(true);
        }
        RestoreViewport(viewport);
        stopwatch.Stop();
        QuestMapDebugLog.Info(_log, $"QUESTMAP_M06_TABLE_EXPAND quest={questId}; elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F2}");
    }

    public void SetSelected(string? questId)
    {
        _selectedQuestId = questId;
        foreach (var pair in _rowBackgrounds)
        {
            pair.Value.SetSelected(string.Equals(pair.Key, questId, StringComparison.Ordinal));
        }
    }

    public void RebindFavoriteQuestService(GClass3794 favoriteQuestService)
    {
        if (_disposed || ReferenceEquals(_favoriteQuestService, favoriteQuestService)) return;
        if (_favoriteQuestService is not null)
            _favoriteQuestService.OnFavoriteQuestAddedOrRemoved -= OnFavoriteQuestAddedOrRemoved;
        _favoriteQuestService = favoriteQuestService;
        _favoriteQuestService.OnFavoriteQuestAddedOrRemoved += OnFavoriteQuestAddedOrRemoved;
        _tracking.RefreshFavoriteSnapshot(_overlay.ProfileId, _topology.Nodes, _favoriteQuestService.IsFavorite, true);
        var viewport = CaptureViewportState();
        foreach (var row in _rows.Values)
        {
            if (row.PinVisual is not null) RefreshPinVisual(row.Node.Id, row.PinVisual);
        }
        LayoutRows();
        RestoreViewport(viewport);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_favoriteQuestService is not null)
            _favoriteQuestService.OnFavoriteQuestAddedOrRemoved -= OnFavoriteQuestAddedOrRemoved;
        _tracking.TrackingChanged -= OnTrackingChanged;
        foreach (var row in _rows.Values) row.DisposeNativeActions();
        _rowBackgrounds.Clear();
        _rows.Clear();
        _sortHeaders.Clear();
        _sectionHeaders.Clear();
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    private void BuildHeader(RectTransform header)
    {
        var firstColumn = ShowFavoriteColumn ? 0.03f : 0f;
        if (ShowFavoriteColumn)
        {
            var pinHeader = CreateAnchoredCell("Pin", header, 0f, 0.03f, 1, 1);
            var pinText = UnityUiFactory.AddText(pinHeader.gameObject, "★", 13, TextAlignmentOptions.Center,
                QuestGraphPalette.Selected);
            pinText.fontStyle = FontStyles.Bold;
            AddRightBorder(pinHeader);
        }
        AddSortHeader(header, "Trader", ClientLocale.Text("label.tableTrader"), firstColumn, 0.09f, QuestTableSortColumn.Trader, false);
        AddSortHeader(header, "Quest", ClientLocale.Text("label.tableQuest"), 0.09f, 0.27f, QuestTableSortColumn.Quest);
        AddSortHeader(header, "Location", ClientLocale.Text("label.tableLocation"), 0.27f, 0.39f, QuestTableSortColumn.Location);
        AddSortHeader(header, "Status", ClientLocale.Text("label.tableStatus"), 0.39f, 0.49f, QuestTableSortColumn.Status);
        AddSortHeader(header, "Progress", ClientLocale.Text("label.tableProgress"), 0.49f, 0.58f, QuestTableSortColumn.Progress);
        AddPlainHeader(header, "Tasks", ClientLocale.Text("label.tableTasks"), 0.58f, 1f);
    }

    private void AddSortHeader(
        RectTransform parent,
        string name,
        string label,
        float minimum,
        float maximum,
        QuestTableSortColumn column,
        bool showRightBorder = true)
    {
        var criterionIndex = -1;
        for (var index = 0; index < _sortCriteria.Count; index++)
        {
            if (_sortCriteria[index].Column == column)
            {
                criterionIndex = index;
                break;
            }
        }
        var active = criterionIndex >= 0;
        var criterion = active ? _sortCriteria[criterionIndex] : null;
        var suffix = criterion is null
            ? string.Empty
            : ClientLocale.Format("common.sortOrder",
                ClientLocale.Arg("direction", criterion.Direction == QuestTableSortDirection.Ascending ? "↑" : "↓"),
                ClientLocale.Arg("order", criterionIndex + 1));
        var rect = CreateAnchoredCell(name, parent, minimum, maximum, 1, 1);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : Color.clear);
        var text = UnityUiFactory.AddText(rect.gameObject, label + suffix, 12, TextAlignmentOptions.MidlineLeft, Color.white);
        text.margin = new Vector4(12, 0, 4, 0);
        text.fontStyle = FontStyles.Bold;
        button.onClick.AddListener(() => _onSortChanged(column));
        QuestMapNativeTooltips.Bind(rect.gameObject, () => SortTooltip(column, label));
        _sortHeaders[column] = new SortHeader(label, text, rect.GetComponent<Image>());
        if (showRightBorder) AddRightBorder(rect);
    }

    private static void AddPlainHeader(RectTransform parent, string name, string label, float minimum, float maximum)
    {
        var rect = CreateAnchoredCell(name, parent, minimum, maximum, 1, 1);
        var text = UnityUiFactory.AddText(rect.gameObject, label, 12, TextAlignmentOptions.MidlineLeft, Color.white);
        text.margin = new Vector4(12, 0, 4, 0);
        text.fontStyle = FontStyles.Bold;
    }

    private string SortTooltip(QuestTableSortColumn column, string label)
    {
        var criterion = _sortCriteria.FirstOrDefault(value => value.Column == column);
        var key = criterion is null
            ? "tooltip.sortInactive"
            : criterion.Direction == QuestTableSortDirection.Ascending
                ? "tooltip.sortAscending"
                : "tooltip.sortDescending";
        return ClientLocale.Format(key, ClientLocale.Arg("column", label.ToLowerInvariant()));
    }

    private void RebuildAllRows(IEnumerable<QuestGraphNode> cacheNodes)
    {
        foreach (Transform child in _content) UnityEngine.Object.Destroy(child.gameObject);
        _rowBackgrounds.Clear();
        _rows.Clear();
        _sectionHeaders.Clear();
        EnsureRows(cacheNodes);
        LayoutRows();
    }

    private void EnsureRows(IEnumerable<QuestGraphNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (_rows.ContainsKey(node.Id)) continue;
            try
            {
                var row = BuildRow(node);
                _rows.Add(node.Id, row);
                _rowBackgrounds[node.Id] = row.BackgroundVisual;
                row.Root.gameObject.SetActive(true);
            }
            catch (Exception exception)
            {
                _log.LogError($"QUESTMAP_M06_TABLE_ROW_CREATE_FAILED quest={node.Id}; partialRowRemoved=True; {exception}");
            }
        }
    }

    private void RebuildRow(string questId)
    {
        if (!_rows.TryGetValue(questId, out var current)) return;
        QuestTableRow? replacement = null;
        try
        {
            // Build first and swap second. A native quest collection may still
            // be settling after accept/hand-in; retaining the old complete row
            // prevents a partially constructed banner from escaping into the
            // table if any native control binding fails.
            replacement = BuildRow(current.Node);
            replacement.Root.gameObject.SetActive(false);
            replacement.Root.anchoredPosition = current.Root.anchoredPosition;
            replacement.Root.SetSiblingIndex(current.Root.GetSiblingIndex());

            current.DisposeNativeActions();
            current.Root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(current.Root.gameObject);
            _rows[questId] = replacement;
            _rowBackgrounds[questId] = replacement.BackgroundVisual;
            replacement.Root.gameObject.SetActive(true);
        }
        catch (Exception exception)
        {
            replacement?.DisposeNativeActions();
            if (replacement?.Root != null) UnityEngine.Object.Destroy(replacement.Root.gameObject);
            current.Root.gameObject.SetActive(true);
            _log.LogError($"QUESTMAP_M06_TABLE_ROW_PATCH_FAILED quest={questId}; oldRowPreserved=True; {exception}");
        }
    }

    private static bool RowStateChanged(
        QuestProfileOverlay prior,
        QuestProfileOverlay current,
        string questId)
    {
        prior.QuestsById.TryGetValue(questId, out var oldLive);
        current.QuestsById.TryGetValue(questId, out var newLive);
        if (!LiveStateEquals(oldLive, newLive)) return true;
        if (prior.AuthoritativeDisplayStates.GetValueOrDefault(questId)
            != current.AuthoritativeDisplayStates.GetValueOrDefault(questId)) return true;
        if (prior.AuthoritativeProgressPercentages.GetValueOrDefault(questId)
            != current.AuthoritativeProgressPercentages.GetValueOrDefault(questId)) return true;
        return prior.RepeatableEndTimes.GetValueOrDefault(questId)
            != current.RepeatableEndTimes.GetValueOrDefault(questId);
    }

    private static bool HasNativeMutationControls(QuestGraphNode node) =>
        node.RepeatableKind is "Daily" or "Weekly"
        || node.Objectives.Any(objective =>
            objective.ConditionType is "HandoverItem" or "WeaponAssembly");

    private static bool LiveStateEquals(QuestLiveState? left, QuestLiveState? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        return string.Equals(left.ExactStatus, right.ExactStatus, StringComparison.Ordinal)
            && left.HasLiveQuest == right.HasLiveQuest
            && left.Visible == right.Visible
            && left.ExpirationTime == right.ExpirationTime
            && left.HandoverReady == right.HandoverReady
            && left.Objectives.SequenceEqual(right.Objectives);
    }

    private void LayoutRows()
    {
        foreach (var header in _sectionHeaders)
        {
            if (header == null) continue;
            header.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(header.gameObject);
        }
        _sectionHeaders.Clear();

        var visibleIds = _projection.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var row in _rows.Values) row.Root.gameObject.SetActive(visibleIds.Contains(row.Node.Id));
        var y = 0f;

        if (_sectionMode == QuestTableSectionMode.TraderStatus)
        {
            LayoutTraderStatusSections(ref y);
            _content.sizeDelta = new Vector2(0, Mathf.Max(y, _scrollViewport.rect.height));
            SetSelected(_selectedQuestId);
            return;
        }

        var pinnedIds = _projection.Nodes
            .Where(node => IsFavorite(node.Id))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        var pinned = InProgressQuestTableSorter.SortWithinSection(
            _projection.Nodes.Where(node => pinnedIds.Contains(node.Id)), _topology, _overlay, _sortCriteria);
        var ordered = InProgressQuestTableSorter.Sort(
            _projection.Nodes.Where(node => !pinnedIds.Contains(node.Id)), _topology, _overlay, _sortCriteria);
        LayoutSection(ClientLocale.Text("label.sectionPinned"), pinned, ref y);
        LayoutSection(ClientLocale.Text("label.sectionDaily"), ordered.Where(IsDaily).ToArray(), ref y, showExpiration: true);
        LayoutSection(ClientLocale.Text("label.sectionWeekly"), ordered.Where(IsWeekly).ToArray(), ref y, showExpiration: true);
        LayoutSection(ClientLocale.Text("label.sectionQuests"), ordered.Where(node => !IsDaily(node) && !IsWeekly(node)).ToArray(), ref y);
        _content.sizeDelta = new Vector2(0, Mathf.Max(y, _scrollViewport.rect.height));
        SetSelected(_selectedQuestId);
    }

    private void LayoutTraderStatusSections(ref float y)
    {
        var groups = _projection.Nodes
            .GroupBy(node => TraderSectionRank(QuestGraphRules.ClassifyProfileDisplayState(_topology, node, _overlay)))
            .OrderBy(group => group.Key);
        foreach (var group in groups)
        {
            var nodes = _sortCriteria.Count > 0
                ? InProgressQuestTableSorter.SortWithinSection(group, _topology, _overlay, _sortCriteria)
                : group.OrderByDescending(HasEligibleHandover)
                    .ThenByDescending(node => QuestGraphRules.ResolveProfileProgressPercent(node.Id, _overlay) ?? 0)
                    .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(node => node.NaturalOrder)
                    .ThenBy(node => node.Id, StringComparer.Ordinal)
                    .ToArray();
            LayoutSection(TraderSectionLabel(group.Key), nodes, ref y);
        }
    }

    private bool HasEligibleHandover(QuestGraphNode node) =>
        _rows.TryGetValue(node.Id, out var row) && row.NativeActions.Count > 0;

    private static int TraderSectionRank(QuestMapDisplayStateKind state) => state switch
    {
        QuestMapDisplayStateKind.ReadyToFinish => 0,
        QuestMapDisplayStateKind.Available or QuestMapDisplayStateKind.RestartableFailure => 1,
        QuestMapDisplayStateKind.InProgress => 2,
        _ => 3,
    };

    private static string TraderSectionLabel(int rank) => rank switch
    {
        0 => ClientLocale.Text("label.availableToFinish"),
        1 => ClientLocale.Text("label.availableToStart"),
        2 => ClientLocale.Text("label.inProgress"),
        _ => ClientLocale.Text("label.unavailable"),
    };

    private void LayoutSection(string label, IReadOnlyList<QuestGraphNode> nodes, ref float y, bool showExpiration = false)
    {
        if (nodes.Count == 0) return;
        if (y > 0) y += 10;
        var header = UnityUiFactory.CreateRect($"Section-{_sectionHeaders.Count}", _content);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.anchoredPosition = new Vector2(0, -y);
        header.sizeDelta = new Vector2(0, 28);
        header.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.10f, 0.98f);
        var remaining = showExpiration ? SectionRemaining(nodes) : string.Empty;
        var title = UnityUiFactory.AddText(header.gameObject, ClientLocale.Format("common.sectionCount",
                ClientLocale.Arg("label", label), ClientLocale.Arg("count", nodes.Count), ClientLocale.Arg("remaining", remaining)), 12,
            TextAlignmentOptions.MidlineLeft, new Color(0.88f, 0.84f, 0.72f, 1));
        title.margin = new Vector4(12, 0, 8, 0);
        title.fontStyle = FontStyles.Bold;
        _sectionHeaders.Add(header);
        y += 30;

        foreach (var node in nodes)
        {
            var row = _rows[node.Id];
            row.Root.anchoredPosition = new Vector2(0, -y);
            y += row.Height;
        }
    }

    private QuestTableRow BuildRow(QuestGraphNode node)
    {
        _overlay.QuestsById.TryGetValue(node.Id, out var live);
        var visibleObjectives = VisibleObjectives(node, live);
        var filteredObjectives = LocationFilteredObjectives(node, live);
        var expanded = ForceExpandedTasks || _expandedQuestIds.Contains(node.Id);
        var displayedObjectives = expanded
            ? visibleObjectives
            : visibleObjectives.Take(CollapsedTaskCount).ToArray();
        var hasExpander = !ForceExpandedTasks && visibleObjectives.Count > CollapsedTaskCount;
        var progressById = ObjectiveProgressById(live);
        var displayedTasksHeight = displayedObjectives.Count == 0
            ? CompactTaskRowHeight
            : displayedObjectives.Sum(definition => TaskVisualHeight(progressById.GetValueOrDefault(definition.Id)));
        if (HasFilteredTaskSummary(node, filteredObjectives)) displayedTasksHeight += FilteredTaskSummaryHeight;
        var taskAreaHeight = 18 + displayedTasksHeight + (hasExpander ? 28 : 0);
        var height = Mathf.Max(MinimumRowHeight, taskAreaHeight);
        var row = UnityUiFactory.CreateRect($"Row-{node.Id}", _content);
        row.gameObject.SetActive(false);
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.anchoredPosition = Vector2.zero;
        row.sizeDelta = new Vector2(0, height);
        var background = row.gameObject.AddComponent<Image>();
        background.color = RowUnderlay;
        row.gameObject.AddComponent<QuestTableBackgroundClickHandler>().Bind(_onBackgroundClick);
        var backgroundVisual = row.gameObject.AddComponent<QuestTableRowBackgroundVisual>();
        backgroundVisual.Bind(background, RowUnderlay);
        var nativeActions = new List<NativeQuestHandoverAction>();
        var phase = "quest";
        try
        {
            BuildQuestCell(row, node);
            phase = "location";
            BuildLocationCell(row, node);
            phase = "status";
            var trackingVisual = BuildStatusCell(row, node);
            phase = "progress";
            BuildProgressCell(row, node, live);
            phase = "tasks";
            BuildTasksCell(row, node, visibleObjectives, filteredObjectives, expanded, progressById, nativeActions);
            PinVisual? pinVisual = null;
            if (ShowFavoriteColumn)
            {
                phase = "pin";
                pinVisual = BuildPinCell(row, node);
            }
            return new QuestTableRow(node, row, backgroundVisual, height, pinVisual, trackingVisual, nativeActions);
        }
        catch (Exception exception)
        {
            foreach (var action in nativeActions) action.Dispose();
            row.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(row.gameObject);
            throw new InvalidOperationException($"Failed to build table row during '{phase}'.", exception);
        }
    }

    private void RefreshTasks(QuestTableRow row)
    {
        _overlay.QuestsById.TryGetValue(row.Node.Id, out var live);
        var visibleObjectives = VisibleObjectives(row.Node, live);
        var filteredObjectives = LocationFilteredObjectives(row.Node, live);
        var expanded = ForceExpandedTasks || _expandedQuestIds.Contains(row.Node.Id);
        var displayedObjectives = expanded
            ? visibleObjectives
            : visibleObjectives.Take(CollapsedTaskCount).ToArray();
        var hasExpander = !ForceExpandedTasks && visibleObjectives.Count > CollapsedTaskCount;
        var progressById = ObjectiveProgressById(live);
        var displayedTasksHeight = displayedObjectives.Count == 0
            ? CompactTaskRowHeight
            : displayedObjectives.Sum(definition => TaskVisualHeight(progressById.GetValueOrDefault(definition.Id)));
        if (HasFilteredTaskSummary(row.Node, filteredObjectives)) displayedTasksHeight += FilteredTaskSummaryHeight;
        var taskAreaHeight = 18 + displayedTasksHeight + (hasExpander ? 28 : 0);
        row.Height = Mathf.Max(MinimumRowHeight, taskAreaHeight);
        row.Root.sizeDelta = new Vector2(0, row.Height);

        var oldTasks = row.Root.Find("Tasks") as RectTransform;
        if (oldTasks is not null)
        {
            oldTasks.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(oldTasks.gameObject);
        }
        row.DisposeNativeActions();
        BuildTasksCell(row.Root, row.Node, visibleObjectives, filteredObjectives, expanded, progressById, row.NativeActions);
    }

    private void RefreshSortHeaders()
    {
        foreach (var pair in _sortHeaders)
        {
            var criterionIndex = -1;
            for (var index = 0; index < _sortCriteria.Count; index++)
            {
                if (_sortCriteria[index].Column == pair.Key)
                {
                    criterionIndex = index;
                    break;
                }
            }
            var active = criterionIndex >= 0;
            var suffix = active
                ? ClientLocale.Format("common.sortOrder",
                    ClientLocale.Arg("direction", _sortCriteria[criterionIndex].Direction == QuestTableSortDirection.Ascending ? "↑" : "↓"),
                    ClientLocale.Arg("order", criterionIndex + 1))
                : string.Empty;
            pair.Value.Text.text = pair.Value.Label + suffix;
            pair.Value.Background.color = active ? QuestGraphPalette.ControlActive : Color.clear;
        }
    }

    private void BuildQuestCell(RectTransform row, QuestGraphNode node)
    {
        var cell = CreateAnchoredCell("Quest", row, ShowFavoriteColumn ? 0.03f : 0f, 0.27f, 2, 2);
        var content = CreateTopContentFrame(cell, "QuestContent");
        var button = UnityUiFactory.AddButton(content.gameObject, Color.clear);
        button.onClick.AddListener(() => _onSelected(node.Id));
        AddWidthFittedImage(content, "QuestArt", node.ImageUrl, 0.64f,
            new Color(0.015f, 0.018f, 0.02f, 0.48f));

        var state = QuestGraphRules.ClassifyProfileDisplayState(_topology, node, _overlay);
        var rail = UnityUiFactory.CreateRect("StatusRail", content);
        rail.anchorMin = Vector2.zero;
        rail.anchorMax = new Vector2(0, 1);
        rail.pivot = new Vector2(0, 0.5f);
        rail.sizeDelta = new Vector2(7, 0);
        rail.gameObject.AddComponent<Image>().color = QuestGraphPalette.Status(state);

        var showTraderPortrait = !string.Equals(_contextTraderId, node.TraderId, StringComparison.Ordinal);
        if (showTraderPortrait)
        {
            var portraitRoot = UnityUiFactory.CreateRect("Trader", content);
            portraitRoot.anchorMin = portraitRoot.anchorMax = new Vector2(0, 0.5f);
            portraitRoot.pivot = new Vector2(0, 0.5f);
            portraitRoot.anchoredPosition = new Vector2(16, 0);
            portraitRoot.sizeDelta = new Vector2(52, 52);
            portraitRoot.gameObject.AddComponent<Image>().color = new Color(0.22f, 0.21f, 0.18f, 0.98f);
            var fallback = UnityUiFactory.AddText(portraitRoot.gameObject, UnityUiFactory.Initials(node.TraderName), 14,
                TextAlignmentOptions.Center, new Color(0.94f, 0.88f, 0.68f, 1));
            UnityUiFactory.AddPortrait(portraitRoot, node.TraderImageUrl, fallback, AssetCache);
        }

        var titleRect = UnityUiFactory.CreateRect("Title", content);
        titleRect.anchorMin = new Vector2(0, 0.5f);
        titleRect.anchorMax = new Vector2(1, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.offsetMin = new Vector2(showTraderPortrait ? 80 : 18, -28);
        titleRect.offsetMax = new Vector2(-10, 28);
        var title = UnityUiFactory.AddText(titleRect.gameObject, node.Name, 17, TextAlignmentOptions.MidlineLeft, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = true;
        AddTextBorder(title.gameObject);
        var collectorRoute = _topology.CollectorPathQuestIds.Contains(node.Id);
        var lightkeeperRoute = _topology.LightkeeperPathQuestIds.Contains(node.Id);
        AddQuestRouteStrips(content, collectorRoute, lightkeeperRoute);
        var actionOffset = QuestTableLayoutRules.QuestBannerActionRightOffset(collectorRoute, lightkeeperRoute);

        var canAccept = _workspace.CanAccept(_topology, node.Id);
        var canComplete = _workspace.CanComplete(_topology, node.Id);
        var canReplace = node.RepeatableKind is "Daily" or "Weekly" && _workspace.CanReplace(_topology, node.Id);
        if (node.RepeatableKind is "Daily" or "Weekly")
        {
            var repeatableKind = ClientLocale.Text($"repeatable.{node.RepeatableKind!.ToLowerInvariant()}");
            var repeatableLabel = node.ScavRepeatable
                ? ClientLocale.Format("common.scavRepeatable", ClientLocale.Arg("kind", repeatableKind)).ToUpperInvariant()
                : repeatableKind.ToUpperInvariant();
            var badgeWidth = node.ScavRepeatable ? 92f : 66f;
            var actionCount = (canAccept || canComplete ? 1 : 0) + (canReplace ? 1 : 0);
            var badge = UnityUiFactory.CreateRect("RepeatableBadge", content);
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1, 1);
            badge.anchoredPosition = new Vector2(-actionOffset - actionCount * 34, -8);
            badge.sizeDelta = new Vector2(badgeWidth, QuestTableLayoutRules.RepeatableBadgeHeight);
            badge.gameObject.AddComponent<Image>().color = QuestGraphPalette.ControlActive;
            var badgeText = UnityUiFactory.AddText(badge.gameObject, repeatableLabel, 9,
                TextAlignmentOptions.Center, Color.white);
            badgeText.fontStyle = FontStyles.Bold;
            badgeText.enableWordWrapping = false;
        }
        if (canReplace)
        {
            AddQuestAction(content, "Replace", "↻", actionOffset, button => RunReplace(node.Id, button),
                () => ClientLocale.Format("tooltip.replace", ClientLocale.Arg("quest", node.Name)));
            actionOffset += 34;
        }
        if (canAccept)
            AddQuestAction(content, "Accept", "✓", actionOffset, button => RunAccept(node.Id, button),
                () => ClientLocale.Format(state == QuestMapDisplayStateKind.RestartableFailure ? "tooltip.restart" : "tooltip.accept",
                    ClientLocale.Arg("quest", node.Name)));
        else if (canComplete)
            AddQuestAction(content, "Complete", "→", actionOffset, button => RunComplete(node.Id, button),
                () => ClientLocale.Format("tooltip.turnIn", ClientLocale.Arg("quest", node.Name)));

        AddClickableHoverOutline(content, "QuestHover", bottomOnly: false);
        AddTopRightBorder(cell);
    }

    private static void AddClickableHoverOutline(RectTransform surface, string name, bool bottomOnly)
    {
        var hoverLayer = UnityUiFactory.CreateRect(name, surface);
        UnityUiFactory.Stretch(hoverLayer);
        var bottom = AddClickableHoverBorder(
            hoverLayer, "Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(0, 1), new Vector2(0, 2));
        var hoverBorders = bottomOnly
            ? new[] { bottom }
            : new[]
            {
                AddClickableHoverBorder(hoverLayer, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -1), new Vector2(0, 2)),
                bottom,
                AddClickableHoverBorder(hoverLayer, "Left", Vector2.zero, new Vector2(0, 1), new Vector2(1, 0), new Vector2(2, 0)),
                AddClickableHoverBorder(hoverLayer, "Right", new Vector2(1, 0), Vector2.one, new Vector2(-1, 0), new Vector2(2, 0)),
            };
        surface.gameObject.AddComponent<QuestTableClickableHoverVisual>().Bind(hoverBorders);
    }

    private static Image AddClickableHoverBorder(
        RectTransform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        var border = UnityUiFactory.CreateRect(name, parent);
        border.anchorMin = anchorMin;
        border.anchorMax = anchorMax;
        border.pivot = new Vector2(0.5f, 0.5f);
        border.anchoredPosition = anchoredPosition;
        border.sizeDelta = sizeDelta;
        var image = border.gameObject.AddComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = false;
        return image;
    }

    private PinVisual BuildPinCell(RectTransform row, QuestGraphNode node)
    {
        var cell = CreateAnchoredCell("Pin", row, 0, 0.03f, 2, 2);
        var button = UnityUiFactory.AddButton(cell.gameObject, Color.clear);
        var image = (Image)button.targetGraphic;
        var text = UnityUiFactory.AddText(cell.gameObject, string.Empty, 17, TextAlignmentOptions.Center, Color.white);
        var favoriteQuestService = _favoriteQuestService;
        button.interactable = favoriteQuestService is not null;
        if (favoriteQuestService is not null)
            button.onClick.AddListener(() => favoriteQuestService.ToggleFavorite(node.Id));
        QuestMapNativeTooltips.Bind(cell.gameObject, () => ClientLocale.Format(
            IsFavorite(node.Id) ? "tooltip.unpinQuest" : "tooltip.pinQuest",
            ClientLocale.Arg("quest", node.Name)));
        AddTopRightBorder(cell);
        var visual = new PinVisual(image, text);
        RefreshPinVisual(node.Id, visual);
        return visual;
    }

    private void OnFavoriteQuestAddedOrRemoved()
    {
        if (_disposed) return;
        var viewport = CaptureViewportState();
        _tracking.RefreshFavoriteSnapshot(_overlay.ProfileId, _topology.Nodes, IsFavorite, true);
        foreach (var row in _rows.Values)
        {
            if (row.PinVisual is not null) RefreshPinVisual(row.Node.Id, row.PinVisual);
        }
        LayoutRows();
        RestoreViewport(viewport);
    }

    private void OnTrackingChanged(string? profileId, string? questId)
    {
        if (_disposed || profileId is not null
            && !string.Equals(profileId, _overlay.ProfileId, StringComparison.Ordinal)) return;
        if (questId is not null)
        {
            if (_rows.TryGetValue(questId, out var row)) RefreshTrackingVisual(row.Node, row.TrackingVisual);
            return;
        }
        foreach (var row in _rows.Values) RefreshTrackingVisual(row.Node, row.TrackingVisual);
    }

    private void RefreshPinVisual(string questId, PinVisual visual)
    {
        var pinned = IsFavorite(questId);
        visual.Background.color = pinned ? QuestGraphPalette.ControlActive : CellSurface;
        visual.Text.text = pinned ? "★" : "☆";
        visual.Text.color = pinned ? QuestGraphPalette.Selected : QuestGraphPalette.MutedText;
    }

    private bool IsFavorite(string questId) => _favoriteQuestService?.IsFavorite(questId) == true;

    private void BuildLocationCell(RectTransform row, QuestGraphNode node)
    {
        var cell = CreateAnchoredCell("Location", row, 0.27f, 0.39f, 2, 2);
        var content = CreateTopContentFrame(cell, "LocationContent");
        var maps = QuestMapLocationVisuals.DisplayMaps(node, FallbackLocationBannerUrl);
        QuestMapLocationVisuals.AddArtworkSlices(
            content, maps, FallbackLocationBannerUrl,
            new Color(0.02f, 0.025f, 0.028f, 1f), 0.48f, AssetCache);
        var labelLayer = UnityUiFactory.CreateRect("LocationLabel", content);
        UnityUiFactory.Stretch(labelLayer);
        labelLayer.SetAsLastSibling();
        QuestMapLocationVisuals.AddCompactMapText(
            labelLayer, maps, 4, 13, TextAlignmentOptions.Center, Color.white);
        AddTopRightBorder(cell);
    }

    private TrackingVisual BuildStatusCell(RectTransform row, QuestGraphNode node)
    {
        var cell = CreateAnchoredCell("Status", row, 0.39f, 0.49f, 2, 2);
        var button = UnityUiFactory.AddButton(cell.gameObject, Color.clear);
        button.onClick.AddListener(() => _tracking.ToggleManual(_overlay.ProfileId, node.Id));
        QuestMapNativeTooltips.Bind(cell.gameObject, () => TrackingTooltip(node));
        var content = CreateTopContentFrame(cell, "StatusContent", false);
        content.gameObject.AddComponent<Image>().color = CellSurface;
        var state = QuestGraphRules.ClassifyProfileDisplayState(_topology, node, _overlay);
        var visual = new TrackingVisual(TableStatusLabel(state, node), UnityUiFactory.AddText(content.gameObject, string.Empty, 14,
            TextAlignmentOptions.Center, QuestGraphPalette.Status(state)));
        visual.Text.fontStyle = FontStyles.Bold;
        visual.Text.richText = true;
        visual.Text.lineSpacing = -8;
        RefreshTrackingVisual(node, visual);
        AddClickableHoverOutline(content, "StatusHover", bottomOnly: true);
        AddTopRightBorder(cell);
        return visual;
    }

    private void RefreshTrackingVisual(QuestGraphNode node, TrackingVisual visual)
    {
        var tracking = _tracking.Resolve(_overlay.ProfileId, node);
        visual.Text.text = !tracking.Tracked
            ? visual.Status
            : ClientLocale.Format(tracking.Implicit ? "common.trackedStatusItalic" : "common.trackedStatus",
                ClientLocale.Arg("status", visual.Status), ClientLocale.Arg("tracked", ClientLocale.Text("label.tracked")));
    }

    private string TrackingTooltip(QuestGraphNode node)
    {
        var tracking = _tracking.Resolve(_overlay.ProfileId, node);
        if (!tracking.Tracked)
            return ClientLocale.Format("tooltip.trackQuest", ClientLocale.Arg("quest", node.Name));

        var reason = ImplicitTrackingReason(tracking);
        if (string.IsNullOrEmpty(reason))
            return ClientLocale.Format("tooltip.untrackQuest", ClientLocale.Arg("quest", node.Name));

        return ClientLocale.Format(tracking.Manual ? "tooltip.untrackWithImplicit" : "tooltip.implicitTracking",
            ClientLocale.Arg("quest", node.Name), ClientLocale.Arg("reason", reason));
    }

    private static string ImplicitTrackingReason(QuestTrackingState tracking)
    {
        if (tracking.FavoritePolicy && tracking.MapPolicy)
            return ClientLocale.Text("tracking.reasonFavoriteAndMap");
        if (tracking.FavoritePolicy) return ClientLocale.Text("tracking.reasonFavorite");
        return tracking.MapPolicy ? ClientLocale.Text("tracking.reasonMap") : string.Empty;
    }

    private void BuildProgressCell(RectTransform row, QuestGraphNode node, QuestLiveState? live)
    {
        var cell = CreateAnchoredCell("Progress", row, 0.49f, 0.58f, 2, 2);
        var content = CreateTopContentFrame(cell, "ProgressContent", false);
        content.gameObject.AddComponent<Image>().color = CellSurface;
        var progress = OverallProgress(node, live);
        var labelRect = UnityUiFactory.CreateRect("Value", content);
        UnityUiFactory.Stretch(labelRect, 8, 8, 10, 34);
        var label = UnityUiFactory.AddText(labelRect.gameObject, progress.HasValue
                ? ClientLocale.Format("common.percent", ClientLocale.Arg("percent", progress.Value))
                : "—",
            16, TextAlignmentOptions.Center, Color.white);
        label.fontStyle = FontStyles.Bold;
        if (progress.HasValue) AddProgressBar(content, progress.Value / 100d, 10, 12, QuestGraphPalette.Status(
            QuestGraphRules.ClassifyProfileDisplayState(_topology, node, _overlay)));
        AddTopRightBorder(cell);
    }

    private void BuildTasksCell(
        RectTransform row,
        QuestGraphNode node,
        IReadOnlyList<QuestObjectiveDefinition> visibleObjectives,
        IReadOnlyList<QuestObjectiveDefinition> filteredObjectives,
        bool expanded,
        IReadOnlyDictionary<string, QuestObjectiveProgress> progressById,
        List<NativeQuestHandoverAction> nativeActions)
    {
        var cell = CreateAnchoredCell("Tasks", row, 0.58f, 1f, 2, 2);
        cell.gameObject.AddComponent<Image>().color = CellSurface;
        cell.gameObject.AddComponent<RectMask2D>();
        if (node.Objectives.Count == 0)
        {
            UnityUiFactory.AddText(cell.gameObject, ClientLocale.Text("label.noTaskDetails"), 12, TextAlignmentOptions.Center,
                QuestGraphPalette.MutedText);
            return;
        }

        var hasFilteredTaskSummary = HasFilteredTaskSummary(node, filteredObjectives);
        if (visibleObjectives.Count == 0 && !hasFilteredTaskSummary)
        {
            UnityUiFactory.AddText(cell.gameObject, ClientLocale.Text("label.allTasksCompleted"), 12, TextAlignmentOptions.Center,
                QuestGraphPalette.Completed);
            return;
        }

        var displayedObjectives = expanded
            ? visibleObjectives
            : visibleObjectives.Take(CollapsedTaskCount).ToArray();
        var taskOffset = 8f;
        if (visibleObjectives.Count == 0)
        {
            AddCompactTaskLine(cell, "FilteredTasksComplete", ClientLocale.Text("label.allTasksCompleted"),
                taskOffset, QuestGraphPalette.Completed);
            taskOffset += CompactTaskRowHeight;
        }
        for (var index = 0; index < displayedObjectives.Count; index++)
        {
            var definition = displayedObjectives[index];
            progressById.TryGetValue(definition.Id, out var progress);
            var percent = ObjectivePercent(progress);
            var taskHeight = percent.HasValue ? TaskRowHeight : CompactTaskRowHeight;
            var task = UnityUiFactory.CreateRect($"Task-{definition.Id}", cell);
            task.anchorMin = new Vector2(0, 1);
            task.anchorMax = new Vector2(1, 1);
            task.pivot = new Vector2(0.5f, 1);
            task.anchoredPosition = new Vector2(0, -taskOffset);
            task.sizeDelta = new Vector2(0, taskHeight);
            var value = ObjectivePrefix(progress);
            var suffix = ObjectiveSuffix(progress);
            // Native EFT constructs objective views for the selected quest, not
            // for every task-list row. Resolve exact row eligibility gradually
            // and show no action until the native result is known.
            var handoverCandidate = _workspace.MutationsAllowed()
                && progress?.Complete != true
                && definition.ConditionType is "HandoverItem" or "WeaponAssembly";
            _overlay.QuestsById.TryGetValue(node.Id, out var liveQuest);
            var skipCandidate = _workspace.MutationsAllowed()
                && liveQuest?.ExactStatus == nameof(EQuestStatus.Started)
                && progress?.Complete != true;
            var actionWidth = handoverCandidate ? 70f : 0f;
            var textRect = UnityUiFactory.CreateRect("Text", task);
            UnityUiFactory.Stretch(textRect, 10 + actionWidth, 8, 0, percent.HasValue ? 9 : 0);
            var text = UnityUiFactory.AddText(textRect.gameObject, value + definition.Text + suffix, 11,
                TextAlignmentOptions.MidlineLeft, progress?.Complete == true ? QuestGraphPalette.Completed : Color.white);
            text.enableWordWrapping = false;
            if (percent.HasValue)
                AddProgressBar(task, percent.Value / 100d, 10, 3, progress?.Complete == true
                    ? QuestGraphPalette.Status(QuestMapDisplayStateKind.ReadyToFinish)
                    : QuestGraphPalette.Status(QuestMapDisplayStateKind.InProgress), actionWidth);
            var progressTrack = task.Find("ProgressTrack") as RectTransform;
            RectTransform? actionRect = null;
            Button? actionButton = null;
            TMP_Text? actionText = null;
            if (handoverCandidate)
            {
                actionRect = UnityUiFactory.CreateRect("Handover", task);
                actionRect.anchorMin = new Vector2(0, 0.12f);
                actionRect.anchorMax = new Vector2(0, 0.88f);
                actionRect.pivot = new Vector2(0, 0.5f);
                actionRect.anchoredPosition = new Vector2(5, 0);
                actionRect.sizeDelta = new Vector2(62, 0);
                actionButton = UnityUiFactory.AddButton(actionRect.gameObject, QuestGraphPalette.ControlActive);
                actionButton.interactable = false;
                actionText = UnityUiFactory.AddText(actionRect.gameObject, "…", 8,
                    TextAlignmentOptions.Center, Color.white);
                actionText.fontStyle = FontStyles.Bold;
                QuestMapNativeTooltips.Bind(actionRect.gameObject, () => ClientLocale.Format(
                    actionButton?.interactable == true ? "tooltip.handover" : "tooltip.handoverPending",
                    ClientLocale.Arg("objective", definition.Text)));
            }

            QuestObjectiveSkipSlot? skipSlot = null;
            if (skipCandidate)
            {
                var skipRect = UnityUiFactory.CreateRect("Skip", task);
                skipRect.anchorMin = new Vector2(0, 0.12f);
                skipRect.anchorMax = new Vector2(0, 0.88f);
                skipRect.pivot = new Vector2(0, 0.5f);
                skipRect.anchoredPosition = new Vector2(5, 0);
                skipRect.sizeDelta = new Vector2(62, 0);
                var skipButton = UnityUiFactory.AddButton(skipRect.gameObject, QuestGraphPalette.Failed);
                var skipText = UnityUiFactory.AddText(skipRect.gameObject, ClientLocale.Text("label.skip"), 8,
                    TextAlignmentOptions.Center, Color.white);
                skipText.fontStyle = FontStyles.Bold;
                skipButton.onClick.AddListener(() => RequestObjectiveSkip(node, definition));
                QuestMapNativeTooltips.Bind(skipRect.gameObject, () => ClientLocale.Format("tooltip.skip",
                    ClientLocale.Arg("objective", definition.Text)));
                skipSlot = _skipVisibility.Register(
                    skipRect.gameObject,
                    actionRect?.gameObject,
                    occupied =>
                    {
                        var left = occupied ? 80f : 10f;
                        textRect.offsetMin = new Vector2(left, textRect.offsetMin.y);
                        if (progressTrack != null)
                            progressTrack.offsetMin = new Vector2(left, progressTrack.offsetMin.y);
                    });
            }

            if (handoverCandidate && actionRect is not null && actionButton is not null && actionText is not null)
            {
                NativeQuestTableActions.ResolveHandoverDeferred(
                    task,
                    false,
                    () => !_disposed && task != null,
                    () => _workspace.TryCreateHandover(_nativeHostRoot, node.Id, definition.Id),
                    action =>
                    {
                        if (action is null)
                        {
                            if (skipSlot is not null) skipSlot.SetFallbackAvailable(false);
                            else
                            {
                                actionRect.gameObject.SetActive(false);
                                textRect.offsetMin = new Vector2(10, textRect.offsetMin.y);
                                if (progressTrack != null)
                                    progressTrack.offsetMin = new Vector2(10, progressTrack.offsetMin.y);
                            }
                            return;
                        }

                        nativeActions.Add(action);
                        actionText.text = ClientLocale.Text("label.handIn");
                        actionButton.interactable = true;
                        actionButton.onClick.AddListener(() => RunHandover(node.Id, action, actionButton));
                    },
                    _log,
                    node.Id,
                    definition.Id);
            }
            taskOffset += taskHeight;
        }

        if (hasFilteredTaskSummary)
            AddFilteredTaskSummary(cell, node, filteredObjectives, ref taskOffset);

        if (!ForceExpandedTasks && visibleObjectives.Count > CollapsedTaskCount)
        {
            var expander = UnityUiFactory.CreateRect("TaskExpander", cell);
            expander.anchorMin = new Vector2(0, 1);
            expander.anchorMax = new Vector2(1, 1);
            expander.pivot = new Vector2(0.5f, 1);
            expander.anchoredPosition = new Vector2(0, -taskOffset);
            expander.sizeDelta = new Vector2(0, 23);
            var button = UnityUiFactory.AddButton(expander.gameObject, QuestGraphPalette.Control);
            var remaining = visibleObjectives.Count - displayedObjectives.Count;
            var label = expanded
                ? ClientLocale.Text("label.collapseTasks")
                : ClientLocale.Format("label.showMoreTasks", ClientLocale.Arg("count", remaining));
            var text = UnityUiFactory.AddText(expander.gameObject, label, 10, TextAlignmentOptions.Center, Color.white);
            text.fontStyle = FontStyles.Bold;
            button.onClick.AddListener(() => _onExpansionChanged(node.Id));
            QuestMapNativeTooltips.Bind(expander.gameObject, () => expanded
                ? ClientLocale.Format("tooltip.collapseTasks", ClientLocale.Arg("quest", node.Name))
                : ClientLocale.Format("tooltip.expandTasks", ClientLocale.Arg("count", remaining), ClientLocale.Arg("quest", node.Name)));
        }
    }

    private double? OverallProgress(QuestGraphNode node, QuestLiveState? live)
    {
        if (QuestGraphRules.ClassifyProfileDisplayState(_topology, node, _overlay) == QuestMapDisplayStateKind.ReadyToFinish)
            return 100;
        return QuestGraphRules.ResolveProfileProgressPercent(node.Id, _overlay);
    }

    private static string ObjectivePrefix(QuestObjectiveProgress? progress)
    {
        if (progress is null || progress.Complete || progress.Required == 1) return string.Empty;
        if (progress.Current.HasValue && progress.Required.HasValue)
            return ClientLocale.Format("common.progressPrefix", ClientLocale.Arg("progress",
                ClientLocale.Format("common.progress", ClientLocale.Arg("current", CappedCurrent(progress)),
                    ClientLocale.Arg("required", progress.Required.Value))));
        return string.Empty;
    }

    private static string ObjectiveSuffix(QuestObjectiveProgress? progress) =>
        progress?.Complete == true ? "  ✓" : string.Empty;

    private static double? CappedCurrent(QuestObjectiveProgress progress)
        => QuestProgressRules.CapCurrent(progress.Current, progress.Required);

    private static double? ObjectivePercent(QuestObjectiveProgress? progress)
    {
        if (progress is null) return null;
        if (progress.Complete || progress.Required == 1) return null;
        return progress.ProgressKnown
            ? QuestProgressRules.CalculatePercent(progress.Current, progress.Required)
            : null;
    }

    private IReadOnlyList<QuestObjectiveDefinition> VisibleObjectives(QuestGraphNode node, QuestLiveState? live)
    {
        var mapFiltered = QuestObjectiveMapRules.FilterTableObjectives(node, _locationIds);
        return ApplyCompletedTaskFilter(mapFiltered, live);
    }

    private IReadOnlyList<QuestObjectiveDefinition> LocationFilteredObjectives(QuestGraphNode node, QuestLiveState? live)
    {
        var filtered = QuestObjectiveMapRules.TableObjectivesExcludedByLocationFilter(node, _locationIds)
            .Where(objective => objective.MapIds.Count > 0)
            .ToArray();
        return ApplyCompletedTaskFilter(filtered, live);
    }

    private IReadOnlyList<QuestObjectiveDefinition> ApplyCompletedTaskFilter(
        IReadOnlyList<QuestObjectiveDefinition> objectives,
        QuestLiveState? live)
    {
        if (!_hideCompletedTasks || live is null) return objectives;
        var progressById = live.Objectives.GroupBy(objective => objective.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return objectives
            .Where(definition => !progressById.TryGetValue(definition.Id, out var progress) || !progress.Complete)
            .ToArray();
    }

    private static bool HasFilteredTaskSummary(
        QuestGraphNode node,
        IReadOnlyList<QuestObjectiveDefinition> filteredObjectives) =>
        node.ActualMaps.Count > 1
        && filteredObjectives.Count > 0;

    private static void AddFilteredTaskSummary(
        RectTransform cell,
        QuestGraphNode node,
        IReadOnlyList<QuestObjectiveDefinition> filteredObjectives,
        ref float taskOffset)
    {
        var mapNames = QuestMapLocationVisuals.MapNames(node, filteredObjectives.SelectMany(objective => objective.MapIds));
        if (mapNames.Count == 0) return;
        var maps = string.Join(ClientLocale.Text("common.listSeparator"), mapNames);
        var key = filteredObjectives.Count == 1
            ? "label.filteredTaskSummaryOne"
            : "label.filteredTaskSummaryMany";
        var summaryText = ClientLocale.Format(key,
            ClientLocale.Arg("count", filteredObjectives.Count),
            ClientLocale.Arg("maps", maps));
        var summary = AddCompactTaskLine(cell, "FilteredTaskSummary", summaryText, taskOffset,
            new Color(0.77f, 0.73f, 0.61f, 1f));
        summary.fontStyle = FontStyles.Italic;
        summary.overflowMode = TextOverflowModes.Ellipsis;
        summary.raycastTarget = true;
        QuestMapNativeTooltips.Bind(summary.gameObject, () => summaryText);
        taskOffset += FilteredTaskSummaryHeight;
    }

    private static TMP_Text AddCompactTaskLine(
        RectTransform cell,
        string name,
        string value,
        float offset,
        Color color)
    {
        var line = UnityUiFactory.CreateRect(name, cell);
        line.anchorMin = new Vector2(0, 1);
        line.anchorMax = new Vector2(1, 1);
        line.pivot = new Vector2(0.5f, 1);
        line.anchoredPosition = new Vector2(0, -offset);
        line.sizeDelta = new Vector2(0, CompactTaskRowHeight);
        var text = UnityUiFactory.AddText(line.gameObject, value, 11, TextAlignmentOptions.MidlineLeft, color);
        text.margin = new Vector4(10, 0, 8, 0);
        text.enableWordWrapping = false;
        return text;
    }

    private static IReadOnlyCollection<string>? CopyLocationIds(IReadOnlyCollection<string>? locationIds) =>
        locationIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool SameLocationIds(
        IReadOnlyCollection<string>? left,
        IReadOnlyCollection<string>? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(right);
    }

    private static IReadOnlyDictionary<string, QuestObjectiveProgress> ObjectiveProgressById(QuestLiveState? live) =>
        live?.Objectives.GroupBy(objective => objective.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
        ?? new Dictionary<string, QuestObjectiveProgress>(StringComparer.Ordinal);

    private static float TaskVisualHeight(QuestObjectiveProgress? progress) =>
        ObjectivePercent(progress).HasValue ? TaskRowHeight : CompactTaskRowHeight;

    private static void AddQuestRouteStrips(RectTransform parent, bool collectorRoute, bool lightkeeperRoute)
    {
        var offset = 5f;
        if (collectorRoute)
        {
            AddRouteStrip(parent, "CollectorRoute", QuestGraphPalette.Collector, offset);
            offset += 7f;
        }
        if (lightkeeperRoute)
        {
            AddRouteStrip(parent, "LightkeeperRoute", QuestGraphPalette.Lightkeeper, offset);
        }
    }

    private static void AddRouteStrip(RectTransform parent, string name, Color color, float right)
    {
        var strip = UnityUiFactory.CreateRect(name, parent);
        strip.anchorMin = new Vector2(1, 0.14f);
        strip.anchorMax = new Vector2(1, 0.86f);
        strip.pivot = new Vector2(1, 0.5f);
        strip.anchoredPosition = new Vector2(-right, 0);
        strip.sizeDelta = new Vector2(5, 0);
        strip.gameObject.AddComponent<Image>().color = color;
    }

    private void RequestObjectiveSkip(QuestGraphNode node, QuestObjectiveDefinition objective)
    {
        if (_disposed || !_workspace.TaskSkippingEnabled() || !_workspace.MutationsAllowed()) return;
        var quest = _workspace.FindLiveQuest(node.Id);
        if (quest is null) return;

        NativeQuestObjectiveSkip.ShowConfirmation(
            _workspace.QuestController,
            quest,
            objective.Id,
            node.Name,
            objective.Text,
            () => !_disposed && _workspace.TaskSkippingEnabled() && _workspace.MutationsAllowed(),
            () =>
            {
                if (!_disposed) _onQuestMutated(node.Id, QuestDetailsActionKind.SkipObjective);
            },
            _log);
    }

    private async void RunHandover(string questId, NativeQuestHandoverAction action, Button button)
    {
        if (_disposed || !_workspace.MutationsAllowed() || !button.interactable) return;
        button.interactable = false;
        try
        {
            await action.Execute();
            if (!_disposed) _onQuestMutated(questId, QuestDetailsActionKind.Handover);
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M07_TABLE_HANDOVER_ERROR quest={questId}; {exception}");
        }
        finally
        {
            if (button != null) button.interactable = true;
        }
    }

    private async void RunReplace(string questId, Button button)
    {
        if (_disposed || !_workspace.CanReplace(_topology, questId) || !button.interactable) return;
        button.interactable = false;
        try
        {
            await _workspace.ReplaceAsync(_nativeHostRoot, _topology, questId);
            if (!_disposed) _onQuestMutated(questId, QuestDetailsActionKind.Replace);
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M07_TABLE_REPLACE_ERROR quest={questId}; {exception}");
        }
        finally
        {
            if (button != null) button.interactable = true;
        }
    }

    private async void RunAccept(string questId, Button button)
    {
        if (_disposed || !_workspace.CanAccept(_topology, questId) || !button.interactable) return;
        button.interactable = false;
        try
        {
            await _workspace.AcceptAsync(_nativeHostRoot, _topology, questId);
            if (!_disposed) _onQuestMutated(questId, QuestDetailsActionKind.Accept);
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M07_TABLE_ACCEPT_ERROR quest={questId}; {exception}");
        }
        finally
        {
            if (button != null) button.interactable = true;
        }
    }

    private async void RunComplete(string questId, Button button)
    {
        if (_disposed || !_workspace.CanComplete(_topology, questId) || !button.interactable) return;
        button.interactable = false;
        try
        {
            await _workspace.CompleteAsync(_nativeHostRoot, _topology, questId);
            if (!_disposed) _onQuestMutated(questId, QuestDetailsActionKind.Complete);
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M07_TABLE_COMPLETE_ERROR quest={questId}; {exception}");
        }
        finally
        {
            if (button != null) button.interactable = true;
        }
    }

    private static void AddQuestAction(
        RectTransform parent,
        string name,
        string text,
        float rightOffset,
        Action<Button> onClick,
        Func<string> tooltip)
    {
        var root = UnityUiFactory.CreateRect(name, parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1, 1);
        root.anchoredPosition = new Vector2(-rightOffset, -8);
        root.sizeDelta = new Vector2(28, QuestTableLayoutRules.QuestBannerActionHeight);
        var button = UnityUiFactory.AddButton(root.gameObject, QuestGraphPalette.Control);
        var label = UnityUiFactory.AddText(root.gameObject, text, 16, TextAlignmentOptions.Center, Color.white);
        label.fontStyle = FontStyles.Bold;
        button.onClick.AddListener(() => onClick(button));
        QuestMapNativeTooltips.Bind(root.gameObject, tooltip);
    }

    private void AddWidthFittedImage(
        RectTransform parent,
        string name,
        string? url,
        float alpha,
        Color shadeColor)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var imageRoot = UnityUiFactory.CreateRect(name, parent);
        imageRoot.anchorMin = new Vector2(0, 0.5f);
        imageRoot.anchorMax = new Vector2(1, 0.5f);
        imageRoot.pivot = new Vector2(0.5f, 0.5f);
        imageRoot.offsetMin = Vector2.zero;
        imageRoot.offsetMax = Vector2.zero;
        var image = imageRoot.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = new Color(1, 1, 1, alpha);
        var fitter = imageRoot.gameObject.AddComponent<WidthDrivenAspectImage>();
        var shade = UnityUiFactory.CreateRect("Shade", imageRoot);
        UnityUiFactory.Stretch(shade);
        shade.gameObject.AddComponent<Image>().color = shadeColor;
        imageRoot.gameObject.SetActive(false);
        AssetCache.Request(url, sprite =>
        {
            if (image == null || sprite is null) return;
            image.sprite = sprite;
            image.preserveAspect = false;
            fitter.Bind(parent, sprite.rect.width / sprite.rect.height);
            imageRoot.gameObject.SetActive(true);
        });
    }

    private static void AddProgressBar(
        RectTransform parent,
        double ratio,
        float horizontalInset,
        float bottom,
        Color color,
        float additionalLeftInset = 0)
    {
        var track = UnityUiFactory.CreateRect("ProgressTrack", parent);
        track.anchorMin = new Vector2(0, 0);
        track.anchorMax = new Vector2(1, 0);
        track.pivot = new Vector2(0.5f, 0);
        track.offsetMin = new Vector2(horizontalInset + additionalLeftInset, bottom);
        track.offsetMax = new Vector2(-horizontalInset, bottom + 5);
        track.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.17f, 0.17f, 1);
        var fill = UnityUiFactory.CreateRect("Fill", track);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2((float)Math.Clamp(ratio, 0d, 1d), 1);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fill.gameObject.AddComponent<Image>().color = color;
    }

    private static RectTransform CreateAnchoredCell(
        string name,
        RectTransform parent,
        float minimum,
        float maximum,
        float horizontalInset,
        float verticalInset)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = new Vector2(minimum, 0);
        rect.anchorMax = new Vector2(maximum, 1);
        rect.offsetMin = new Vector2(horizontalInset, verticalInset);
        rect.offsetMax = new Vector2(-horizontalInset, -verticalInset);
        return rect;
    }

    private static RectTransform CreateTopContentFrame(RectTransform parent, string name, bool clip = true)
    {
        var content = UnityUiFactory.CreateRect(name, parent);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0, NonTaskContentHeight);
        if (clip) content.gameObject.AddComponent<RectMask2D>();
        return content;
    }

    private static void AddRightBorder(RectTransform parent)
    {
        var border = UnityUiFactory.CreateRect("Border", parent);
        border.anchorMin = new Vector2(1, 0);
        border.anchorMax = Vector2.one;
        border.pivot = new Vector2(1, 0.5f);
        border.sizeDelta = new Vector2(1, 0);
        border.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
    }

    private static void AddTopRightBorder(RectTransform parent)
    {
        var border = UnityUiFactory.CreateRect("Border", parent);
        border.anchorMin = border.anchorMax = border.pivot = new Vector2(1, 1);
        border.anchoredPosition = Vector2.zero;
        border.sizeDelta = new Vector2(1, NonTaskContentHeight);
        border.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
    }

    private static void AddTextBorder(GameObject target)
    {
        var outline = target.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.98f);
        outline.effectDistance = new Vector2(1.25f, -1.25f);
        outline.useGraphicAlpha = true;
        var shadow = target.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.92f);
        shadow.effectDistance = new Vector2(2, -2);
    }

    private static Scrollbar CreateScrollbar(RectTransform viewport)
    {
        var track = UnityUiFactory.CreateRect("Scrollbar", viewport);
        track.anchorMin = new Vector2(1, 0);
        track.anchorMax = Vector2.one;
        track.pivot = new Vector2(1, 0.5f);
        track.offsetMin = new Vector2(-11, 4);
        track.offsetMax = new Vector2(-2, -TableHeaderHeight - 6);
        var trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(0.10f, 0.11f, 0.11f, 0.85f);
        var slidingArea = UnityUiFactory.CreateRect("SlidingArea", track);
        UnityUiFactory.Stretch(slidingArea, 1, 1, 1, 1);
        var handle = UnityUiFactory.CreateRect("Handle", slidingArea);
        UnityUiFactory.Stretch(handle);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.62f, 0.65f, 0.64f, 0.9f);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        return scrollbar;
    }

    private void RestoreViewport(GraphViewportState state)
    {
        var maximumY = Mathf.Max(0, _content.rect.height - _scrollViewport.rect.height);
        _content.anchoredPosition = new Vector2(0, Mathf.Clamp(state.AnchoredPosition.y, 0, maximumY));
        _scrollRect.StopMovement();
    }

    private string SectionRemaining(IReadOnlyList<QuestGraphNode> nodes)
    {
        var ends = nodes.Select(node => _overlay.RepeatableEndTimes.TryGetValue(node.Id, out var end) ? end : 0)
            .Where(end => end > 0).ToArray();
        if (ends.Length == 0) return string.Empty;
        var seconds = Math.Max(0, ends.Min() - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var days = seconds / 86400;
        var remaining = days > 0
            ? ClientLocale.Format("common.remainingDays", ClientLocale.Arg("days", days),
                ClientLocale.Arg("hours", (seconds % 86400) / 3600), ClientLocale.Arg("minutes", seconds % 3600 / 60))
            : ClientLocale.Format("common.remainingHours", ClientLocale.Arg("hours", seconds / 3600),
                ClientLocale.Arg("minutes", seconds % 3600 / 60));
        return ClientLocale.Format("common.inlineDetailWide", ClientLocale.Arg("detail", remaining));
    }

    private static bool IsDaily(QuestGraphNode node) =>
        string.Equals(node.RepeatableKind, "Daily", StringComparison.OrdinalIgnoreCase);

    private static bool IsWeekly(QuestGraphNode node) =>
        string.Equals(node.RepeatableKind, "Weekly", StringComparison.OrdinalIgnoreCase);

    private static string SortToken(QuestTableSortCriterion criterion) =>
        $"{criterion.Column}:{criterion.Direction}";

    private string TableStatusLabel(QuestMapDisplayStateKind state, QuestGraphNode node)
    {
        var status = state switch
        {
            QuestMapDisplayStateKind.InProgress => ClientLocale.Text("state.inProgress"),
            QuestMapDisplayStateKind.ReadyToFinish => ClientLocale.Text("state.readyToTurnIn"),
            QuestMapDisplayStateKind.RestartableFailure => ClientLocale.Text("state.restartableFailure"),
            _ => QuestGraphCardNodeView.StateLabel(state),
        };
        var requirement = state switch
        {
            QuestMapDisplayStateKind.PrestigeGated => PrestigeGateLabel(node),
            QuestMapDisplayStateKind.LevelGated => LevelGateLabel(node),
            QuestMapDisplayStateKind.TraderGated => TraderGateLabel(node),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(requirement)
            ? status
            : ClientLocale.Format("common.statusRequirement", ClientLocale.Arg("status", status),
                ClientLocale.Arg("requirement", requirement));
    }

    private static string? PrestigeGateLabel(QuestGraphNode node)
    {
        var requirement = node.EffectiveRequirements.FirstOrDefault(value => value.Kind == "PrestigeLevel");
        return requirement is null
            ? null
            : ClientLocale.Format("requirement.prestige", ClientLocale.Arg("operator", RequirementSymbol(requirement.Compare)),
                ClientLocale.Arg("value", FormatRequirementValue(requirement.Value)));
    }

    private string? LevelGateLabel(QuestGraphNode node)
    {
        var requirement = node.EffectiveRequirements.FirstOrDefault(value => value.Kind == "Level"
            && !RequirementSatisfied(_overlay.Level, value));
        return requirement is null ? null : ClientLocale.Format("requirement.level",
            ClientLocale.Arg("level", FormatRequirementValue(requirement.Value)));
    }

    private string? TraderGateLabel(QuestGraphNode node)
    {
        var requirement = node.EffectiveRequirements.FirstOrDefault(value =>
        {
            if (value.Kind is not ("TraderLoyalty" or "TraderStanding") || value.TraderId is null
                || !_overlay.TradersById.TryGetValue(value.TraderId, out var trader) || !trader.Available) return false;
            var actual = value.Kind == "TraderLoyalty" ? trader.LoyaltyLevel : trader.Standing;
            return !RequirementSatisfied(actual, value);
        });
        if (requirement?.TraderId is null) return null;
        var traderName = _topology.TradersById.TryGetValue(requirement.TraderId, out var trader)
            ? trader.Name
            : requirement.TraderId;
        return requirement.Kind == "TraderLoyalty"
            ? ClientLocale.Format("requirement.traderLoyalty", ClientLocale.Arg("trader", traderName),
                ClientLocale.Arg("level", FormatRequirementValue(requirement.Value)))
            : ClientLocale.Format("requirement.traderStanding", ClientLocale.Arg("trader", traderName),
                ClientLocale.Arg("operator", RequirementSymbol(requirement.Compare)),
                ClientLocale.Arg("value", FormatRequirementValue(requirement.Value)));
    }

    private static bool RequirementSatisfied(double actual, QuestRequirement requirement) => requirement.Compare switch
    {
        ">" => actual > requirement.Value,
        ">=" => actual >= requirement.Value,
        "<" => actual < requirement.Value,
        "<=" => actual <= requirement.Value,
        "=" or "==" => Math.Abs(actual - requirement.Value) < 0.0001d,
        "!=" => Math.Abs(actual - requirement.Value) >= 0.0001d,
        _ => actual >= requirement.Value,
    };

    private static string RequirementSymbol(string comparison) => comparison switch
    {
        ">" or ">=" or "<" or "<=" or "!=" => comparison,
        "=" or "==" => "=",
        _ => ">=",
    };

    private static string FormatRequirementValue(double value) => Math.Abs(value - Math.Round(value)) < 0.0001d
        ? Math.Round(value).ToString("0")
        : value.ToString("0.##");

    private sealed class QuestTableRow
    {
        public QuestTableRow(
            QuestGraphNode node,
            RectTransform root,
            QuestTableRowBackgroundVisual backgroundVisual,
            float height,
            PinVisual? pinVisual,
            TrackingVisual trackingVisual,
            List<NativeQuestHandoverAction> nativeActions)
        {
            Node = node;
            Root = root;
            BackgroundVisual = backgroundVisual;
            Height = height;
            PinVisual = pinVisual;
            TrackingVisual = trackingVisual;
            NativeActions = nativeActions;
        }

        public QuestGraphNode Node { get; }
        public RectTransform Root { get; }
        public QuestTableRowBackgroundVisual BackgroundVisual { get; }
        public float Height { get; set; }
        public PinVisual? PinVisual { get; }
        public TrackingVisual TrackingVisual { get; }
        public List<NativeQuestHandoverAction> NativeActions { get; }

        public void DisposeNativeActions()
        {
            foreach (var action in NativeActions) action.Dispose();
            NativeActions.Clear();
        }
    }

    private sealed class TrackingVisual
    {
        public TrackingVisual(string status, TMP_Text text)
        {
            Status = status;
            Text = text;
        }

        public string Status { get; }
        public TMP_Text Text { get; }
    }

    private sealed class PinVisual
    {
        public PinVisual(Image background, TMP_Text text)
        {
            Background = background;
            Text = text;
        }

        public Image Background { get; }
        public TMP_Text Text { get; }
    }

    private sealed class SortHeader
    {
        public SortHeader(string label, TMP_Text text, Image background)
        {
            Label = label;
            Text = text;
            Background = background;
        }

        public string Label { get; }
        public TMP_Text Text { get; }
        public Image Background { get; }
    }
}

internal enum QuestTableSectionMode
{
    Global,
    TraderStatus,
}

internal sealed class QuestTableBackgroundClickHandler : MonoBehaviour, IPointerClickHandler
{
    private Action? _onClick;

    public void Bind(Action onClick) => _onClick = onClick;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging) _onClick?.Invoke();
    }
}

internal sealed class QuestTableRowBackgroundVisual : MonoBehaviour
{
    private Image? _background;
    private Color _normalColor;
    private bool _selected;

    public void Bind(Image background, Color normalColor)
    {
        _background = background;
        _normalColor = normalColor;
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        Refresh();
    }

    private void Refresh()
    {
        if (_background is null) return;
        if (!_selected)
        {
            _background.color = _normalColor;
            return;
        }

        var highlight = QuestGraphPalette.Selected;
        highlight.a = _normalColor.a;
        _background.color = highlight;
    }
}

internal sealed class QuestTableClickableHoverVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Image[] _borders = Array.Empty<Image>();

    public void Bind(Image[] borders)
    {
        _borders = borders;
        SetColor(Color.clear);
    }

    private void OnDisable() => SetColor(Color.clear);

    public void OnPointerEnter(PointerEventData eventData)
    {
        var color = QuestGraphPalette.Selected;
        color.a = 0.9f;
        SetColor(color);
    }

    public void OnPointerExit(PointerEventData eventData) => SetColor(Color.clear);

    private void SetColor(Color color)
    {
        foreach (var border in _borders)
        {
            if (border is not null) border.color = color;
        }
    }
}
