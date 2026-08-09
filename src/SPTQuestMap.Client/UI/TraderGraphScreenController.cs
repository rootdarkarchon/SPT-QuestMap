using System;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPTQuestMap.Core.Layout;
using SPTQuestMap.Core.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class TraderGraphScreenController : IDisposable
{
    private static readonly FieldInfo QuestsListField = AccessTools.Field(typeof(QuestsScreen), "_questsListView")
        ?? throw new MissingFieldException(typeof(QuestsScreen).FullName, "_questsListView");
    private static readonly FieldInfo QuestViewField = AccessTools.Field(typeof(QuestsScreen), "_questView")
        ?? throw new MissingFieldException(typeof(QuestsScreen).FullName, "_questView");
    private static readonly FieldInfo QuestListContainerField = AccessTools.Field(typeof(QuestsListView), "_questListContainer")
        ?? throw new MissingFieldException(typeof(QuestsListView).FullName, "_questListContainer");

    private readonly QuestsScreen _screen;
    private readonly ISession _session;
    private readonly InventoryController _inventoryController;
    private readonly AbstractQuestControllerClass _questController;
    private readonly TraderClass _trader;
    private readonly ManualLogSource _log;
    private readonly bool _debugLogging;
    private readonly QuestAssetSpriteCache _assetCache;
    private QuestsListView? _vanillaList;
    private QuestView? _nativeQuestView;
    private ScrollRect? _vanillaScroll;
    private RectTransform? _vanillaScrollViewport;
    private bool _vanillaScrollWasEnabled;
    private bool _vanillaViewportWasActive;
    private QuestGraphView? _graphView;
    private ReadonlyFutureQuestView? _futureView;
    private QuestGraphTopology? _topology;
    private QuestProfileOverlay? _overlay;
    private string? _selectedQuestId;
    private string? _viewStateScope;
    private bool _mounted;
    private bool _disposed;

    public TraderGraphScreenController(
        QuestsScreen screen,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        TraderClass trader,
        ManualLogSource log,
        bool debugLogging,
        QuestAssetSpriteCache assetCache)
    {
        _screen = screen;
        _session = session;
        _inventoryController = inventoryController;
        _questController = questController;
        _trader = trader;
        _log = log;
        _debugLogging = debugLogging;
        _assetCache = assetCache;
    }

    public void Mount(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TraderGraphScreenController));
        if (_mounted) throw new InvalidOperationException("The trader graph is already mounted for this screen instance.");
        _vanillaList = (QuestsListView?)QuestsListField.GetValue(_screen)
            ?? throw new InvalidOperationException("QuestsScreen._questsListView was null.");
        _nativeQuestView = (QuestView?)QuestViewField.GetValue(_screen)
            ?? throw new InvalidOperationException("QuestsScreen._questView was null.");
        var questListContainer = (RectTransform?)QuestListContainerField.GetValue(_vanillaList)
            ?? throw new InvalidOperationException("QuestsListView._questListContainer was null.");
        _vanillaScroll = _vanillaList.GetComponentsInChildren<ScrollRect>(true)
            .Where(scroll => ReferenceEquals(scroll.content, questListContainer) || questListContainer.IsChildOf(scroll.transform))
            .OrderByDescending(scroll => ReferenceEquals(scroll.content, questListContainer))
            .FirstOrDefault()
            ?? throw new InvalidOperationException("The vanilla quest list scroll region could not be resolved.");
        _vanillaScrollViewport = _vanillaScroll.viewport
            ?? throw new InvalidOperationException("The vanilla quest list ScrollRect has no viewport.");
        if (ReferenceEquals(_vanillaScrollViewport, _vanillaList.transform))
            throw new InvalidOperationException("Refusing to replace the complete QuestsListView root; native controls must remain active.");
        _vanillaScrollWasEnabled = _vanillaScroll.enabled;
        _vanillaViewportWasActive = _vanillaScrollViewport.gameObject.activeSelf;
        var nativeDetailRect = _nativeQuestView.transform as RectTransform
            ?? throw new InvalidOperationException("The native quest detail view has no RectTransform.");
        var projection = TraderGraphProjectionBuilder.Build(topology, layout, _trader.Id);
        _viewStateScope = QuestGraphViewStateStore.Scope(topology.Version, overlay.ProfileId, $"trader:{_trader.Id}");
        var hasPersistedState = QuestGraphViewStateStore.TryLoad(_viewStateScope, out var persistedState);
        if (hasPersistedState && projection.NodesById.ContainsKey(persistedState.SelectedQuestId ?? string.Empty))
        {
            _selectedQuestId = persistedState.SelectedQuestId;
        }

        _topology = topology;
        _overlay = overlay;
        _graphView = QuestGraphView.Create(
            _vanillaScrollViewport,
            projection.Nodes.Count == 0 ? "QUEST MAP" : $"QUEST MAP  ·  {projection.Nodes[0].TraderName}",
            projection,
            topology,
            overlay,
            SelectQuest,
            PersistCurrentState,
            _log,
            _debugLogging,
            _assetCache,
            hasPersistedState ? persistedState.Viewport : null);
        _futureView = ReadonlyFutureQuestView.Create(nativeDetailRect);
        PlaceGraphBehindNativeDetail();
        _vanillaScroll.enabled = false;
        _vanillaScrollViewport.gameObject.SetActive(false);
        _mounted = true;
        if (_selectedQuestId is not null) SelectQuest(_selectedQuestId);
        _log.LogInfo(
            "QUESTMAP_M03_MOUNT " +
            $"screen={_screen.GetInstanceID()}; trader={_trader.Id}; nodes={projection.Nodes.Count}; " +
            $"edges={projection.Edges.Count}; liveNodes={projection.Nodes.Count(node => overlay.QuestsById.TryGetValue(node.Id, out var state) && state.HasLiveQuest)}; " +
            "vanillaListActive=True; vanillaScrollViewportActive=False; nativeControlsRetained=True; nativeDetailRetained=True");
    }

    public void Dispose()
    {
        if (_disposed) return;
        PersistCurrentState();
        QuestGraphViewStateStore.Flush();
        _disposed = true;
        _graphView?.Dispose();
        _graphView = null;
        _futureView?.Destroy();
        _futureView = null;
        foreach (var ownedRoot in _screen.GetComponentsInChildren<RectTransform>(true)
                     .Where(rect => rect.name == "QuestMapTraderGraph" || rect.name == "QuestMapGraph" || rect.name == "QuestMapReadonlyFutureDetail")
                     .ToArray())
        {
            UnityEngine.Object.Destroy(ownedRoot.gameObject);
        }
        if (_nativeQuestView != null) _nativeQuestView.gameObject.SetActive(true);
        if (_vanillaScroll != null) _vanillaScroll.enabled = _vanillaScrollWasEnabled;
        if (_vanillaScrollViewport != null) _vanillaScrollViewport.gameObject.SetActive(_vanillaViewportWasActive);
        if (_vanillaList != null) _vanillaList.gameObject.SetActive(true);
        _log.LogInfo(
            "QUESTMAP_M03_DISPOSE " +
            $"screen={(_screen == null ? 0 : _screen.GetInstanceID())}; trader={_trader.Id}; vanillaRestored=True");
    }

    public string RefreshOverlay(QuestProfileOverlay overlay)
    {
        if (_disposed || !_mounted || _graphView is null) return "screen-inactive";

        var priorOverlay = _overlay;
        _overlay = overlay;
        _graphView.RefreshOverlay(overlay, _selectedQuestId);
        if (_selectedQuestId is null) return "selection-none";

        var selectedQuestId = _selectedQuestId;
        var wasLive = priorOverlay is not null
            && priorOverlay.QuestsById.TryGetValue(selectedQuestId, out var priorState)
            && priorState.HasLiveQuest;
        var liveQuest = _questController.Quests.FirstOrDefault(
            quest => string.Equals(quest.Id, selectedQuestId, StringComparison.Ordinal));
        if (liveQuest is not null)
        {
            if (!wasLive)
            {
                SelectQuest(selectedQuestId);
                return "selection-promoted-to-native";
            }

            return "selection-preserved-native";
        }

        if (!wasLive) return "selection-preserved-readonly";

        _selectedQuestId = null;
        _graphView.SetSelected(null);
        PersistCurrentState();
        _futureView?.Hide();
        if (_nativeQuestView is not null)
        {
            _nativeQuestView.Close();
            _nativeQuestView.gameObject.SetActive(true);
        }
        return "selection-cleared-live-removed";
    }

    public string RefreshQuest(QuestProfileOverlay overlay, string questId)
    {
        if (_disposed || !_mounted || _graphView is null) return "screen-inactive";
        _overlay = overlay;
        _graphView.RefreshQuest(overlay, questId);
        return "trader-quest-refreshed";
    }

    public string RebuildTopology(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay)
    {
        if (_disposed || !_mounted || _vanillaScrollViewport is null) return "screen-inactive";
        var selectedQuestId = _selectedQuestId;
        var projection = TraderGraphProjectionBuilder.Build(topology, layout, _trader.Id);
        var viewportState = _graphView?.CaptureViewportState();
        PersistCurrentState();

        _graphView?.Dispose();
        _topology = topology;
        _overlay = overlay;
        _viewStateScope = QuestGraphViewStateStore.Scope(topology.Version, overlay.ProfileId, $"trader:{_trader.Id}");
        _graphView = QuestGraphView.Create(
            _vanillaScrollViewport,
            projection.Nodes.Count == 0 ? "QUEST MAP" : $"QUEST MAP  ·  {projection.Nodes[0].TraderName}",
            projection,
            topology,
            overlay,
            SelectQuest,
            PersistCurrentState,
            _log,
            _debugLogging,
            _assetCache,
            viewportState);
        PlaceGraphBehindNativeDetail();
        if (selectedQuestId is not null && projection.NodesById.ContainsKey(selectedQuestId))
        {
            _selectedQuestId = selectedQuestId;
            _graphView.SetSelected(selectedQuestId);
            PersistCurrentState();
            return "selection-preserved-topology-rebuild";
        }

        _selectedQuestId = null;
        _futureView?.Hide();
        if (_nativeQuestView is not null)
        {
            _nativeQuestView.Close();
            _nativeQuestView.gameObject.SetActive(true);
        }
        PersistCurrentState();
        return selectedQuestId is null ? "selection-none" : "selection-cleared-topology-removed";
    }

    public string ApplyRepeatableTopologyDelta(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay)
    {
        if (_disposed || !_mounted || _graphView is null) return "screen-inactive";
        var projection = TraderGraphProjectionBuilder.Build(topology, layout, _trader.Id);
        if (_selectedQuestId is not null && !projection.NodesById.ContainsKey(_selectedQuestId))
        {
            _selectedQuestId = null;
            _futureView?.Hide();
            if (_nativeQuestView is not null)
            {
                _nativeQuestView.Close();
                _nativeQuestView.gameObject.SetActive(true);
            }
        }
        if (!_graphView.ApplyTopologyDelta(projection, topology, overlay, _selectedQuestId))
            return RebuildTopology(topology, layout, overlay);
        _topology = topology;
        _overlay = overlay;
        return "trader-topology-delta-applied";
    }

    private void PlaceGraphBehindNativeDetail()
    {
        if (_graphView is null || _nativeQuestView is null) return;
        var nativeDetailTransform = _nativeQuestView.transform;
        if (!ReferenceEquals(_graphView.Root.parent, nativeDetailTransform.parent)) return;

        _graphView.Root.SetSiblingIndex(nativeDetailTransform.GetSiblingIndex());
        _log.LogInfo(
            "QUESTMAP_M04_GEOMETRY " +
            $"trader={_trader.Id}; graphSibling={_graphView.Root.GetSiblingIndex()}; " +
            $"nativeDetailSibling={nativeDetailTransform.GetSiblingIndex()}; nativeDetailOnTop=True");
    }

    private void SelectQuest(string questId)
    {
        if (_disposed || _nativeQuestView is null || _graphView is null || _topology is null || _overlay is null) return;
        if (!_topology.NodesById.TryGetValue(questId, out var node)) return;

        try
        {
            _graphView.SetSelected(questId);
            _selectedQuestId = questId;
            PersistCurrentState();
            var liveQuest = _questController.Quests.LastOrDefault(quest => string.Equals(quest.Id, questId, StringComparison.Ordinal));
            if (liveQuest is not null)
            {
                _futureView?.Hide();
                _nativeQuestView.Close();
                _nativeQuestView.gameObject.SetActive(true);
                liveQuest.IsViewed = true;
                _nativeQuestView.Show(_session, _inventoryController, _questController, liveQuest, _trader);
                _log.LogInfo($"QUESTMAP_M03_SELECT trader={_trader.Id}; quest={questId}; detail=native; viewed={liveQuest.IsViewed}");
                return;
            }

            _nativeQuestView.Close();
            _nativeQuestView.gameObject.SetActive(false);
            _futureView?.Show(BuildFutureDetails(node));
            _log.LogInfo($"QUESTMAP_M03_SELECT trader={_trader.Id}; quest={questId}; detail=readonly-future; actions=False");
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M03_SELECTION_ERROR trader={_trader.Id}; quest={questId}; {exception}");
            _futureView?.Hide();
            _nativeQuestView.gameObject.SetActive(true);
        }
    }

    private string BuildFutureDetails(QuestGraphNode node)
    {
        var builder = new StringBuilder();
        builder.AppendLine(node.Name.ToUpperInvariant());
        builder.AppendLine($"{node.TraderName}  ·  Locked future  ·  Read-only");
        builder.AppendLine();
        builder.AppendLine(string.IsNullOrWhiteSpace(node.Description) ? "No description is available." : node.Description);

        builder.AppendLine();
        builder.AppendLine("REQUIREMENTS");
        if (node.EffectiveRequirements.Count == 0) builder.AppendLine("None recorded.");
        foreach (var requirement in node.EffectiveRequirements)
        {
            builder.AppendLine($"• {requirement.Kind} {requirement.Compare} {requirement.Value:0.##}" +
                (string.IsNullOrWhiteSpace(requirement.TraderId) ? string.Empty : $" ({requirement.TraderId})"));
        }

        builder.AppendLine();
        builder.AppendLine("OBJECTIVES");
        if (node.Objectives.Count == 0) builder.AppendLine("None recorded.");
        foreach (var objective in node.Objectives.OrderBy(value => value.Index ?? int.MaxValue).ThenBy(value => value.Id, StringComparer.Ordinal))
        {
            builder.AppendLine($"• {objective.Text}" +
                (objective.RequiredValue.HasValue ? $" ({objective.RequiredValue.Value:0.##})" : string.Empty));
        }

        var prerequisites = _topology!.IncomingEdgesByTarget.TryGetValue(node.Id, out var incoming)
            ? incoming.Select(edge => edge.SourceId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        var successors = _topology.OutgoingEdgesBySource.TryGetValue(node.Id, out var outgoing)
            ? outgoing.Select(edge => edge.TargetId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        builder.AppendLine();
        builder.AppendLine("RELATED QUEST IDS");
        builder.AppendLine($"Prerequisites: {(prerequisites.Length == 0 ? "None" : string.Join(", ", prerequisites))}");
        builder.AppendLine($"Direct successors: {(successors.Length == 0 ? "None" : string.Join(", ", successors))}");
        builder.AppendLine();
        builder.AppendLine("This topology-only quest has no live EFT QuestClass. QuestMap does not create one and exposes no actions here.");
        return builder.ToString();
    }

    private void PersistCurrentState()
    {
        if (_graphView is null || string.IsNullOrWhiteSpace(_viewStateScope)) return;
        QuestGraphViewStateStore.Save(_viewStateScope!, _graphView.CaptureViewportState(), _selectedQuestId);
    }
}
