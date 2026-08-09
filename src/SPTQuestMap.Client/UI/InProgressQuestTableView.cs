using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using SPTQuestMap.Client.Data;
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
    private readonly Action<QuestTableSortColumn> _onSortChanged;
    private readonly Action<string> _onExpansionChanged;
    private GClass3794? _favoriteQuestService;
    private readonly QuestTrackingService _tracking;
    private readonly Func<bool> _mutationsAllowed;
    private readonly Func<RectTransform, string, string, NativeQuestHandoverAction?> _createHandoverAction;
    private readonly Func<string, bool> _canAccept;
    private readonly Func<RectTransform, string, Task> _acceptQuest;
    private readonly Func<string, bool> _canComplete;
    private readonly Func<RectTransform, string, Task> _completeQuest;
    private readonly Func<string, bool> _canReplace;
    private readonly Func<RectTransform, string, Task> _replaceQuest;
    private readonly Action<string, QuestDetailsActionKind> _onQuestMutated;
    private readonly QuestTableSectionMode _sectionMode;
    private readonly string? _contextTraderId;
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, Outline> _rowOutlines = new(StringComparer.Ordinal);
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
        Func<bool> mutationsAllowed,
        Func<RectTransform, string, string, NativeQuestHandoverAction?> createHandoverAction,
        Func<string, bool> canAccept,
        Func<RectTransform, string, Task> acceptQuest,
        Func<string, bool> canComplete,
        Func<RectTransform, string, Task> completeQuest,
        Func<string, bool> canReplace,
        Func<RectTransform, string, Task> replaceQuest,
        Action<string, QuestDetailsActionKind> onQuestMutated,
        QuestTableSectionMode sectionMode,
        string? contextTraderId)
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
        _onSortChanged = onSortChanged;
        _onExpansionChanged = onExpansionChanged;
        _log = log;
        AssetCache = assetCache;
        _favoriteQuestService = favoriteQuestService;
        _tracking = tracking;
        _mutationsAllowed = mutationsAllowed;
        _createHandoverAction = createHandoverAction;
        _canAccept = canAccept;
        _acceptQuest = acceptQuest;
        _canComplete = canComplete;
        _completeQuest = completeQuest;
        _canReplace = canReplace;
        _replaceQuest = replaceQuest;
        _onQuestMutated = onQuestMutated;
        _sectionMode = sectionMode;
        _contextTraderId = contextTraderId;
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
        Func<bool> mutationsAllowed,
        Func<RectTransform, string, string, NativeQuestHandoverAction?> createHandoverAction,
        Func<string, bool> canAccept,
        Func<RectTransform, string, Task> acceptQuest,
        Func<string, bool> canComplete,
        Func<RectTransform, string, Task> completeQuest,
        Func<string, bool> canReplace,
        Func<RectTransform, string, Task> replaceQuest,
        Action<string, QuestDetailsActionKind> onQuestMutated,
        GraphViewportState? initialViewport = null,
        float headerHeight = GlobalHeaderHeight,
        QuestTableSectionMode sectionMode = QuestTableSectionMode.Global,
        string? contextTraderId = null)
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
            onExpansionChanged, log, assetCache, favoriteQuestService, tracking, mutationsAllowed, createHandoverAction,
            canAccept, acceptQuest, canComplete, completeQuest, canReplace, replaceQuest, onQuestMutated, sectionMode,
            contextTraderId);
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
    {
        if (_disposed) return;
        _overlay = overlay;
        if (!_rows.TryGetValue(questId, out var current)) return;

        var stopwatch = Stopwatch.StartNew();
        var viewport = CaptureViewportState();
        var priorHeight = current.Height;
        var priorPosition = current.Root.anchoredPosition;
        var priorSiblingIndex = current.Root.GetSiblingIndex();
        RebuildRow(questId);
        var replacement = _rows[questId];
        replacement.Root.anchoredPosition = priorPosition;
        replacement.Root.SetSiblingIndex(priorSiblingIndex);

        var sortCanMoveRow = _sectionMode == QuestTableSectionMode.TraderStatus
            || _sortCriteria.Any(criterion => criterion.Column is QuestTableSortColumn.Status or QuestTableSortColumn.Progress);
        if (sortCanMoveRow || !Mathf.Approximately(priorHeight, replacement.Height)) LayoutRows();
        SetSelected(_selectedQuestId);
        RestoreViewport(viewport);
        stopwatch.Stop();
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M06_TABLE_QUEST_UPDATE " +
            $"quest={questId}; relaidOut={sortCanMoveRow || !Mathf.Approximately(priorHeight, replacement.Height)}; " +
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
                _rowOutlines.Remove(questId);
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

    public void UpdatePresentation(GlobalQuestGraphProjection projection, bool hideCompletedTasks)
    {
        if (_disposed) return;
        var stopwatch = Stopwatch.StartNew();
        var viewport = CaptureViewportState();
        var taskVisibilityChanged = _hideCompletedTasks != hideCompletedTasks;
        _projection = projection;
        _hideCompletedTasks = hideCompletedTasks;

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
        foreach (var pair in _rowOutlines)
        {
            pair.Value.enabled = string.Equals(pair.Key, questId, StringComparison.Ordinal);
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
        foreach (var row in _rows.Values) RefreshPinVisual(row.Node.Id, row.PinVisual);
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
        _rowOutlines.Clear();
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
        var pinHeader = CreateAnchoredCell("Pin", header, 0f, 0.03f, 1, 1);
        var pinText = UnityUiFactory.AddText(pinHeader.gameObject, "★", 13, TextAlignmentOptions.Center,
            QuestGraphPalette.Selected);
        pinText.fontStyle = FontStyles.Bold;
        AddRightBorder(pinHeader);
        AddSortHeader(header, "Trader", "TRADER", 0.03f, 0.09f, QuestTableSortColumn.Trader, false);
        AddSortHeader(header, "Quest", "QUEST", 0.09f, 0.27f, QuestTableSortColumn.Quest);
        AddSortHeader(header, "Location", "LOCATION", 0.27f, 0.39f, QuestTableSortColumn.Location);
        AddSortHeader(header, "Status", "STATUS", 0.39f, 0.49f, QuestTableSortColumn.Status);
        AddSortHeader(header, "Progress", "PROGRESS", 0.49f, 0.58f, QuestTableSortColumn.Progress);
        AddPlainHeader(header, "Tasks", "TASKS", 0.58f, 1f);
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
            : $"  {(criterion.Direction == QuestTableSortDirection.Ascending ? "↑" : "↓")} {criterionIndex + 1}";
        var rect = CreateAnchoredCell(name, parent, minimum, maximum, 1, 1);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : Color.clear);
        var text = UnityUiFactory.AddText(rect.gameObject, label + suffix, 12, TextAlignmentOptions.MidlineLeft, Color.white);
        text.margin = new Vector4(12, 0, 4, 0);
        text.fontStyle = FontStyles.Bold;
        button.onClick.AddListener(() => _onSortChanged(column));
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

    private void RebuildAllRows(IEnumerable<QuestGraphNode> cacheNodes)
    {
        foreach (Transform child in _content) UnityEngine.Object.Destroy(child.gameObject);
        _rowOutlines.Clear();
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
                _rowOutlines[node.Id] = row.Outline;
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
            _rowOutlines[questId] = replacement.Outline;
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
        || node.Objectives.Any(objective => objective.ConditionType is "HandoverItem" or "WeaponAssembly");

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
        LayoutSection("PINNED", pinned, ref y);
        LayoutSection("DAILY", ordered.Where(IsDaily).ToArray(), ref y);
        LayoutSection("WEEKLY", ordered.Where(IsWeekly).ToArray(), ref y);
        LayoutSection("QUESTS", ordered.Where(node => !IsDaily(node) && !IsWeekly(node)).ToArray(), ref y);
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
        0 => "AVAILABLE TO FINISH",
        1 => "AVAILABLE TO START",
        2 => "IN PROGRESS",
        _ => "UNAVAILABLE",
    };

    private void LayoutSection(string label, IReadOnlyList<QuestGraphNode> nodes, ref float y)
    {
        if (nodes.Count == 0) return;
        if (y > 0) y += 10;
        var header = UnityUiFactory.CreateRect($"Section-{label}", _content);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.anchoredPosition = new Vector2(0, -y);
        header.sizeDelta = new Vector2(0, 28);
        header.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.10f, 0.98f);
        var remaining = label is "DAILY" or "WEEKLY" ? SectionRemaining(nodes) : string.Empty;
        var title = UnityUiFactory.AddText(header.gameObject, $"{label}  ·  {nodes.Count}{remaining}", 12,
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
        var expanded = ForceExpandedTasks || _expandedQuestIds.Contains(node.Id);
        var displayedObjectives = expanded
            ? visibleObjectives
            : visibleObjectives.Take(CollapsedTaskCount).ToArray();
        var hasExpander = !ForceExpandedTasks && visibleObjectives.Count > CollapsedTaskCount;
        var progressById = ObjectiveProgressById(live);
        var displayedTasksHeight = displayedObjectives.Count == 0
            ? CompactTaskRowHeight
            : displayedObjectives.Sum(definition => TaskVisualHeight(progressById.GetValueOrDefault(definition.Id)));
        var taskAreaHeight = 18 + displayedTasksHeight + (hasExpander ? 28 : 0);
        var height = Mathf.Max(MinimumRowHeight, taskAreaHeight);
        var row = UnityUiFactory.CreateRect($"Row-{node.Id}", _content);
        row.gameObject.SetActive(false);
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.anchoredPosition = Vector2.zero;
        row.sizeDelta = new Vector2(0, height);
        row.gameObject.AddComponent<Image>().color = RowUnderlay;
        row.gameObject.AddComponent<QuestTableRowClickBlocker>();
        var outline = row.gameObject.AddComponent<Outline>();
        outline.effectColor = QuestGraphPalette.Selected;
        outline.effectDistance = new Vector2(2, -2);
        outline.enabled = false;
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
            BuildTasksCell(row, node, visibleObjectives, expanded, progressById, nativeActions);
            phase = "pin";
            var pinVisual = BuildPinCell(row, node);
            return new QuestTableRow(node, row, outline, height, pinVisual, trackingVisual, nativeActions);
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
        var expanded = ForceExpandedTasks || _expandedQuestIds.Contains(row.Node.Id);
        var displayedObjectives = expanded
            ? visibleObjectives
            : visibleObjectives.Take(CollapsedTaskCount).ToArray();
        var hasExpander = !ForceExpandedTasks && visibleObjectives.Count > CollapsedTaskCount;
        var progressById = ObjectiveProgressById(live);
        var displayedTasksHeight = displayedObjectives.Count == 0
            ? CompactTaskRowHeight
            : displayedObjectives.Sum(definition => TaskVisualHeight(progressById.GetValueOrDefault(definition.Id)));
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
        BuildTasksCell(row.Root, row.Node, visibleObjectives, expanded, progressById, row.NativeActions);
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
                ? $"  {(_sortCriteria[criterionIndex].Direction == QuestTableSortDirection.Ascending ? "↑" : "↓")} {criterionIndex + 1}"
                : string.Empty;
            pair.Value.Text.text = pair.Value.Label + suffix;
            pair.Value.Background.color = active ? QuestGraphPalette.ControlActive : Color.clear;
        }
    }

    private void BuildQuestCell(RectTransform row, QuestGraphNode node)
    {
        var cell = CreateAnchoredCell("Quest", row, 0.03f, 0.27f, 2, 2);
        var button = UnityUiFactory.AddButton(cell.gameObject, Color.clear);
        button.onClick.AddListener(() => _onSelected(node.Id));
        var content = CreateTopContentFrame(cell, "QuestContent");
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
        var routeInset = AddQuestRouteStrips(content, node);

        var canAccept = _mutationsAllowed() && _canAccept(node.Id);
        var canComplete = _mutationsAllowed() && _canComplete(node.Id);
        var canReplace = node.RepeatableKind is "Daily" or "Weekly" && _mutationsAllowed() && _canReplace(node.Id);
        if (node.RepeatableKind is "Daily" or "Weekly")
        {
            var actionCount = (canAccept || canComplete ? 1 : 0) + (canReplace ? 1 : 0);
            var badge = UnityUiFactory.CreateRect("RepeatableBadge", content);
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1, 1);
            badge.anchoredPosition = new Vector2(-8 - routeInset - actionCount * 34, -8);
            badge.sizeDelta = new Vector2(66, 19);
            badge.gameObject.AddComponent<Image>().color = QuestGraphPalette.ControlActive;
            var badgeText = UnityUiFactory.AddText(badge.gameObject, node.RepeatableKind!.ToUpperInvariant(), 9,
                TextAlignmentOptions.Center, Color.white);
            badgeText.fontStyle = FontStyles.Bold;
        }
        var actionOffset = 8f + routeInset;
        if (canReplace)
        {
            AddQuestAction(content, "Replace", "↻", actionOffset, button => RunReplace(node.Id, button));
            actionOffset += 34;
        }
        if (canAccept)
            AddQuestAction(content, "Accept", "✓", actionOffset, button => RunAccept(node.Id, button));
        else if (canComplete)
            AddQuestAction(content, "Complete", "→", actionOffset, button => RunComplete(node.Id, button));
        AddTopRightBorder(cell);
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
        foreach (var row in _rows.Values) RefreshPinVisual(row.Node.Id, row.PinVisual);
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
        AddWidthFittedImage(content, "LocationArt", node.Location.BannerImageUrl ?? FallbackLocationBannerUrl, 0.5f,
            new Color(0, 0, 0, 0.48f));
        var labelLayer = UnityUiFactory.CreateRect("LocationLabel", content);
        UnityUiFactory.Stretch(labelLayer);
        labelLayer.SetAsLastSibling();
        var label = UnityUiFactory.AddText(labelLayer.gameObject, node.Location.Any ? "Any" : node.Location.Name ?? node.Location.Id,
            14, TextAlignmentOptions.Center, Color.white);
        label.fontStyle = FontStyles.Bold;
        label.alpha = 1f;
        label.faceColor = new Color32(255, 255, 255, 255);
        AddTextBorder(label.gameObject);
        AddTopRightBorder(cell);
    }

    private TrackingVisual BuildStatusCell(RectTransform row, QuestGraphNode node)
    {
        var cell = CreateAnchoredCell("Status", row, 0.39f, 0.49f, 2, 2);
        var button = UnityUiFactory.AddButton(cell.gameObject, Color.clear);
        button.onClick.AddListener(() => _tracking.ToggleManual(_overlay.ProfileId, node.Id));
        var content = CreateTopContentFrame(cell, "StatusContent", false);
        content.gameObject.AddComponent<Image>().color = CellSurface;
        var state = QuestGraphRules.ClassifyProfileDisplayState(_topology, node, _overlay);
        var visual = new TrackingVisual(TableStatusLabel(state, node), UnityUiFactory.AddText(content.gameObject, string.Empty, 14,
            TextAlignmentOptions.Center, QuestGraphPalette.Status(state)));
        visual.Text.fontStyle = FontStyles.Bold;
        visual.Text.richText = true;
        visual.Text.lineSpacing = -8;
        RefreshTrackingVisual(node, visual);
        AddTopRightBorder(cell);
        return visual;
    }

    private void RefreshTrackingVisual(QuestGraphNode node, TrackingVisual visual)
    {
        var tracking = _tracking.Resolve(_overlay.ProfileId, node);
        visual.Text.text = !tracking.Tracked
            ? visual.Status
            : tracking.Implicit
                ? $"{visual.Status}\n<i>(Tracked)</i>"
                : $"{visual.Status}\n(Tracked)";
    }

    private void BuildProgressCell(RectTransform row, QuestGraphNode node, QuestLiveState? live)
    {
        var cell = CreateAnchoredCell("Progress", row, 0.49f, 0.58f, 2, 2);
        var content = CreateTopContentFrame(cell, "ProgressContent", false);
        content.gameObject.AddComponent<Image>().color = CellSurface;
        var progress = OverallProgress(node, live);
        var labelRect = UnityUiFactory.CreateRect("Value", content);
        UnityUiFactory.Stretch(labelRect, 8, 8, 10, 34);
        var label = UnityUiFactory.AddText(labelRect.gameObject, progress.HasValue ? $"{progress.Value:0.#}%" : "—",
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
        bool expanded,
        IReadOnlyDictionary<string, QuestObjectiveProgress> progressById,
        List<NativeQuestHandoverAction> nativeActions)
    {
        var cell = CreateAnchoredCell("Tasks", row, 0.58f, 1f, 2, 2);
        cell.gameObject.AddComponent<Image>().color = CellSurface;
        cell.gameObject.AddComponent<RectMask2D>();
        if (node.Objectives.Count == 0)
        {
            UnityUiFactory.AddText(cell.gameObject, "No task details available", 12, TextAlignmentOptions.Center,
                QuestGraphPalette.MutedText);
            return;
        }

        if (visibleObjectives.Count == 0)
        {
            UnityUiFactory.AddText(cell.gameObject, "All tasks completed", 12, TextAlignmentOptions.Center,
                QuestGraphPalette.Completed);
            return;
        }

        var displayedObjectives = expanded
            ? visibleObjectives
            : visibleObjectives.Take(CollapsedTaskCount).ToArray();
        var taskOffset = 8f;
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
            NativeQuestHandoverAction? handover = null;
            if (_mutationsAllowed()
                && progress?.Complete != true
                && definition.ConditionType is "HandoverItem" or "WeaponAssembly")
            {
                handover = _createHandoverAction(_nativeHostRoot, node.Id, definition.Id);
                if (handover is not null) nativeActions.Add(handover);
            }
            var actionWidth = handover is null ? 0f : 70f;
            if (handover is not null)
            {
                var actionRect = UnityUiFactory.CreateRect("Handover", task);
                actionRect.anchorMin = new Vector2(0, 0.12f);
                actionRect.anchorMax = new Vector2(0, 0.88f);
                actionRect.pivot = new Vector2(0, 0.5f);
                actionRect.anchoredPosition = new Vector2(5, 0);
                actionRect.sizeDelta = new Vector2(62, 0);
                var actionButton = UnityUiFactory.AddButton(actionRect.gameObject, QuestGraphPalette.ControlActive);
                var actionText = UnityUiFactory.AddText(actionRect.gameObject, "HAND IN", 8,
                    TextAlignmentOptions.Center, Color.white);
                actionText.fontStyle = FontStyles.Bold;
                var captured = handover;
                actionButton.onClick.AddListener(() => RunHandover(node.Id, captured, actionButton));
            }
            var textRect = UnityUiFactory.CreateRect("Text", task);
            UnityUiFactory.Stretch(textRect, 10 + actionWidth, 8, 0, percent.HasValue ? 9 : 0);
            var text = UnityUiFactory.AddText(textRect.gameObject, value + definition.Text + suffix, 11,
                TextAlignmentOptions.MidlineLeft, progress?.Complete == true ? QuestGraphPalette.Completed : Color.white);
            text.enableWordWrapping = false;
            if (percent.HasValue)
                AddProgressBar(task, percent.Value / 100d, 10, 3, progress?.Complete == true
                    ? QuestGraphPalette.Status(QuestMapDisplayStateKind.ReadyToFinish)
                    : QuestGraphPalette.Status(QuestMapDisplayStateKind.InProgress), actionWidth);
            taskOffset += taskHeight;
        }

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
            var label = expanded ? "COLLAPSE TASKS" : $"SHOW {remaining} MORE TASKS";
            var text = UnityUiFactory.AddText(expander.gameObject, label, 10, TextAlignmentOptions.Center, Color.white);
            text.fontStyle = FontStyles.Bold;
            button.onClick.AddListener(() => _onExpansionChanged(node.Id));
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
        if (progress is null || progress.Required == 1) return string.Empty;
        if (progress.Complete)
        {
            var current = CappedCurrent(progress);
            if (progress.Current.HasValue && progress.Required.HasValue)
                return $"{current:0.##} / {progress.Required.Value:0.##}  ";
            return string.Empty;
        }
        if (progress.Current.HasValue && progress.Required.HasValue)
            return $"{CappedCurrent(progress):0.##} / {progress.Required.Value:0.##}  ";
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
        if (!_hideCompletedTasks || live is null) return node.Objectives;
        var progressById = live.Objectives.GroupBy(objective => objective.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return node.Objectives
            .Where(definition => !progressById.TryGetValue(definition.Id, out var progress) || !progress.Complete)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, QuestObjectiveProgress> ObjectiveProgressById(QuestLiveState? live) =>
        live?.Objectives.GroupBy(objective => objective.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
        ?? new Dictionary<string, QuestObjectiveProgress>(StringComparer.Ordinal);

    private static float TaskVisualHeight(QuestObjectiveProgress? progress) =>
        ObjectivePercent(progress).HasValue ? TaskRowHeight : CompactTaskRowHeight;

    private float AddQuestRouteStrips(RectTransform parent, QuestGraphNode node)
    {
        var offset = 5f;
        var count = 0;
        if (_topology.CollectorPathQuestIds.Contains(node.Id))
        {
            AddRouteStrip(parent, "CollectorRoute", QuestGraphPalette.Collector, offset);
            offset += 7f;
            count++;
        }
        if (_topology.LightkeeperPathQuestIds.Contains(node.Id))
        {
            AddRouteStrip(parent, "LightkeeperRoute", QuestGraphPalette.Lightkeeper, offset);
            count++;
        }

        // Route strips own the right edge of the quest banner. Keep state
        // actions and repeatable badges to their left instead of painting a
        // button over the Collector/Lightkeeper identity markers.
        return count == 0 ? 0f : count * 7f + 4f;
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

    private async void RunHandover(string questId, NativeQuestHandoverAction action, Button button)
    {
        if (_disposed || !_mutationsAllowed() || !button.interactable) return;
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
        if (_disposed || !_mutationsAllowed() || !_canReplace(questId) || !button.interactable) return;
        button.interactable = false;
        try
        {
            await _replaceQuest(_nativeHostRoot, questId);
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
        if (_disposed || !_mutationsAllowed() || !_canAccept(questId) || !button.interactable) return;
        button.interactable = false;
        try
        {
            await _acceptQuest(_nativeHostRoot, questId);
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
        if (_disposed || !_mutationsAllowed() || !_canComplete(questId) || !button.interactable) return;
        button.interactable = false;
        try
        {
            await _completeQuest(_nativeHostRoot, questId);
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
        Action<Button> onClick)
    {
        var root = UnityUiFactory.CreateRect(name, parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1, 1);
        root.anchoredPosition = new Vector2(-rightOffset, -8);
        root.sizeDelta = new Vector2(28, 24);
        var button = UnityUiFactory.AddButton(root.gameObject, QuestGraphPalette.Control);
        var label = UnityUiFactory.AddText(root.gameObject, text, 16, TextAlignmentOptions.Center, Color.white);
        label.fontStyle = FontStyles.Bold;
        button.onClick.AddListener(() => onClick(button));
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
            ? $"{days}d {(seconds % 86400) / 3600:00}:{seconds % 3600 / 60:00}"
            : $"{seconds / 3600:00}:{seconds % 3600 / 60:00}";
        return $"  ·  {remaining}";
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
            QuestMapDisplayStateKind.InProgress => "In Progress",
            QuestMapDisplayStateKind.ReadyToFinish => "Ready to Turn In",
            QuestMapDisplayStateKind.RestartableFailure => "Restartable Failure",
            _ => QuestGraphCardNodeView.StateLabel(state),
        };
        var requirement = state switch
        {
            QuestMapDisplayStateKind.LevelGated => LevelGateLabel(node),
            QuestMapDisplayStateKind.TraderGated => TraderGateLabel(node),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(requirement) ? status : $"{status}\n<size=10>{requirement}</size>";
    }

    private string? LevelGateLabel(QuestGraphNode node)
    {
        var requirement = node.EffectiveRequirements.FirstOrDefault(value => value.Kind == "Level"
            && !RequirementSatisfied(_overlay.Level, value));
        return requirement is null ? null : $"Requires level {FormatRequirementValue(requirement.Value)}";
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
            ? $"Requires {traderName} LL{FormatRequirementValue(requirement.Value)}"
            : $"Requires {traderName} rep {RequirementSymbol(requirement.Compare)} {FormatRequirementValue(requirement.Value)}";
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
            Outline outline,
            float height,
            PinVisual pinVisual,
            TrackingVisual trackingVisual,
            List<NativeQuestHandoverAction> nativeActions)
        {
            Node = node;
            Root = root;
            Outline = outline;
            Height = height;
            PinVisual = pinVisual;
            TrackingVisual = trackingVisual;
            NativeActions = nativeActions;
        }

        public QuestGraphNode Node { get; }
        public RectTransform Root { get; }
        public Outline Outline { get; }
        public float Height { get; set; }
        public PinVisual PinVisual { get; }
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

internal sealed class QuestTableRowClickBlocker : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
    }
}
