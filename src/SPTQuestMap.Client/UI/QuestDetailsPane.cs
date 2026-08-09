using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestDetailsPane : IDisposable
{
    private const float PaneWidth = 640f;
    private const float HeaderHeight = 166f;
    private const float ActionsHeight = 48f;
    private const float MaximumDescriptionHeight = 152f;
    private const float MaximumObjectivesHeight = 258f;
    private const float MinimumRewardsHeight = 150f;
    private const float SectionGap = 2f;
    private const string FallbackLocationBannerUrl = "/files/banners/norvinskzone.png";
    private const string LightkeeperTraderId = "638f541a29ffd1183d187f57";
    private const string BtrDriverTraderId = "656f0f98d80a697f855d34b1";

    private static readonly FieldInfo RewardListPrefabField = AccessTools.Field(typeof(QuestView), "_rewardListPrefab")
        ?? throw new MissingFieldException(typeof(QuestView).FullName, "_rewardListPrefab");
    private static readonly FieldInfo RewardContainerField = AccessTools.Field(typeof(QuestRewardList), "_container")
        ?? throw new MissingFieldException(typeof(QuestRewardList).FullName, "_container");

    private readonly ISession _session;
    private readonly InventoryController _inventoryController;
    private readonly AbstractQuestControllerClass _questController;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly ManualLogSource _log;
    private readonly Func<bool> _showHiddenRewards;
    private readonly Func<bool> _defaultToSummary;
    private readonly Action<string, QuestDetailsActionKind> _requestQuestRefresh;
    private readonly string? _contextTraderId;
    private readonly float _headerHeight;
    private readonly RectTransform _shadow;
    private readonly RectTransform _nativeHostRoot;
    private readonly NativeQuestViewHost? _actionHost;
    private readonly List<NativeQuestHandoverAction> _objectiveHosts = [];
    private string? _selectedQuestId;
    private DetailTextTab _textTab;
    private QuestGraphTopology? _topology;
    private QuestProfileOverlay? _overlay;
    private bool _disposed;

    private QuestDetailsPane(
        RectTransform root,
        RectTransform shadow,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        QuestAssetSpriteCache assetCache,
        ManualLogSource log,
        Func<bool> showHiddenRewards,
        Func<bool> defaultToSummary,
        Action<string, QuestDetailsActionKind> requestQuestRefresh,
        string? contextTraderId,
        float headerHeightScale)
    {
        Root = root;
        _shadow = shadow;
        _session = session;
        _inventoryController = inventoryController;
        _questController = questController;
        _assetCache = assetCache;
        _log = log;
        _showHiddenRewards = showHiddenRewards;
        _defaultToSummary = defaultToSummary;
        _requestQuestRefresh = requestQuestRefresh;
        _contextTraderId = contextTraderId;
        _headerHeight = HeaderHeight * Mathf.Clamp(headerHeightScale, 0.5f, 1f);
        _nativeHostRoot = UnityUiFactory.CreateRect("NativeActionHosts", root);
        UnityUiFactory.Stretch(_nativeHostRoot);
        _nativeHostRoot.gameObject.SetActive(false);
        _actionHost = NativeQuestViewHost.TryCreate(_nativeHostRoot, session, inventoryController, questController, log);
    }

    public RectTransform Root { get; }

    public static QuestDetailsPane Create(
        RectTransform overlayParent,
        RectTransform viewport,
        ISession session,
        InventoryController inventoryController,
        AbstractQuestControllerClass questController,
        QuestAssetSpriteCache assetCache,
        ManualLogSource log,
        Func<bool> showHiddenRewards,
        Func<bool> defaultToSummary,
        Action<string, QuestDetailsActionKind> requestQuestRefresh,
        string? contextTraderId = null,
        float headerHeightScale = 1f)
    {
        var bounds = BoundsInParent(viewport, overlayParent);
        var width = Mathf.Min(PaneWidth, bounds.width);
        var shadow = UnityUiFactory.CreateRect("QuestDescriptionPane-OuterShadow", overlayParent);
        ApplyLocalRect(shadow, overlayParent,
            Rect.MinMaxRect(bounds.xMax - width - 52f, bounds.yMin, bounds.xMax - width, bounds.yMax));
        shadow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var shadowGradient = shadow.gameObject.AddComponent<QuestMapPanelGradient>();
        shadowGradient.raycastTarget = false;
        shadowGradient.SetColors(Color.clear, new Color(0, 0, 0, 0.84f));

        var root = UnityUiFactory.CreateRect("QuestDescriptionPane", overlayParent);
        ApplyLocalRect(root, overlayParent, Rect.MinMaxRect(bounds.xMax - width, bounds.yMin, bounds.xMax, bounds.yMax));
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var gradient = root.gameObject.AddComponent<QuestMapPanelGradient>();
        gradient.raycastTarget = true;
        gradient.SetVerticalColors(
            new Color(0.025f, 0.030f, 0.033f, 0.995f),
            new Color(0.105f, 0.125f, 0.132f, 0.995f));
        var outline = root.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.22f, 0.25f, 0.25f, 0.95f);
        outline.effectDistance = new Vector2(-1, 0);
        var pane = new QuestDetailsPane(
            root, shadow, session, inventoryController, questController, assetCache, log,
            showHiddenRewards, defaultToSummary, requestQuestRefresh, contextTraderId, headerHeightScale);
        shadow.gameObject.SetActive(false);
        root.gameObject.SetActive(false);
        return pane;
    }

    public void Show(QuestGraphTopology topology, QuestProfileOverlay overlay, string questId)
    {
        if (_disposed || !topology.NodesById.TryGetValue(questId, out var node)) return;
        var changedSelection = !string.Equals(_selectedQuestId, questId, StringComparison.Ordinal);
        _selectedQuestId = questId;
        _topology = topology;
        _overlay = overlay;
        if (changedSelection)
        {
            _textTab = _defaultToSummary() && !string.IsNullOrWhiteSpace(node.Summary)
                ? DetailTextTab.Summary
                : DetailTextTab.Description;
        }
        try
        {
            Rebuild(node);
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M07_DETAILS_ERROR quest={questId}; {exception}");
            BuildFailure(node);
        }
    }

    public void ShowRoot()
    {
        if (_disposed || _selectedQuestId is null) return;
        _shadow.gameObject.SetActive(true);
        Root.gameObject.SetActive(true);
        _shadow.SetAsLastSibling();
        Root.SetAsLastSibling();
    }

    public void Hide()
    {
        if (_disposed) return;
        if (_shadow != null) _shadow.gameObject.SetActive(false);
        if (Root != null) Root.gameObject.SetActive(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearNativeObjectiveHosts();
        _actionHost?.Dispose();
        if (Root != null)
        {
            Root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(Root.gameObject);
        }
        if (_shadow != null) UnityEngine.Object.Destroy(_shadow.gameObject);
    }

    private void Rebuild(QuestGraphNode node)
    {
        ClearNativeObjectiveHosts();
        foreach (Transform child in Root)
        {
            if (ReferenceEquals(child, _nativeHostRoot)) continue;
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }

        _overlay!.QuestsById.TryGetValue(node.Id, out var liveState);
        var liveQuest = liveState?.HasLiveQuest == true
            ? _questController.Quests.LastOrDefault(quest => string.Equals(quest.Id, node.Id, StringComparison.Ordinal))
            : null;
        var future = liveQuest is null;
        var raidOnlyTrader = IsRaidOnlyTrader(node.TraderId);
        var actionBound = QuestMutationsAllowed() && liveQuest is not null && !raidOnlyTrader
            && _actionHost?.Bind(liveQuest, node.TraderId) == true;
        var descriptionHeight = DesiredDescriptionHeight(node);
        var objectivesHeight = DesiredObjectivesHeight(node, liveState);
        var sectionBudget = Mathf.Max(0f,
            Root.rect.height - _headerHeight - ActionsHeight - SectionGap * 4f - MinimumRewardsHeight);
        if (descriptionHeight + objectivesHeight > sectionBudget)
        {
            var overflow = descriptionHeight + objectivesHeight - sectionBudget;
            var objectiveReduction = Mathf.Min(overflow, objectivesHeight - 90f);
            objectivesHeight -= objectiveReduction;
            overflow -= objectiveReduction;
            descriptionHeight -= Mathf.Min(overflow, descriptionHeight - 72f);
        }

        var y = 0f;
        BuildHeader(node, y, _headerHeight);
        y += _headerHeight;
        AddDivider(y);
        y += SectionGap;
        BuildActions(node, liveQuest, actionBound, raidOnlyTrader, y, ActionsHeight);
        y += ActionsHeight;
        AddDivider(y);
        y += SectionGap;
        BuildDescription(node, y, descriptionHeight);
        y += descriptionHeight;
        AddDivider(y);
        y += SectionGap;
        BuildObjectives(node, liveState, liveQuest, future, y, objectivesHeight);
        y += objectivesHeight;
        AddDivider(y);
        y += SectionGap;
        BuildRewards(node, liveQuest, future, y, Mathf.Max(120f, Root.rect.height - y));
        _nativeHostRoot.SetAsLastSibling();
        QuestMapDebugLog.Info(_log,
            "QUESTMAP_M07_DETAILS " +
            $"quest={node.Id}; live={!future}; actionBound={actionBound}; raidOnlyTrader={raidOnlyTrader}; objectives={node.Objectives.Count}; " +
            $"rewards={node.Rewards.Count}; summary={!string.IsNullOrWhiteSpace(node.Summary)}; hiddenRewards={_showHiddenRewards()}; " +
            $"descriptionHeight={descriptionHeight:0.#}; objectivesHeight={objectivesHeight:0.#}; rewardsHeight={Mathf.Max(MinimumRewardsHeight, Root.rect.height - y):0.#}");
    }

    private float DesiredDescriptionHeight(QuestGraphNode node)
    {
        var hasSummary = !string.IsNullOrWhiteSpace(node.Summary);
        var text = _textTab == DetailTextTab.Summary && hasSummary ? node.Summary : node.Description;
        var lines = EstimateWrappedLines(text, 78);
        var tabs = hasSummary ? 34f : 0f;
        return Mathf.Clamp(tabs + 20f + lines * 18.9f, hasSummary ? 92f : 72f, MaximumDescriptionHeight);
    }

    private static float DesiredObjectivesHeight(QuestGraphNode node, QuestLiveState? liveState)
    {
        var progressById = (liveState?.Objectives ?? [])
            .GroupBy(value => value.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var rows = 0f;
        for (var index = 0; index < node.Objectives.Count; index++)
        {
            var objective = node.Objectives[index];
            progressById.TryGetValue(objective.Id, out var progress);
            rows += ObjectiveRowHeight(objective, progress);
            if (index < node.Objectives.Count - 1) rows += 3f;
        }
        if (node.Objectives.Count == 0) rows = 42f;
        return Mathf.Clamp(42f + rows, 90f, MaximumObjectivesHeight);
    }

    private static float ObjectiveRowHeight(QuestObjectiveDefinition objective, QuestObjectiveProgress? progress)
    {
        var height = 42f + (EstimateWrappedLines(objective.Text, 72) - 1) * 15f;
        var numeric = progress is { ProgressKnown: true, Complete: false }
            && (progress.Required ?? objective.RequiredValue) is > 1d;
        return height + (numeric ? 7f : 0f);
    }

    private static int EstimateWrappedLines(string? text, int charactersPerLine)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;
        return TextForMeasurement(text).Replace("\r", string.Empty)
            .Split('\n')
            .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (double)charactersPerLine)));
    }

    private static string TextForMeasurement(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '<')
            {
                result.Append(text[index]);
                continue;
            }

            var close = text.IndexOf('>', index + 1);
            if (close < 0)
            {
                result.Append(text[index]);
                continue;
            }

            var tag = text.AsSpan(index + 1, close - index - 1).TrimStart();
            if (tag.StartsWith("br".AsSpan(), StringComparison.OrdinalIgnoreCase)) result.Append('\n');
            index = close;
        }
        return result.ToString();
    }

    private void BuildHeader(QuestGraphNode node, float top, float height)
    {
        var header = TopRect("Header", top, height);
        var questBanner = FractionRect("QuestBanner", header, 0f, 0.67f);
        var locationBanner = FractionRect("LocationBanner", header, 0.67f, 1f);
        AddArtwork(questBanner, node.ImageUrl, new Color(0.02f, 0.025f, 0.028f, 1f), 0.42f);
        AddArtwork(locationBanner, LocationBannerUrl(node.Location),
            new Color(0.02f, 0.025f, 0.028f, 1f), 0.48f);

        if (!string.Equals(_contextTraderId, node.TraderId, StringComparison.Ordinal))
        {
            var portrait = UnityUiFactory.CreateRect("TraderPortrait", questBanner);
            portrait.anchorMin = portrait.anchorMax = portrait.pivot = new Vector2(0, 1);
            portrait.anchoredPosition = new Vector2(10, -10);
            portrait.sizeDelta = new Vector2(54, 54);
            portrait.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.16f, 0.14f, 0.96f);
            var fallback = UnityUiFactory.AddText(portrait.gameObject, UnityUiFactory.Initials(node.TraderName), 14,
                TextAlignmentOptions.Center, new Color(0.95f, 0.85f, 0.58f, 1f));
            UnityUiFactory.AddPortrait(portrait, node.TraderImageUrl, fallback, _assetCache);
        }

        var title = UnityUiFactory.AddText(questBanner.gameObject, node.Name, 22,
            TextAlignmentOptions.BottomLeft, Color.white);
        var collector = _topology?.CollectorPathQuestIds.Contains(node.Id) == true;
        var lightkeeper = _topology?.LightkeeperPathQuestIds.Contains(node.Id) == true;
        title.margin = new Vector4(12, 8, 12, collector || lightkeeper ? 22 : 8);
        title.fontStyle = FontStyles.Bold;
        AddTextShadow(title);
        AddRouteBar(questBanner, collector, lightkeeper);
        if (node.ScavRepeatable) AddScavBadge(questBanner);

        var locationName = node.Location.Any ? "Any" : node.Location.Name ?? node.Location.Id;
        var location = UnityUiFactory.AddText(locationBanner.gameObject, locationName, 16,
            TextAlignmentOptions.Center, Color.white);
        location.fontStyle = FontStyles.Bold;
        AddTextShadow(location);
    }

    private void BuildActions(
        QuestGraphNode node,
        QuestClass? liveQuest,
        bool actionBound,
        bool raidOnlyTrader,
        float top,
        float height)
    {
        var area = TopRect("Actions", top, height);
        if (!QuestMutationsAllowed())
        {
            var disabled = UnityUiFactory.AddText(area.gameObject, "QUEST ACTIONS DISABLED IN RAID", 12,
                TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
            disabled.fontStyle = FontStyles.Bold;
            return;
        }
        if (liveQuest is null || !actionBound)
        {
            var status = QuestGraphCardNodeView.StateLabel(
                QuestGraphRules.ClassifyProfileDisplayState(_topology!, node, _overlay!));
            if (raidOnlyTrader) status += " · IN-RAID QUEST GIVER";
            var text = UnityUiFactory.AddText(area.gameObject, status.ToUpperInvariant(), 12,
                TextAlignmentOptions.Center, new Color(0.55f, 0.57f, 0.57f, 1f));
            text.fontStyle = FontStyles.Bold;
            return;
        }

        var actions = new List<(string Label, QuestDetailsActionKind Kind, Func<Task> Action)>();
        switch (liveQuest.QuestStatus)
        {
            case EQuestStatus.AvailableForStart:
                actions.Add(("ACCEPT", QuestDetailsActionKind.Accept, () => _actionHost!.Accept(liveQuest)));
                break;
            case EQuestStatus.FailRestartable:
                actions.Add(("RESTART", QuestDetailsActionKind.Restart, () => _actionHost!.Accept(liveQuest)));
                break;
            case EQuestStatus.AvailableForFinish:
                // Tarkov's quest-level FinishQuest path is the native TURN IN
                // operation. Objective HAND OVER remains the separate
                // QuestObjectiveView/HandoverItem path below.
                actions.Add(("TURN IN", QuestDetailsActionKind.Complete, () => _actionHost!.Complete(liveQuest)));
                break;
        }
        if (liveQuest.IsChangeAllowed)
            actions.Add(("REPLACE", QuestDetailsActionKind.Replace, () => _actionHost!.Replace()));

        if (actions.Count == 0)
        {
            var state = UnityUiFactory.AddText(area.gameObject, QuestGraphCardNodeView.StateLabel(
                    QuestGraphRules.ClassifyProfileDisplayState(_topology!, node, _overlay!)).ToUpperInvariant(),
                12, TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
            state.fontStyle = FontStyles.Bold;
            return;
        }

        const float gap = 8f;
        var width = Mathf.Min(174f, (area.rect.width - 20 - gap * (actions.Count - 1)) / actions.Count);
        var total = width * actions.Count + gap * (actions.Count - 1);
        var x = (area.rect.width - total) * 0.5f;
        foreach (var action in actions)
        {
            var rect = UnityUiFactory.CreateRect(action.Label, area);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0);
            rect.sizeDelta = new Vector2(width, 32);
            var button = UnityUiFactory.AddButton(rect.gameObject, QuestGraphPalette.Control);
            var label = UnityUiFactory.AddText(rect.gameObject, action.Label, 12, TextAlignmentOptions.Center, Color.white);
            label.fontStyle = FontStyles.Bold;
            button.onClick.AddListener(() => RunNativeAction(action.Action, action.Kind, button));
            x += width + gap;
        }
    }

    private void BuildDescription(QuestGraphNode node, float top, float height)
    {
        var section = TopRect("DescriptionSection", top, height);
        var hasSummary = !string.IsNullOrWhiteSpace(node.Summary);
        var tabHeight = hasSummary ? 30f : 0f;
        if (hasSummary)
        {
            AddTab(section, "DescriptionTab", "DESCRIPTION", 0, 0.5f, _textTab == DetailTextTab.Description,
                () => SelectTextTab(DetailTextTab.Description));
            AddTab(section, "SummaryTab", "SUMMARY", 0.5f, 1f, _textTab == DetailTextTab.Summary,
                () => SelectTextTab(DetailTextTab.Summary));
        }
        var text = _textTab == DetailTextTab.Summary && hasSummary ? node.Summary! : node.Description;
        CreateTextScroll(section, "DescriptionViewport", text, tabHeight + 4, 8, 14,
            new Color(0.88f, 0.88f, 0.82f, 1f));
    }

    private void SelectTextTab(DetailTextTab tab)
    {
        if (_disposed || _selectedQuestId is null || _topology is null || _overlay is null || _textTab == tab) return;
        _textTab = tab;
        Show(_topology, _overlay, _selectedQuestId);
    }

    private void BuildObjectives(
        QuestGraphNode node,
        QuestLiveState? liveState,
        QuestClass? liveQuest,
        bool future,
        float top,
        float height)
    {
        var section = TopRect("ObjectivesSection", top, height);
        var progress = liveState is null ? null : QuestGraphRules.CalculateObjectiveProgressPercent(liveState.Objectives);
        var displayState = QuestGraphRules.ClassifyProfileDisplayState(_topology!, node, _overlay!);
        if (displayState is QuestMapDisplayStateKind.ReadyToFinish or QuestMapDisplayStateKind.Completed) progress = 100d;
        var title = UnityUiFactory.AddText(section.gameObject,
            progress.HasValue ? $"TASKS  ·  OVERALL {progress.Value:0.#}%" : "TASKS",
            14, TextAlignmentOptions.TopLeft, Color.white);
        title.margin = new Vector4(10, 5, 10, 0);
        title.fontStyle = FontStyles.Bold;
        if (progress.HasValue) AddProgressBar(section, progress.Value / 100d, 10, height - 31, QuestGraphPalette.Status(displayState));

        var viewport = CreateScrollViewport(section, "ObjectivesViewport", 38, 4, out var content);
        var progressById = (liveState?.Objectives ?? [])
            .GroupBy(value => value.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var conditions = LiveConditions(liveQuest);
        var y = 0f;
        var orderedObjectives = node.Objectives
            .OrderBy(value => value.Index ?? int.MaxValue)
            .ThenBy(value => value.Id, StringComparer.Ordinal)
            .ToArray();
        for (var index = 0; index < orderedObjectives.Length; index++)
        {
            var objective = orderedObjectives[index];
            progressById.TryGetValue(objective.Id, out var objectiveProgress);
            conditions.TryGetValue(objective.Id, out var condition);
            var rowHeight = ObjectiveRowHeight(objective, objectiveProgress);
            var numeric = objectiveProgress is { ProgressKnown: true }
                && (objectiveProgress.Required ?? objective.RequiredValue) is > 1d;
            var row = ContentRow(content, $"Objective-{objective.Id}", y, rowHeight);
            row.gameObject.AddComponent<Image>().color = new Color(0.085f, 0.098f, 0.102f, 0.93f);

            const float actionWidth = 76f;
            const float valueWidth = 72f;
            var canHandover = !future
                && QuestMutationsAllowed()
                && liveQuest?.QuestStatus == EQuestStatus.Started
                && objectiveProgress?.Complete != true
                && condition is ConditionHandoverItem or ConditionWeaponAssembly;
            if (canHandover && condition is not null && TryCreateEligibleObjectiveHost(liveQuest!, condition, out var objectiveHost))
            {
                var buttonRect = UnityUiFactory.CreateRect("Handover", row);
                buttonRect.anchorMin = new Vector2(0, 0.15f);
                buttonRect.anchorMax = new Vector2(0, 0.85f);
                buttonRect.pivot = new Vector2(0, 0.5f);
                buttonRect.anchoredPosition = new Vector2(5, 0);
                buttonRect.sizeDelta = new Vector2(actionWidth - 10, 0);
                var button = UnityUiFactory.AddButton(buttonRect.gameObject, QuestGraphPalette.ControlActive);
                var buttonText = UnityUiFactory.AddText(buttonRect.gameObject, "HAND OVER", 9,
                    TextAlignmentOptions.Center, Color.white);
                buttonText.fontStyle = FontStyles.Bold;
                button.onClick.AddListener(() => RunNativeAction(
                    objectiveHost.Execute, QuestDetailsActionKind.Handover, button));
            }

            var valueRect = UnityUiFactory.CreateRect("Value", row);
            valueRect.anchorMin = new Vector2(0, 0);
            valueRect.anchorMax = new Vector2(0, 1);
            valueRect.pivot = new Vector2(0, 0.5f);
            valueRect.anchoredPosition = new Vector2(actionWidth, 0);
            valueRect.sizeDelta = new Vector2(valueWidth, 0);
            var valueText = objectiveProgress?.Complete == true
                ? "✓"
                : numeric
                    ? $"{Math.Min(objectiveProgress!.Current ?? 0, objectiveProgress.Required ?? objective.RequiredValue ?? 0):0.#} / {objectiveProgress.Required ?? objective.RequiredValue:0.#}"
                    : string.Empty;
            var value = UnityUiFactory.AddText(valueRect.gameObject, valueText, 12,
                TextAlignmentOptions.Center, future ? new Color(0.45f, 0.46f, 0.46f, 1f) : QuestGraphPalette.MutedText);
            if (objectiveProgress?.Complete == true) value.color = QuestGraphPalette.Completed;

            var descriptionRect = UnityUiFactory.CreateRect("Text", row);
            descriptionRect.anchorMin = Vector2.zero;
            descriptionRect.anchorMax = Vector2.one;
            descriptionRect.offsetMin = new Vector2(actionWidth + valueWidth, numeric && objectiveProgress?.Complete != true ? 7 : 3);
            descriptionRect.offsetMax = new Vector2(-8, -3);
            var description = UnityUiFactory.AddText(descriptionRect.gameObject, objective.Text, 13,
                TextAlignmentOptions.MidlineLeft,
                future ? new Color(0.43f, 0.44f, 0.44f, 1f) : objectiveProgress?.Complete == true
                    ? QuestGraphPalette.Completed
                    : new Color(0.90f, 0.90f, 0.86f, 1f));
            description.enableWordWrapping = true;
            if (numeric && objectiveProgress is { Complete: false })
            {
                var required = objectiveProgress.Required ?? objective.RequiredValue ?? 0;
                var current = Math.Min(objectiveProgress.Current ?? 0, required);
                if (required > 0) AddProgressBar(row, current / required, actionWidth + valueWidth, 2, QuestGraphPalette.InProgress);
            }
            y += rowHeight;
            if (index < orderedObjectives.Length - 1) y += 3f;
        }
        if (node.Objectives.Count == 0)
        {
            var empty = ContentRow(content, "NoObjectives", 0, 42);
            UnityUiFactory.AddText(empty.gameObject, "No objectives are recorded.", 13,
                TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
            y = 42;
        }
        ConfigureScrollableContent(viewport, content, y);
    }

    private void BuildRewards(QuestGraphNode node, QuestClass? liveQuest, bool future, float top, float height)
    {
        var section = TopRect("RewardsSection", top, height);
        var title = UnityUiFactory.AddText(section.gameObject, "REWARDS", 14,
            TextAlignmentOptions.TopLeft, Color.white);
        title.margin = new Vector4(10, 5, 10, 0);
        title.fontStyle = FontStyles.Bold;
        var viewport = CreateScrollViewport(section, "RewardsViewport", 28, 4, out var content);

        if (!future && liveQuest is not null && TryBuildNativeRewards(node, liveQuest, viewport, content, out var nativeHeight))
        {
            content.sizeDelta = new Vector2(0, Mathf.Max(nativeHeight, viewport.rect.height));
            return;
        }

        var rewards = node.Rewards.Where(reward => _showHiddenRewards() || !reward.Hidden).ToArray();
        var y = 0f;
        foreach (var reward in rewards)
        {
            var row = ContentRow(content, $"Reward-{reward.Id}", y, 44);
            row.gameObject.AddComponent<Image>().color = new Color(0.085f, 0.098f, 0.102f, 0.93f);
            var text = UnityUiFactory.AddText(row.gameObject, RewardText(reward), 12,
                TextAlignmentOptions.MidlineLeft, future ? new Color(0.52f, 0.53f, 0.53f, 1f) : Color.white);
            text.margin = new Vector4(10, 3, 8, 3);
            y += 47;
        }
        if (rewards.Length == 0)
        {
            var empty = ContentRow(content, "NoRewards", 0, 42);
            UnityUiFactory.AddText(empty.gameObject, "No rewards are recorded.", 13,
                TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
            y = 42;
        }
        content.sizeDelta = new Vector2(0, Mathf.Max(y, viewport.rect.height));
    }

    private bool TryBuildNativeRewards(
        QuestGraphNode node,
        QuestClass quest,
        RectTransform viewport,
        RectTransform content,
        out float height)
    {
        height = 0;
        var source = FindQuestViewSource();
        var prefab = source is null ? null : RewardListPrefabField.GetValue(source) as GameObject;
        if (prefab is null || !quest.Template.Rewards.TryGetValue(EQuestStatus.Success, out var rawRewards)) return false;
        var rewards = rawRewards.Where((_, index) =>
            _showHiddenRewards() || index >= node.Rewards.Count || !node.Rewards[index].Hidden).ToArray();
        var instance = UnityEngine.Object.Instantiate(prefab, content, false);
        instance.name = "NativeRewardHost";
        instance.SetActive(true);
        if (instance.TryGetComponent<Image>(out var panelBackground)) panelBackground.enabled = false;
        var list = instance.GetComponent<QuestRewardList>();
        if (list is null)
        {
            UnityEngine.Object.Destroy(instance);
            return false;
        }
        list.Init(string.Empty, rewards, true, null);
        var container = RewardContainerField.GetValue(list) as RectTransform;
        if (container is not null)
        {
            // Keep EFT's native reward cards and interaction components, but
            // detach their grid from the stock QuestRewardList panel. The stock
            // panel reserves a title/header offset which otherwise becomes an
            // unexplained blank strip in QuestMap's own REWARDS section.
            container.SetParent(content, false);
            container.name = "NativeRewardCards";
            container.anchorMin = new Vector2(0, 1);
            container.anchorMax = new Vector2(1, 1);
            container.pivot = new Vector2(0.5f, 1);
            container.anchoredPosition = Vector2.zero;
            NormalizeRewardLayout(container);
            if (container.TryGetComponent<Graphic>(out var containerBackground)) containerBackground.enabled = false;

            var rows = Mathf.Max(1, Mathf.CeilToInt(rewards.Length / 2f));
            height = rows * 72f;
            container.sizeDelta = new Vector2(0, height);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(container);

            // Keep the native owner active for its first-frame setup, but make
            // the now-empty stock host invisible and non-interactive. The live
            // detached grid is normalized again after EFT completes layout.
            var hostCanvas = instance.GetComponent<CanvasGroup>() ?? instance.AddComponent<CanvasGroup>();
            hostCanvas.alpha = 0f;
            hostCanvas.interactable = false;
            hostCanvas.blocksRaycasts = false;
            if (instance.transform is RectTransform hostRect) hostRect.sizeDelta = Vector2.zero;
            container.gameObject.AddComponent<QuestDetailsRewardLayoutNormalizer>()
                .Bind(container, content, viewport, rows);
        }
        else
        {
            UnityEngine.Object.Destroy(instance);
            return false;
        }
        return true;
    }

    private static void NormalizeRewardLayout(RectTransform container)
    {
        foreach (var grid in container.GetComponents<GridLayoutGroup>())
        {
            grid.padding.top = 0;
            grid.childAlignment = TextAnchor.UpperCenter;
        }
        foreach (var layout in container.GetComponents<HorizontalOrVerticalLayoutGroup>())
        {
            layout.padding.top = 0;
            layout.childAlignment = TextAnchor.UpperCenter;
        }
    }

    private void BuildFailure(QuestGraphNode node)
    {
        ClearNativeObjectiveHosts();
        foreach (Transform child in Root)
        {
            if (ReferenceEquals(child, _nativeHostRoot)) continue;
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }

        var title = UnityUiFactory.AddText(Root.gameObject, node.Name, 18,
            TextAlignmentOptions.TopLeft, Color.white);
        title.margin = new Vector4(18, 18, 18, 0);
        title.fontStyle = FontStyles.Bold;
        var message = UnityUiFactory.AddText(Root.gameObject,
            "Quest details could not be rendered. The quest map remains available; see LogOutput.log for the failing component.",
            14, TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
        message.margin = new Vector4(28, 80, 28, 28);
    }

    private bool TryCreateEligibleObjectiveHost(QuestClass quest, Condition condition, out NativeQuestHandoverAction host)
    {
        host = NativeQuestTableActions.TryCreateHandover(
            _nativeHostRoot, quest, condition.id, _questController, _inventoryController)!;
        if (host is null) return false;
        _objectiveHosts.Add(host);
        return true;
    }

    private void ClearNativeObjectiveHosts()
    {
        foreach (var host in _objectiveHosts)
        {
            if (host == null) continue;
            host.Dispose();
        }
        _objectiveHosts.Clear();
    }

    private async void RunNativeAction(Func<Task> action, QuestDetailsActionKind kind, Button button)
    {
        if (_disposed || !button.interactable || !QuestMutationsAllowed()) return;
        var questId = _selectedQuestId;
        button.interactable = false;
        try
        {
            await action();
            if (_disposed || string.IsNullOrWhiteSpace(questId)) return;
            // Rebuilding from the pre-transaction overlay can briefly corrupt a
            // row and cannot represent a reroll's replacement ID. Reconcile the
            // authoritative quest book/server projection before rendering.
            _requestQuestRefresh(questId, kind);
        }
        catch (Exception exception)
        {
            _log.LogError($"QUESTMAP_M07_ACTION_ERROR quest={_selectedQuestId}; {exception}");
        }
        finally
        {
            if (button != null) button.interactable = true;
        }
    }

    private static IReadOnlyDictionary<string, Condition> LiveConditions(QuestClass? quest)
    {
        if (quest is null || !quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var conditions))
            return new Dictionary<string, Condition>(StringComparer.Ordinal);
        return conditions.IEnumerable_0
            .GroupBy(condition => condition.id.ToString(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    private void AddArtwork(RectTransform parent, string? url, Color fallback, float shadeAlpha)
    {
        parent.gameObject.AddComponent<Image>().color = fallback;
        parent.gameObject.AddComponent<RectMask2D>();
        if (!string.IsNullOrWhiteSpace(url))
        {
            var imageRect = UnityUiFactory.CreateRect("Artwork", parent);
            UnityUiFactory.Stretch(imageRect);
            var image = imageRect.gameObject.AddComponent<Image>();
            image.preserveAspect = false;
            image.raycastTarget = false;
            var fitter = imageRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            imageRect.gameObject.SetActive(false);
            _assetCache.Request(url, sprite =>
            {
                if (image == null || sprite is null) return;
                image.sprite = sprite;
                fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
                imageRect.gameObject.SetActive(true);
            });
        }
        var shade = UnityUiFactory.CreateRect("Shade", parent);
        UnityUiFactory.Stretch(shade);
        shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, shadeAlpha);
    }

    private RectTransform TopRect(string name, float top, float height)
    {
        var rect = UnityUiFactory.CreateRect(name, Root);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, -top);
        rect.sizeDelta = new Vector2(0, height);
        return rect;
    }

    private void AddDivider(float top)
    {
        var divider = TopRect("Divider", top, 1);
        divider.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
    }

    private static RectTransform FractionRect(string name, RectTransform parent, float minimum, float maximum)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = new Vector2(minimum, 0);
        rect.anchorMax = new Vector2(maximum, 1);
        rect.offsetMin = new Vector2(minimum == 0 ? 0 : 2, 0);
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static RectTransform ContentRow(RectTransform content, string name, float top, float height)
    {
        var row = UnityUiFactory.CreateRect(name, content);
        row.anchorMin = new Vector2(0, 1);
        row.anchorMax = new Vector2(1, 1);
        row.pivot = new Vector2(0.5f, 1);
        row.anchoredPosition = new Vector2(0, -top);
        row.sizeDelta = new Vector2(0, height);
        return row;
    }

    private static RectTransform CreateScrollViewport(
        RectTransform parent,
        string name,
        float top,
        float bottom,
        out RectTransform content)
    {
        var viewport = UnityUiFactory.CreateRect(name, parent);
        UnityUiFactory.Stretch(viewport, 5, 10, top, bottom);
        // The pane gradient is the only surface. The Image remains as a clear
        // raycast target for scrolling without adding a second dark panel.
        viewport.gameObject.AddComponent<Image>().color = Color.clear;
        viewport.gameObject.AddComponent<RectMask2D>();
        content = UnityUiFactory.CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = 32f;
        var scrollbar = CreateScrollbar(viewport);
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalScrollbarSpacing = 2;
        return viewport;
    }

    private static Scrollbar CreateScrollbar(RectTransform viewport)
    {
        var track = UnityUiFactory.CreateRect("Scrollbar", viewport);
        track.anchorMin = new Vector2(1, 0);
        track.anchorMax = Vector2.one;
        track.pivot = new Vector2(1, 0.5f);
        track.offsetMin = new Vector2(-9, 3);
        track.offsetMax = new Vector2(-1, -3);
        var trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(0.10f, 0.11f, 0.11f, 0.88f);
        var slidingArea = UnityUiFactory.CreateRect("SlidingArea", track);
        UnityUiFactory.Stretch(slidingArea, 1, 1, 1, 1);
        var handle = UnityUiFactory.CreateRect("Handle", slidingArea);
        UnityUiFactory.Stretch(handle);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.66f, 0.69f, 0.68f, 0.95f);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.targetGraphic = handleImage;
        scrollbar.handleRect = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        return scrollbar;
    }

    private static void ConfigureScrollableContent(RectTransform viewport, RectTransform content, float desiredHeight)
    {
        // Unity layout values can differ by a fraction of a pixel on the first
        // frame. Do not expose a scrollbar merely because that rounding makes
        // otherwise fitting objective rows microscopically taller.
        var viewportHeight = viewport.rect.height;
        var scrollable = desiredHeight > viewportHeight + 2f;
        content.sizeDelta = new Vector2(0, scrollable ? desiredHeight : viewportHeight);
        var scroll = viewport.GetComponent<ScrollRect>();
        scroll.vertical = scrollable;
        scroll.verticalNormalizedPosition = 1f;
        if (scroll.verticalScrollbar != null) scroll.verticalScrollbar.gameObject.SetActive(scrollable);
    }

    private static void CreateTextScroll(
        RectTransform parent,
        string name,
        string text,
        float top,
        float bottom,
        float fontSize,
        Color color)
    {
        var viewport = CreateScrollViewport(parent, name, top, bottom, out var content);
        var value = string.IsNullOrWhiteSpace(text) ? "No description is available." : text;
        var label = UnityUiFactory.AddText(content.gameObject,
            value,
            fontSize,
            TextAlignmentOptions.TopLeft,
            color);
        label.margin = new Vector4(9, 7, 9, 7);
        label.richText = true;
        label.enableWordWrapping = true;
        // Start with a safe estimate, then replace it with TMP's actual rendered
        // bounds after the font/material and viewport have completed layout.
        // Measuring preferredHeight synchronously here can dereference an
        // uninitialised TMP material during an EFT screen transition.
        var lineCount = EstimateWrappedLines(value, 78);
        var estimatedHeight = lineCount * (fontSize * 1.35f) + 18f;
        var scroll = viewport.GetComponent<ScrollRect>();
        var scrollable = estimatedHeight > viewport.rect.height + 1f;
        content.sizeDelta = new Vector2(0, scrollable ? estimatedHeight : viewport.rect.height);
        content.anchoredPosition = Vector2.zero;
        scroll.vertical = scrollable;
        scroll.verticalNormalizedPosition = 1f;
        if (scroll.verticalScrollbar is not null) scroll.verticalScrollbar.gameObject.SetActive(scrollable);
        content.gameObject.AddComponent<QuestDetailsTextContentSizer>()
            .Bind(label, viewport, content, scroll);
    }

    private static void AddTab(
        RectTransform parent,
        string name,
        string label,
        float minimum,
        float maximum,
        bool active,
        Action action)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = new Vector2(minimum, 1);
        rect.anchorMax = new Vector2(maximum, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(-2, 30);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        var text = UnityUiFactory.AddText(rect.gameObject, label, 11, TextAlignmentOptions.Center, Color.white);
        text.fontStyle = FontStyles.Bold;
        button.onClick.AddListener(() => action());
    }

    private static void AddProgressBar(RectTransform parent, double ratio, float horizontalInset, float bottom, Color color)
    {
        var track = UnityUiFactory.CreateRect("ProgressTrack", parent);
        track.anchorMin = new Vector2(0, 0);
        track.anchorMax = new Vector2(1, 0);
        track.pivot = new Vector2(0.5f, 0);
        track.offsetMin = new Vector2(horizontalInset, bottom);
        track.offsetMax = new Vector2(-10, bottom + 5);
        track.gameObject.AddComponent<Image>().color = new Color(0.16f, 0.17f, 0.17f, 1);
        var fill = UnityUiFactory.CreateRect("Fill", track);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2((float)Math.Clamp(ratio, 0d, 1d), 1);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        fill.gameObject.AddComponent<Image>().color = color;
    }

    private static string RewardText(QuestReward reward)
    {
        var hidden = reward.Hidden ? "[HIDDEN]  " : string.Empty;
        if (reward.Items.Count > 0)
            return hidden + string.Join(", ", reward.Items.Select(item => $"{item.Count:0.##}× {item.Name}"));
        var subject = reward.TargetName ?? reward.TraderName ?? reward.TargetId;
        var value = reward.Value.HasValue ? $" {reward.Value.Value:+0.##;-0.##;0}" : string.Empty;
        var loyalty = reward.LoyaltyLevel.HasValue ? $" LL{reward.LoyaltyLevel.Value}" : string.Empty;
        return $"{hidden}{reward.Type}{(string.IsNullOrWhiteSpace(subject) ? string.Empty : $" · {subject}")}{value}{loyalty}";
    }

    private static void AddTextShadow(TMP_Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.96f);
        shadow.effectDistance = new Vector2(1, -1);
    }

    private static bool IsRaidOnlyTrader(string traderId) =>
        string.Equals(traderId, LightkeeperTraderId, StringComparison.Ordinal)
        || string.Equals(traderId, BtrDriverTraderId, StringComparison.Ordinal);

    private static string LocationBannerUrl(QuestLocation location) =>
        location.Any
        || location.Id.Contains("transit", StringComparison.OrdinalIgnoreCase)
        || string.Equals(location.Name, "Transition", StringComparison.OrdinalIgnoreCase)
            ? FallbackLocationBannerUrl
            : location.BannerImageUrl ?? FallbackLocationBannerUrl;

    private static bool QuestMutationsAllowed() => !InRaidQuestContext.TryCapture(out _);

    private static void AddRouteBar(RectTransform parent, bool collector, bool lightkeeper)
    {
        if (!collector && !lightkeeper) return;
        const float height = 12f;
        if (collector) AddRouteBarPart(parent, "CollectorRoute", "COLLECTOR", QuestGraphPalette.Collector,
            0f, lightkeeper ? 0.5f : 1f, height);
        if (lightkeeper) AddRouteBarPart(parent, "LightkeeperRoute", "LIGHTKEEPER", QuestGraphPalette.Lightkeeper,
            collector ? 0.5f : 0f, 1f, height);
    }

    private static void AddScavBadge(RectTransform parent)
    {
        var badge = UnityUiFactory.CreateRect("ScavBadge", parent);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1, 1);
        badge.anchoredPosition = new Vector2(-10, -10);
        badge.sizeDelta = new Vector2(58, 22);
        badge.gameObject.AddComponent<Image>().color = new Color(0.40f, 0.34f, 0.16f, 0.96f);
        var label = UnityUiFactory.AddText(badge.gameObject, "SCAV", 10,
            TextAlignmentOptions.Center, new Color(1f, 0.96f, 0.78f, 1f));
        label.fontStyle = FontStyles.Bold;
    }

    private static void AddRouteBarPart(
        RectTransform parent,
        string name,
        string label,
        Color color,
        float minimum,
        float maximum,
        float height)
    {
        var bar = UnityUiFactory.CreateRect(name, parent);
        bar.anchorMin = new Vector2(minimum, 0);
        bar.anchorMax = new Vector2(maximum, 0);
        bar.pivot = new Vector2(0.5f, 0);
        bar.offsetMin = Vector2.zero;
        bar.offsetMax = new Vector2(0, height);
        bar.gameObject.AddComponent<Image>().color = color;
        var text = UnityUiFactory.AddText(bar.gameObject, label, 7, TextAlignmentOptions.Center, Color.white);
        text.fontStyle = FontStyles.Bold;
    }

    private static QuestView? FindQuestViewSource() =>
        Resources.FindObjectsOfTypeAll<QuestView>()
            .FirstOrDefault(view => view != null
                && !view.name.StartsWith("QuestMapNativeActionHost", StringComparison.Ordinal)
                && RewardListPrefabField.GetValue(view) is GameObject);

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

    private enum DetailTextTab
    {
        Description,
        Summary,
    }

    private sealed class NativeQuestViewHost : IDisposable
    {
        private readonly QuestView _view;
        private readonly ISession _session;
        private readonly InventoryController _inventoryController;
        private readonly AbstractQuestControllerClass _questController;
        private readonly ManualLogSource _log;
        private bool _bound;

        private NativeQuestViewHost(
            QuestView view,
            ISession session,
            InventoryController inventoryController,
            AbstractQuestControllerClass questController,
            ManualLogSource log)
        {
            _view = view;
            _session = session;
            _inventoryController = inventoryController;
            _questController = questController;
            _log = log;
        }

        public static NativeQuestViewHost? TryCreate(
            RectTransform parent,
            ISession session,
            InventoryController inventoryController,
            AbstractQuestControllerClass questController,
            ManualLogSource log)
        {
            var source = FindQuestViewSource();
            if (source is null)
            {
                log.LogWarning("QUESTMAP_M07_NATIVE unavailable=QuestView; actions and native rewards remain read-only");
                return null;
            }
            var clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
            clone.name = "QuestMapNativeActionHost";
            clone.SetActive(false);
            var view = clone.GetComponent<QuestView>();
            if (view is null)
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }
            return new NativeQuestViewHost(view, session, inventoryController, questController, log);
        }

        public bool Bind(QuestClass quest, string traderId)
        {
            var trader = _session.Traders.FirstOrDefault(value => string.Equals(value.Id, traderId, StringComparison.Ordinal));
            if (trader is null)
            {
                _log.LogWarning($"QUESTMAP_M07_NATIVE quest={quest.Id}; unavailable=trader:{traderId}");
                return false;
            }
            if (_bound) _view.Close();
            _view.Show(_session, _inventoryController, _questController, quest, trader);
            _view.gameObject.SetActive(false);
            _bound = true;
            return true;
        }

        public Task Accept(QuestClass quest) => _view.StartQuest(quest);

        public Task Complete(QuestClass quest) => _view.FinishQuest(quest);

        public Task Replace() => _view.ShowChangeQuestConfirmation();

        public void Dispose()
        {
            if (_view == null) return;
            if (_bound) _view.Close();
            UnityEngine.Object.Destroy(_view.gameObject);
        }
    }
}

internal enum QuestDetailsActionKind
{
    Accept,
    Restart,
    Complete,
    Replace,
    Handover,
}

internal sealed class QuestDetailsTextContentSizer : MonoBehaviour
{
    private TextMeshProUGUI? _label;
    private RectTransform? _viewport;
    private RectTransform? _content;
    private ScrollRect? _scroll;
    private int _attempts;

    public QuestDetailsTextContentSizer Bind(
        TextMeshProUGUI label,
        RectTransform viewport,
        RectTransform content,
        ScrollRect scroll)
    {
        _label = label;
        _viewport = viewport;
        _content = content;
        _scroll = scroll;
        return this;
    }

    private void LateUpdate()
    {
        if (_label is null || _viewport is null || _content is null || _scroll is null)
        {
            enabled = false;
            return;
        }
        if (++_attempts < 2 || _label.font is null || _viewport.rect.width <= 1f) return;

        try
        {
            _label.ForceMeshUpdate(true, true);
            var renderedHeight = _label.GetRenderedValues(false).y;
            var exactHeight = Mathf.Max(1f, renderedHeight + _label.margin.y + _label.margin.w + 4f);
            var scrollable = exactHeight > _viewport.rect.height + 1f;
            _content.sizeDelta = new Vector2(0, scrollable ? exactHeight : _viewport.rect.height);
            _content.anchoredPosition = Vector2.zero;
            _scroll.vertical = scrollable;
            _scroll.verticalNormalizedPosition = 1f;
            if (_scroll.verticalScrollbar is not null)
                _scroll.verticalScrollbar.gameObject.SetActive(scrollable);
            enabled = false;
        }
        catch
        {
            // TMP can still be between font/material lifecycle phases for the
            // first few frames of an EFT screen transition. Retry briefly, but
            // retain the safe estimate rather than letting detail UI fail.
            if (_attempts >= 12) enabled = false;
        }
    }
}

internal sealed class QuestDetailsRewardLayoutNormalizer : MonoBehaviour
{
    private RectTransform? _container;
    private RectTransform? _content;
    private RectTransform? _viewport;
    private int _rows;
    private int _passes;

    public QuestDetailsRewardLayoutNormalizer Bind(
        RectTransform container,
        RectTransform content,
        RectTransform viewport,
        int rows)
    {
        _container = container;
        _content = content;
        _viewport = viewport;
        _rows = Mathf.Max(1, rows);
        return this;
    }

    private void LateUpdate()
    {
        if (_container is null || _content is null || _viewport is null)
        {
            enabled = false;
            return;
        }

        try
        {
            foreach (var grid in _container.GetComponents<GridLayoutGroup>())
            {
                grid.padding.top = 0;
                grid.childAlignment = TextAnchor.UpperCenter;
            }
            foreach (var layout in _container.GetComponents<HorizontalOrVerticalLayoutGroup>())
            {
                layout.padding.top = 0;
                layout.childAlignment = TextAnchor.UpperCenter;
            }

            var expectedHeight = _rows * 72f;
            _container.sizeDelta = new Vector2(0, expectedHeight);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_container);

            var minimum = float.PositiveInfinity;
            var maximum = float.NegativeInfinity;
            foreach (Transform child in _container)
            {
                if (!child.gameObject.activeSelf || child is not RectTransform childRect) continue;
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(_container, childRect);
                minimum = Mathf.Min(minimum, bounds.min.y);
                maximum = Mathf.Max(maximum, bounds.max.y);
            }
            var renderedHeight = float.IsInfinity(minimum) || float.IsInfinity(maximum)
                ? expectedHeight
                : maximum - minimum + 4f;
            var height = Mathf.Max(expectedHeight, renderedHeight);
            _container.sizeDelta = new Vector2(0, height);
            _container.anchoredPosition = new Vector2(0, float.IsInfinity(maximum) ? 0 : -maximum);
            _content.sizeDelta = new Vector2(0, Mathf.Max(height, _viewport.rect.height));
            _content.anchoredPosition = Vector2.zero;

            var scroll = _viewport.GetComponent<ScrollRect>();
            if (scroll is not null)
            {
                var scrollable = height > _viewport.rect.height + 1f;
                scroll.vertical = scrollable;
                scroll.verticalNormalizedPosition = 1f;
                if (scroll.verticalScrollbar is not null)
                    scroll.verticalScrollbar.gameObject.SetActive(scrollable);
            }

            // EFT may finish populating/layout one frame after Init. Reapply for
            // a few bounded frames so first-open and subsequent-open geometry
            // converge without any permanent Update cost.
            if (++_passes >= 4) enabled = false;
        }
        catch
        {
            if (++_passes >= 8) enabled = false;
        }
    }
}
