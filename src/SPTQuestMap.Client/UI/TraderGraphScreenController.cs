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

namespace SPTQuestMap.Client.UI;

internal sealed class TraderGraphScreenController : IDisposable
{
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
    private QuestsListView? _vanillaList;
    private QuestView? _nativeQuestView;
    private TraderGraphView? _graphView;
    private ReadonlyFutureQuestView? _futureView;
    private QuestGraphTopology? _topology;
    private QuestProfileOverlay? _overlay;
    private bool _mounted;
    private bool _disposed;

    public TraderGraphScreenController(
        QuestsScreen screen,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        TraderClass trader,
        ManualLogSource log)
    {
        _screen = screen;
        _session = session;
        _inventoryController = inventoryController;
        _questController = questController;
        _trader = trader;
        _log = log;
    }

    public void Mount(
        QuestGraphTopology topology,
        QuestGraphLayout layout,
        QuestProfileOverlay overlay,
        bool forceFailure)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TraderGraphScreenController));
        if (_mounted) throw new InvalidOperationException("The trader graph is already mounted for this screen instance.");
        if (forceFailure) throw new InvalidOperationException("Forced trader graph initialization failure.");

        _vanillaList = (QuestsListView?)QuestsListField.GetValue(_screen)
            ?? throw new InvalidOperationException("QuestsScreen._questsListView was null.");
        _nativeQuestView = (QuestView?)QuestViewField.GetValue(_screen)
            ?? throw new InvalidOperationException("QuestsScreen._questView was null.");
        var vanillaListRect = _vanillaList.transform as RectTransform
            ?? throw new InvalidOperationException("The vanilla quest list has no RectTransform.");
        var nativeDetailRect = _nativeQuestView.transform as RectTransform
            ?? throw new InvalidOperationException("The native quest detail view has no RectTransform.");
        var projection = TraderGraphProjectionBuilder.Build(topology, layout, _trader.Id);

        _topology = topology;
        _overlay = overlay;
        _graphView = TraderGraphView.Create(vanillaListRect, projection, overlay, SelectQuest);
        _futureView = ReadonlyFutureQuestView.Create(nativeDetailRect);
        _vanillaList.gameObject.SetActive(false);
        _mounted = true;
        _log.LogInfo(
            "QUESTMAP_M03_MOUNT " +
            $"screen={_screen.GetInstanceID()}; trader={_trader.Id}; nodes={projection.Nodes.Count}; " +
            $"edges={projection.Edges.Count}; liveNodes={projection.Nodes.Count(node => overlay.QuestsById.TryGetValue(node.Id, out var state) && state.HasLiveQuest)}; " +
            "vanillaListActive=False; nativeDetailRetained=True");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _graphView?.Dispose();
        _graphView = null;
        _futureView?.Destroy();
        _futureView = null;
        foreach (var ownedRoot in _screen.GetComponentsInChildren<RectTransform>(true)
                     .Where(rect => rect.name == "QuestMapTraderGraph" || rect.name == "QuestMapReadonlyFutureDetail")
                     .ToArray())
        {
            UnityEngine.Object.Destroy(ownedRoot.gameObject);
        }
        if (_nativeQuestView != null) _nativeQuestView.gameObject.SetActive(true);
        if (_vanillaList != null) _vanillaList.gameObject.SetActive(true);
        _log.LogInfo(
            "QUESTMAP_M03_DISPOSE " +
            $"screen={(_screen == null ? 0 : _screen.GetInstanceID())}; trader={_trader.Id}; vanillaRestored=True");
    }

    private void SelectQuest(string questId)
    {
        if (_disposed || _nativeQuestView is null || _graphView is null || _topology is null || _overlay is null) return;
        if (!_topology.NodesById.TryGetValue(questId, out var node)) return;

        try
        {
            _graphView.SetSelected(questId);
            var liveQuest = _questController.Quests.FirstOrDefault(quest => string.Equals(quest.Id, questId, StringComparison.Ordinal));
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
}
