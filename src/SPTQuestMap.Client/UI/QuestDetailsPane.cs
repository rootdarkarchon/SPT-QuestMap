using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;
using SPTQuestMap.Client.Data;
using SPTQuestMap.Client.Localization;
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
    private const float SectionGap = 2f;
    private const string FallbackLocationBannerUrl = "/files/banners/norvinskzone.png";
    private static readonly FieldInfo RewardContainerField = AccessTools.Field(typeof(QuestRewardList), "_container")
        ?? throw new MissingFieldException(typeof(QuestRewardList).FullName, "_container");

    private readonly NativeQuestWorkspaceContext _workspace;
    private readonly InventoryController _inventoryController;
    private readonly AbstractQuestControllerClass _questController;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly Action<string, QuestDetailsActionKind> _requestQuestRefresh;
    private readonly string? _contextTraderId;
    private readonly float _headerHeight;
    private readonly RectTransform _shadow;
    private readonly RectTransform _nativeHostRoot;
    private readonly NativeQuestViewHost? _actionHost;
    private readonly QuestObjectiveSkipVisibility _skipVisibility;
    private readonly List<NativeQuestHandoverAction> _objectiveHosts = [];
    private string? _selectedQuestId;
    private DetailTextTab _textTab;
    private QuestGraphTopology? _topology;
    private QuestProfileOverlay? _overlay;
    private bool _disposed;

    private QuestDetailsPane(
        RectTransform root,
        RectTransform shadow,
        NativeQuestWorkspaceContext workspace,
        QuestAssetSpriteCache assetCache,
        Action<string, QuestDetailsActionKind> requestQuestRefresh,
        string? contextTraderId,
        float headerHeightScale)
    {
        Root = root;
        _shadow = shadow;
        _workspace = workspace;
        _inventoryController = workspace.InventoryController;
        _questController = workspace.QuestController;
        _assetCache = assetCache;
        _requestQuestRefresh = requestQuestRefresh;
        _contextTraderId = contextTraderId;
        _headerHeight = HeaderHeight * Mathf.Clamp(headerHeightScale, 0.5f, 1f);
        _nativeHostRoot = UnityUiFactory.CreateRect("NativeActionHosts", root);
        UnityUiFactory.Stretch(_nativeHostRoot);
        _nativeHostRoot.gameObject.SetActive(false);
        _actionHost = NativeQuestViewHost.TryCreate(
            _nativeHostRoot,
            workspace.Session,
            _inventoryController,
            _questController,
            workspace.Log);
        _skipVisibility = root.gameObject.AddComponent<QuestObjectiveSkipVisibility>();
        _skipVisibility.Bind(workspace.TaskSkippingEnabled, workspace.TaskSkipModifier);
    }

    public RectTransform Root { get; }

    public static QuestDetailsPane Create(
        RectTransform overlayParent,
        RectTransform viewport,
        NativeQuestWorkspaceContext workspace,
        QuestAssetSpriteCache assetCache,
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
            root, shadow, workspace, assetCache,
            requestQuestRefresh, contextTraderId, headerHeightScale);
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
            _textTab = _workspace.DefaultDetailsToSummary() && !string.IsNullOrWhiteSpace(node.Summary)
                ? DetailTextTab.Summary
                : DetailTextTab.Description;
        }
        try
        {
            Rebuild(node);
        }
        catch (Exception exception)
        {
            _workspace.Log.LogError($"QUESTMAP_M07_DETAILS_ERROR quest={questId}; {exception}");
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
        DestroyPane();
    }

    public void AbandonNativeRuntime()
    {
        if (_disposed) return;
        _disposed = true;
        _skipVisibility.Clear();
        foreach (var host in _objectiveHosts) host?.Abandon();
        _objectiveHosts.Clear();
        _actionHost?.Abandon();
        DestroyPane();
    }

    private void DestroyPane()
    {
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
        var actionBound = _workspace.MutationsAllowed() && liveQuest is not null && !raidOnlyTrader
            && _actionHost?.Bind(liveQuest, node.TraderId) == true;
        var descriptionHeight = DesiredDescriptionHeight(node);
        var objectivesHeight = DesiredObjectivesHeight(node, liveState);

        BuildHeader(node, 0, _headerHeight);
        AddFixedDivider(_headerHeight);
        var actionsTop = _headerHeight + SectionGap;
        BuildActions(Root, node, liveQuest, actionBound, raidOnlyTrader, actionsTop, ActionsHeight);
        var bodyTop = actionsTop + ActionsHeight;
        AddFixedDivider(bodyTop);
        bodyTop += SectionGap;
        var bodyViewport = CreateScrollViewport(Root, "DetailsBodyViewport", bodyTop, 0, out var body);
        ConfigureExpandingBody(body);
        BuildDescription(body, node, descriptionHeight);
        AddStackDivider(body);
        BuildObjectives(body, node, liveState, liveQuest, future, objectivesHeight);
        AddStackDivider(body);
        var rewardsHeight = BuildRewards(body, node, liveQuest, future);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(body);
        bodyViewport.GetComponent<ScrollRect>().verticalNormalizedPosition = 1f;
        _nativeHostRoot.SetAsLastSibling();
        QuestMapDebugLog.Info(_workspace.Log,
            "QUESTMAP_M07_DETAILS " +
            $"quest={node.Id}; live={!future}; actionBound={actionBound}; raidOnlyTrader={raidOnlyTrader}; objectives={node.Objectives.Count}; " +
            $"rewards={node.Rewards.Count}; summary={!string.IsNullOrWhiteSpace(node.Summary)}; hiddenRewards={_workspace.ShowHiddenRewards()}; " +
            $"descriptionHeight={descriptionHeight:0.#}; objectivesHeight={objectivesHeight:0.#}; rewardsHeight={rewardsHeight:0.#}; fixedHeaderAndActions=True; nestedScroll=False");
    }

    private float DesiredDescriptionHeight(QuestGraphNode node)
    {
        var hasSummary = !string.IsNullOrWhiteSpace(node.Summary);
        var hasRelevantItems = node.RelevantItems.Count > 0;
        var tabs = hasSummary || hasRelevantItems ? 34f : 0f;
        if (_textTab == DetailTextTab.RelevantItems && hasRelevantItems)
        {
            var rows = node.RelevantItems.Count * 36f + Math.Max(0, node.RelevantItems.Count - 1) * 3f;
            return tabs + 4f + rows + 6f;
        }
        var text = _textTab == DetailTextTab.Summary && hasSummary ? node.Summary : node.Description;
        var estimatedTextHeight = EstimateWrappedLines(text, 78) * 18.9f + 18f;
        return Mathf.Max(tabs > 0 ? 92f : 72f, tabs + 4f + estimatedTextHeight + 8f);
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
        return Mathf.Max(90f, 42f + rows);
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
        var header = TopRect("Header", Root, top, height);
        var questBanner = FractionRect("QuestBanner", header, 0f, 0.67f);
        var locationBanner = FractionRect("LocationBanner", header, 0.67f, 1f);
        AddArtwork(questBanner, node.ImageUrl, new Color(0.02f, 0.025f, 0.028f, 1f), 0.42f);
        var maps = QuestMapLocationVisuals.DisplayMaps(node, FallbackLocationBannerUrl);
        QuestMapLocationVisuals.AddArtworkSlices(
            locationBanner, maps, FallbackLocationBannerUrl,
            new Color(0.02f, 0.025f, 0.028f, 1f), 0.48f, _assetCache);

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
        if (!string.IsNullOrWhiteSpace(node.WikiUrl)) AddWikiButton(questBanner, node.WikiUrl);
        if (node.ScavRepeatable) AddScavBadge(questBanner, string.IsNullOrWhiteSpace(node.WikiUrl) ? 10f : 46f);

        QuestMapLocationVisuals.AddCompactMapText(
            locationBanner,
            maps,
            _contextTraderId is null ? 9 : 5,
            15,
            TextAlignmentOptions.Center,
            Color.white);
    }

    private void BuildActions(
        RectTransform parent,
        QuestGraphNode node,
        QuestClass? liveQuest,
        bool actionBound,
        bool raidOnlyTrader,
        float top,
        float height)
    {
        var area = TopRect("Actions", parent, top, height);
        if (!_workspace.MutationsAllowed())
        {
            var disabled = UnityUiFactory.AddText(area.gameObject, ClientLocale.Text("label.actionsDisabledInRaid"), 12,
                TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
            disabled.fontStyle = FontStyles.Bold;
            UnityUiFactory.FitSingleLine(disabled, 8f, 8f);
            return;
        }
        if (liveQuest is null || !actionBound)
        {
            var status = QuestGraphCardNodeView.StateLabel(
                QuestGraphRules.ClassifyProfileDisplayState(_topology!, node, _overlay!));
            if (raidOnlyTrader)
                status += ClientLocale.Format("common.inlineDetail",
                    ClientLocale.Arg("detail", ClientLocale.Text("label.inRaidQuestGiver")));
            var text = UnityUiFactory.AddText(area.gameObject, status.ToUpperInvariant(), 12,
                TextAlignmentOptions.Center, new Color(0.55f, 0.57f, 0.57f, 1f));
            text.fontStyle = FontStyles.Bold;
            return;
        }

        var actions = new List<(string Label, QuestDetailsActionKind Kind, Func<Task> Action)>();
        switch (liveQuest.QuestStatus)
        {
            case EQuestStatus.AvailableForStart:
                actions.Add((ClientLocale.Text("label.accept"), QuestDetailsActionKind.Accept, () => _actionHost!.Accept(liveQuest)));
                break;
            case EQuestStatus.FailRestartable:
                actions.Add((ClientLocale.Text("label.restart"), QuestDetailsActionKind.Restart, () => _actionHost!.Accept(liveQuest)));
                break;
            case EQuestStatus.AvailableForFinish:
                // Tarkov's quest-level FinishQuest path is the native TURN IN
                // operation. Objective HAND OVER remains the separate
                // QuestObjectiveView/HandoverItem path below.
                actions.Add((ClientLocale.Text("label.turnIn"), QuestDetailsActionKind.Complete, () => _actionHost!.Complete(liveQuest)));
                break;
        }
        if (liveQuest.IsChangeAllowed)
            actions.Add((ClientLocale.Text("label.replace"), QuestDetailsActionKind.Replace, () => _actionHost!.Replace()));

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
            UnityUiFactory.FitSingleLine(label, 8f, 6f);
            button.onClick.AddListener(() => RunNativeAction(action.Action, action.Kind, button));
            QuestMapNativeTooltips.Bind(rect.gameObject, () => ClientLocale.Format(action.Kind switch
            {
                QuestDetailsActionKind.Accept => "tooltip.accept",
                QuestDetailsActionKind.Restart => "tooltip.restart",
                QuestDetailsActionKind.Complete => "tooltip.turnIn",
                _ => "tooltip.replace",
            }, ClientLocale.Arg("quest", node.Name)));
            x += width + gap;
        }
    }

    private void BuildDescription(RectTransform parent, QuestGraphNode node, float height)
    {
        var section = StackRect("DescriptionSection", parent, height);
        var hasSummary = !string.IsNullOrWhiteSpace(node.Summary);
        var hasRelevantItems = node.RelevantItems.Count > 0;
        var tabCount = 1 + (hasSummary ? 1 : 0) + (hasRelevantItems ? 1 : 0);
        var tabHeight = tabCount > 1 ? 30f : 0f;
        if (tabCount > 1)
        {
            var tabIndex = 0;
            AddTab(section, "DescriptionTab", ClientLocale.Text("label.description"), TabStart(tabIndex++, tabCount), TabStart(tabIndex, tabCount), _textTab == DetailTextTab.Description,
                () => SelectTextTab(DetailTextTab.Description), _textTab == DetailTextTab.Description
                    ? null
                    : () => ClientLocale.Text("tooltip.showDescription"));
            if (hasSummary)
            {
                AddTab(section, "SummaryTab", ClientLocale.Text("label.summary"), TabStart(tabIndex++, tabCount), TabStart(tabIndex, tabCount), _textTab == DetailTextTab.Summary,
                    () => SelectTextTab(DetailTextTab.Summary), _textTab == DetailTextTab.Summary
                        ? null
                        : () => ClientLocale.Text("tooltip.showSummary"));
            }
            if (hasRelevantItems)
            {
                AddTab(section, "RelevantItemsTab", ClientLocale.Text("label.relevantItems"), TabStart(tabIndex++, tabCount), TabStart(tabIndex, tabCount), _textTab == DetailTextTab.RelevantItems,
                    () => SelectTextTab(DetailTextTab.RelevantItems), _textTab == DetailTextTab.RelevantItems
                        ? null
                        : () => ClientLocale.Text("tooltip.showRelevantItems"));
            }
        }
        if (_textTab == DetailTextTab.RelevantItems && hasRelevantItems)
        {
            BuildRelevantItems(section, node.RelevantItems, tabHeight + 4);
            return;
        }
        var text = _textTab == DetailTextTab.Summary && hasSummary ? node.Summary! : node.Description;
        CreateExpandedText(section, parent, "DescriptionContent", text, tabHeight + 4, 8, 14,
            new Color(0.88f, 0.88f, 0.82f, 1f), tabHeight + 12f, tabHeight > 0 ? 92f : 72f);
    }

    private static float TabStart(int index, int count) => index / (float)count;

    private void BuildRelevantItems(
        RectTransform section,
        IReadOnlyList<QuestRelevantItem> items,
        float top)
    {
        const float rowHeight = 36f;
        const float gap = 3f;
        var inRaid = InRaidQuestContext.TryCapture(out _);
        var contentHeight = items.Count * rowHeight + Math.Max(0, items.Count - 1) * gap;
        var content = CreateExpandedRegion(section, "RelevantItemsContent", top, contentHeight);
        var y = 0f;
        foreach (var item in items)
        {
            var row = ContentRow(content, $"RelevantItem-{item.TemplateId}", y, rowHeight);
            row.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.11f, 0.86f);
            var itemName = item.FleaEligible ? item.Name : ClientLocale.Format("label.fleaIneligible", ClientLocale.Arg("item", item.Name));
            var label = UnityUiFactory.AddText(row.gameObject, itemName, 13,
                TextAlignmentOptions.MidlineLeft,
                item.FleaEligible ? new Color(0.88f, 0.88f, 0.82f, 1f) : QuestGraphPalette.MutedText);
            label.margin = new Vector4(10, 3, item.FleaEligible ? 188 : 110, 3);

            var haveRect = UnityUiFactory.CreateRect("Have", row);
            haveRect.anchorMin = haveRect.anchorMax = haveRect.pivot = new Vector2(1, 0.5f);
            haveRect.anchoredPosition = new Vector2(item.FleaEligible ? -82 : -6, 0);
            haveRect.sizeDelta = new Vector2(100, 26);
            var haveLabel = UnityUiFactory.AddText(haveRect.gameObject, ClientLocale.Format("common.haveCount",
                    ClientLocale.Arg("count", OwnedItemCount(item.TemplateId))), 11,
                TextAlignmentOptions.MidlineRight, QuestGraphPalette.MutedText);
            haveLabel.fontStyle = FontStyles.Bold;
            if (item.FleaEligible)
            {
                var buttonRect = UnityUiFactory.CreateRect("Flea", row);
                buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(1, 0.5f);
                buttonRect.anchoredPosition = new Vector2(-6, 0);
                buttonRect.sizeDelta = new Vector2(70, 26);
                var button = UnityUiFactory.AddButton(buttonRect.gameObject, QuestGraphPalette.Control);
                button.interactable = !inRaid;
                var buttonLabel = UnityUiFactory.AddText(buttonRect.gameObject, ClientLocale.Text("label.flea"), 11,
                    TextAlignmentOptions.Center,
                    inRaid ? QuestGraphPalette.MutedText : Color.white);
                buttonLabel.fontStyle = FontStyles.Bold;
                UnityUiFactory.FitSingleLine(buttonLabel, 8f, 4f);
                var templateId = item.TemplateId;
                button.onClick.AddListener(() => OpenFlea(templateId));
                QuestMapNativeTooltips.Bind(buttonRect.gameObject, () => ClientLocale.Format(
                    inRaid ? "tooltip.searchFleaInRaid" : "tooltip.searchFlea",
                    ClientLocale.Arg("item", item.Name)));
            }
            y += rowHeight + gap;
        }
    }

    private long OwnedItemCount(string templateId)
    {
        try
        {
            return _inventoryController.Inventory.GetAllItemByTemplate(templateId)
                .Where(item => item is not Mod || !HasWeaponAncestor(item))
                .Sum(item => (long)Math.Max(0, item.StackObjectsCount));
        }
        catch (Exception exception)
        {
            _workspace.Log.LogWarning($"QUESTMAP_RELEVANT_ITEM_COUNT_ERROR template={templateId}; {exception.Message}");
            return 0;
        }
    }

    private static bool HasWeaponAncestor(Item item)
    {
        var parent = item.Parent?.Container?.ParentItem;
        for (var depth = 0; parent is not null && depth < 32; depth++)
        {
            if (parent is Weapon) return true;
            parent = parent.Parent?.Container?.ParentItem;
        }
        return false;
    }

    private void OpenFlea(string templateId)
    {
        if (_disposed || InRaidQuestContext.TryCapture(out _)) return;
        try
        {
            var itemUiContext = ItemUiContext.Instance;
            if (itemUiContext is null)
            {
                _workspace.Log.LogWarning($"QUESTMAP_RELEVANT_ITEM_FLEA_UNAVAILABLE item={templateId}; reason=ItemUiContext unavailable");
                return;
            }

            // FilterSearch is Tarkov's native "Filter by item" action. LinkedSearch instead
            // searches for compatible items and can select a category with no offers.
            itemUiContext.ExternalRagfairSearch(new GClass3943(EFilterType.FilterSearch, templateId, true));
        }
        catch (Exception exception)
        {
            _workspace.Log.LogError($"QUESTMAP_RELEVANT_ITEM_FLEA_ERROR item={templateId}; {exception}");
        }
    }

    private void OpenWiki(string wikiUrl)
    {
        if (_disposed
            || !Uri.TryCreate(wikiUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        try
        {
            // Unity delegates the URL to Windows' configured browser and
            // returns immediately without waiting for that browser process.
            Application.OpenURL(uri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            _workspace.Log.LogError($"QUESTMAP_WIKI_ERROR quest={_selectedQuestId}; {exception}");
        }
    }

    private void SelectTextTab(DetailTextTab tab)
    {
        if (_disposed || _selectedQuestId is null || _topology is null || _overlay is null || _textTab == tab) return;
        _textTab = tab;
        Show(_topology, _overlay, _selectedQuestId);
    }

    private void BuildObjectives(
        RectTransform parent,
        QuestGraphNode node,
        QuestLiveState? liveState,
        QuestClass? liveQuest,
        bool future,
        float height)
    {
        var section = StackRect("ObjectivesSection", parent, height);
        var progress = liveState is null ? null : QuestGraphRules.CalculateObjectiveProgressPercent(liveState.Objectives);
        var displayState = QuestGraphRules.ClassifyProfileDisplayState(_topology!, node, _overlay!);
        if (displayState is QuestMapDisplayStateKind.ReadyToFinish or QuestMapDisplayStateKind.Completed) progress = 100d;
        var title = UnityUiFactory.AddText(section.gameObject,
            progress.HasValue
                ? ClientLocale.Format("label.overallTasks", ClientLocale.Arg("percent", progress.Value))
                : ClientLocale.Text("label.tasks"),
            14, TextAlignmentOptions.TopLeft, Color.white);
        title.margin = new Vector4(10, 5, 10, 0);
        title.fontStyle = FontStyles.Bold;
        if (progress.HasValue) AddProgressBar(section, progress.Value / 100d, 10, height - 31, QuestGraphPalette.Status(displayState));

        var progressById = (liveState?.Objectives ?? [])
            .GroupBy(value => value.ObjectiveId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var conditions = LiveConditions(liveQuest);
        var y = 0f;
        var orderedObjectives = node.Objectives.Any(objective => !objective.ContributesToProgress)
            ? node.Objectives.ToArray()
            : node.Objectives
                .OrderBy(value => value.Index ?? int.MaxValue)
                .ThenBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();
        var contentHeight = orderedObjectives.Length == 0
            ? 42f
            : orderedObjectives.Select((objective, index) =>
            {
                progressById.TryGetValue(objective.Id, out var objectiveProgress);
                return ObjectiveRowHeight(objective, objectiveProgress) + (index < orderedObjectives.Length - 1 ? 3f : 0f);
            }).Sum();
        var content = CreateExpandedRegion(section, "ObjectivesContent", 38, contentHeight);
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
            QuestObjectiveSkipSlot? skipSlot = null;
            GameObject? handoverObject = null;
            var canHandover = !future
                && _workspace.MutationsAllowed()
                && liveQuest?.QuestStatus == EQuestStatus.Started
                && objectiveProgress?.Complete != true
                && condition is ConditionHandoverItem or ConditionWeaponAssembly;
            if (canHandover && condition is not null)
            {
                var buttonRect = UnityUiFactory.CreateRect("Handover", row);
                buttonRect.anchorMin = new Vector2(0, 0.15f);
                buttonRect.anchorMax = new Vector2(0, 0.85f);
                buttonRect.pivot = new Vector2(0, 0.5f);
                buttonRect.anchoredPosition = new Vector2(5, 0);
                buttonRect.sizeDelta = new Vector2(actionWidth - 10, 0);
                handoverObject = buttonRect.gameObject;
                var button = UnityUiFactory.AddButton(buttonRect.gameObject, QuestGraphPalette.ControlActive);
                button.interactable = false;
            var buttonText = UnityUiFactory.AddText(buttonRect.gameObject, "…", 9,
                    TextAlignmentOptions.Center, Color.white);
            buttonText.fontStyle = FontStyles.Bold;
            QuestMapNativeTooltips.Bind(buttonRect.gameObject, () => ClientLocale.Format(
                button.interactable ? "tooltip.handover" : "tooltip.handoverPending",
                ClientLocale.Arg("objective", objective.Text)));
                var capturedCondition = condition;
                NativeQuestTableActions.ResolveHandoverDeferred(
                    row,
                    true,
                    () => !_disposed
                        && row != null
                        && string.Equals(_selectedQuestId, node.Id, StringComparison.Ordinal),
                    () => NativeQuestTableActions.TryCreateHandover(
                        _nativeHostRoot, liveQuest!, capturedCondition.id, _questController, _inventoryController),
                    objectiveHost =>
                    {
                        if (objectiveHost is null)
                        {
                            if (skipSlot is not null) skipSlot.SetFallbackAvailable(false);
                            else buttonRect.gameObject.SetActive(false);
                            return;
                        }

                        _objectiveHosts.Add(objectiveHost);
                        buttonText.text = ClientLocale.Text("label.handOver");
                        button.interactable = true;
                        button.onClick.AddListener(() => RunNativeAction(
                            objectiveHost.Execute, QuestDetailsActionKind.Handover, button, objective.Id));
                    },
                    _workspace.Log,
                    node.Id,
                    objective.Id);
            }

            var canSkip = !future
                && _workspace.MutationsAllowed()
                && liveQuest?.QuestStatus == EQuestStatus.Started
                && objectiveProgress?.Complete != true
                && objective.ContributesToProgress
                && condition is not null;
            if (canSkip && liveQuest is not null)
            {
                var skipRect = UnityUiFactory.CreateRect("Skip", row);
                skipRect.anchorMin = new Vector2(0, 0.15f);
                skipRect.anchorMax = new Vector2(0, 0.85f);
                skipRect.pivot = new Vector2(0, 0.5f);
                skipRect.anchoredPosition = new Vector2(5, 0);
                skipRect.sizeDelta = new Vector2(actionWidth - 10, 0);
                var skipButton = UnityUiFactory.AddButton(skipRect.gameObject, QuestGraphPalette.Failed);
                var skipText = UnityUiFactory.AddText(skipRect.gameObject, ClientLocale.Text("label.skip"), 9,
                    TextAlignmentOptions.Center, Color.white);
                skipText.fontStyle = FontStyles.Bold;
                UnityUiFactory.FitSingleLine(skipText, 7f, 3f);
                skipButton.onClick.AddListener(() => RequestObjectiveSkip(node, objective, liveQuest));
                QuestMapNativeTooltips.Bind(skipRect.gameObject, () => ClientLocale.Format("tooltip.skip",
                    ClientLocale.Arg("objective", objective.Text)));
                skipSlot = _skipVisibility.Register(skipRect.gameObject, handoverObject);
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
                    ? ClientLocale.Format("common.progress",
                        ClientLocale.Arg("current", Math.Min(objectiveProgress!.Current ?? 0, objectiveProgress.Required ?? objective.RequiredValue ?? 0)),
                        ClientLocale.Arg("required", objectiveProgress.Required ?? objective.RequiredValue ?? 0))
                    : string.Empty;
            var value = UnityUiFactory.AddText(valueRect.gameObject, valueText, 12,
                TextAlignmentOptions.Center, future ? new Color(0.45f, 0.46f, 0.46f, 1f) : QuestGraphPalette.MutedText);
            if (objectiveProgress?.Complete == true) value.color = QuestGraphPalette.Completed;

            var descriptionRect = UnityUiFactory.CreateRect("Text", row);
            descriptionRect.anchorMin = Vector2.zero;
            descriptionRect.anchorMax = Vector2.one;
            var nestedIndent = objective.ContributesToProgress ? 0f : 16f;
            descriptionRect.offsetMin = new Vector2(actionWidth + valueWidth + nestedIndent, numeric && objectiveProgress?.Complete != true ? 7 : 3);
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
            UnityUiFactory.AddText(empty.gameObject, ClientLocale.Text("label.noObjectives"), 13,
                TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
            y = 42;
        }
    }

    private float BuildRewards(RectTransform parent, QuestGraphNode node, QuestClass? liveQuest, bool future)
    {
        var rewards = node.Rewards.Where(reward => _workspace.ShowHiddenRewards() || !reward.Hidden).ToArray();
        var fallbackContentHeight = rewards.Length == 0 ? 42f : rewards.Length * 47f;
        var section = StackRect("RewardsSection", parent, 32f + fallbackContentHeight);
        var title = UnityUiFactory.AddText(section.gameObject, ClientLocale.Text("label.rewards"), 14,
            TextAlignmentOptions.TopLeft, Color.white);
        title.margin = new Vector4(10, 5, 10, 0);
        title.fontStyle = FontStyles.Bold;
        var content = CreateExpandedRegion(section, "RewardsContent", 28, fallbackContentHeight);

        if (!future && liveQuest is not null
            && TryBuildNativeRewards(node, liveQuest, section, parent, content, out var nativeHeight))
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, nativeHeight);
            SetPreferredHeight(section, 32f + nativeHeight);
            return 32f + nativeHeight;
        }

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
            UnityUiFactory.AddText(empty.gameObject, ClientLocale.Text("label.noRewards"), 13,
                TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
            y = 42;
        }
        content.sizeDelta = new Vector2(content.sizeDelta.x, y);
        SetPreferredHeight(section, 32f + y);
        return 32f + y;
    }

    private bool TryBuildNativeRewards(
        QuestGraphNode node,
        QuestClass quest,
        RectTransform section,
        RectTransform layoutRoot,
        RectTransform content,
        out float height)
    {
        height = 0;
        var source = NativeQuestViewSource.FindForDetails();
        var prefab = source is null ? null : NativeQuestViewSource.GetRewardListPrefab(source);
        if (prefab is null || !quest.Template.Rewards.TryGetValue(EQuestStatus.Success, out var rawRewards)) return false;
        var rewards = rawRewards.Where((_, index) =>
            _workspace.ShowHiddenRewards() || index >= node.Rewards.Count || !node.Rewards[index].Hidden).ToArray();
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
                .Bind(container, content, section.GetComponent<LayoutElement>(), layoutRoot, 32f, rows);
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
            ClientLocale.Text("label.detailsFailed"),
            14, TextAlignmentOptions.Center, QuestGraphPalette.MutedText);
        message.margin = new Vector4(28, 80, 28, 28);
    }

    private void ClearNativeObjectiveHosts()
    {
        _skipVisibility.Clear();
        foreach (var host in _objectiveHosts)
        {
            if (host == null) continue;
            host.Dispose();
        }
        _objectiveHosts.Clear();
    }

    private void RequestObjectiveSkip(QuestGraphNode node, QuestObjectiveDefinition objective, QuestClass quest)
    {
        if (_disposed || !_workspace.TaskSkippingEnabled() || !_workspace.MutationsAllowed()) return;
        NativeQuestObjectiveSkip.ShowConfirmation(
            _questController,
            quest,
            objective.Id,
            node.Name,
            objective.Text,
            () => !_disposed && _workspace.TaskSkippingEnabled() && _workspace.MutationsAllowed(),
            () =>
            {
                if (!_disposed) _requestQuestRefresh(node.Id, QuestDetailsActionKind.SkipObjective);
            },
            _workspace.Log);
    }

    private async void RunNativeAction(
        Func<Task> action,
        QuestDetailsActionKind kind,
        Button button,
        string? objectiveId = null)
    {
        if (_disposed || !button.interactable || !_workspace.MutationsAllowed()) return;
        var questId = _selectedQuestId;
        var protectedKind = kind switch
        {
            QuestDetailsActionKind.Accept or QuestDetailsActionKind.Restart => NativeQuestActionKind.Accept,
            QuestDetailsActionKind.Complete => NativeQuestActionKind.Complete,
            QuestDetailsActionKind.Handover => NativeQuestActionKind.Handover,
            _ => (NativeQuestActionKind?)null,
        };
        var pressedAt = 0f;
        if (protectedKind.HasValue
            && (string.IsNullOrWhiteSpace(questId)
                || !_workspace.TryBeginProtectedAction(questId, protectedKind.Value, objectiveId, out pressedAt)))
            return;

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
            _workspace.Log.LogError($"QUESTMAP_M07_ACTION_ERROR quest={_selectedQuestId}; {exception}");
        }
        finally
        {
            if (protectedKind.HasValue)
                await NativeQuestWorkspaceContext.WaitForProtectedActionCooldown(pressedAt);
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

    private static RectTransform TopRect(string name, RectTransform parent, float top, float height)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = new Vector2(0, -top);
        rect.sizeDelta = new Vector2(0, height);
        return rect;
    }

    private static RectTransform StackRect(string name, RectTransform parent, float height)
    {
        var rect = TopRect(name, parent, 0, height);
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        layout.flexibleHeight = 0;
        return rect;
    }

    private void AddFixedDivider(float top)
    {
        var divider = TopRect("HeaderDivider", Root, top, 1);
        divider.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
    }

    private static void AddStackDivider(RectTransform parent)
    {
        var divider = StackRect("Divider", parent, 1);
        divider.gameObject.AddComponent<Image>().color = QuestGraphPalette.Border;
    }

    private static void SetPreferredHeight(RectTransform section, float height)
    {
        var layout = section.GetComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
    }

    private static RectTransform CreateExpandedRegion(
        RectTransform parent,
        string name,
        float top,
        float height)
    {
        var content = UnityUiFactory.CreateRect(name, parent);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = new Vector2(-2.5f, -top);
        content.sizeDelta = new Vector2(-15, Mathf.Max(1f, height));
        return content;
    }

    private static void ConfigureExpandingBody(RectTransform content)
    {
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = SectionGap;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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

    private static void CreateExpandedText(
        RectTransform parent,
        RectTransform layoutRoot,
        string name,
        string text,
        float top,
        float bottom,
        float fontSize,
        Color color,
        float chromeHeight,
        float minimumHeight)
    {
        var content = CreateExpandedRegion(parent, name, top, Mathf.Max(1f, parent.rect.height - top - bottom));
        var value = string.IsNullOrWhiteSpace(text) ? ClientLocale.Text("common.noDescription") : text;
        var label = UnityUiFactory.AddText(content.gameObject,
            value,
            fontSize,
            TextAlignmentOptions.TopLeft,
            color);
        label.margin = new Vector4(9, 7, 9, 7);
        label.richText = true;
        label.enableWordWrapping = true;
        // Start with a safe estimate, then replace the section's preferred
        // height with TMP's actual rendered bounds after font/material layout.
        // Measuring preferredHeight synchronously here can dereference an
        // uninitialised TMP material during an EFT screen transition.
        content.gameObject.AddComponent<QuestDetailsTextContentSizer>()
            .Bind(label, content, parent.GetComponent<LayoutElement>(), layoutRoot, chromeHeight, minimumHeight);
    }

    private static void AddTab(
        RectTransform parent,
        string name,
        string label,
        float minimum,
        float maximum,
        bool active,
        Action action,
        Func<string>? tooltip)
    {
        var rect = UnityUiFactory.CreateRect(name, parent);
        rect.anchorMin = new Vector2(minimum, 1);
        rect.anchorMax = new Vector2(maximum, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(-2, 30);
        var button = UnityUiFactory.AddButton(rect.gameObject, active ? QuestGraphPalette.ControlActive : QuestGraphPalette.Control);
        var text = UnityUiFactory.AddText(rect.gameObject, label, 11, TextAlignmentOptions.Center, Color.white);
        text.fontStyle = FontStyles.Bold;
        UnityUiFactory.FitSingleLine(text, 8f, 5f);
        button.onClick.AddListener(() => action());
        if (tooltip is not null) QuestMapNativeTooltips.Bind(rect.gameObject, tooltip);
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
        var hidden = reward.Hidden ? ClientLocale.Text("common.hiddenPrefix") : string.Empty;
        if (reward.Items.Count > 0)
            return hidden + string.Join(ClientLocale.Text("common.listSeparator"), reward.Items.Select(item =>
                ClientLocale.Format("reward.item", ClientLocale.Arg("count", item.Count), ClientLocale.Arg("item", item.Name))));
        var subject = reward.TargetName ?? reward.TraderName ?? reward.TargetId;
        return ClientLocale.Format("reward.line",
            ClientLocale.Arg("hidden", hidden),
            ClientLocale.Arg("type", reward.Type),
            ClientLocale.Arg("subject", string.IsNullOrWhiteSpace(subject) ? string.Empty : ClientLocale.Format("reward.subject", ClientLocale.Arg("subject", subject))),
            ClientLocale.Arg("value", reward.Value.HasValue ? ClientLocale.Format("reward.value", ClientLocale.Arg("value", reward.Value.Value)) : string.Empty),
            ClientLocale.Arg("loyalty", reward.LoyaltyLevel.HasValue ? ClientLocale.Format("reward.loyalty", ClientLocale.Arg("level", reward.LoyaltyLevel.Value)) : string.Empty));
    }

    private static void AddTextShadow(TMP_Text text)
    {
        var shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, 0.96f);
        shadow.effectDistance = new Vector2(1, -1);
    }

    private static bool IsRaidOnlyTrader(string traderId) => NativeQuestTableActions.IsRaidOnlyTrader(traderId);

    private string SelectedQuestName() =>
        _selectedQuestId is not null && _topology?.NodesById.TryGetValue(_selectedQuestId, out var node) == true
            ? node.Name
            : ClientLocale.Text("label.questDescription");

    private static void AddRouteBar(RectTransform parent, bool collector, bool lightkeeper)
    {
        if (!collector && !lightkeeper) return;
        const float height = 12f;
        if (collector) AddRouteBarPart(parent, "CollectorRoute", ClientLocale.Text("legend.collector"), QuestGraphPalette.Collector,
            0f, lightkeeper ? 0.5f : 1f, height);
        if (lightkeeper) AddRouteBarPart(parent, "LightkeeperRoute", ClientLocale.Text("legend.lightkeeper"), QuestGraphPalette.Lightkeeper,
            collector ? 0.5f : 0f, 1f, height);
    }

    private void AddWikiButton(RectTransform parent, string wikiUrl)
    {
        var rect = UnityUiFactory.CreateRect("Wiki", parent);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-10, -10);
        rect.sizeDelta = new Vector2(74, 28);
        var button = UnityUiFactory.AddButton(rect.gameObject, new Color(0.20f, 0.22f, 0.19f, 0.96f));
        var label = UnityUiFactory.AddText(rect.gameObject, ClientLocale.Text("label.wiki"), 11, TextAlignmentOptions.Center,
            new Color(0.95f, 0.90f, 0.72f, 1f));
        label.fontStyle = FontStyles.Bold;
        UnityUiFactory.FitSingleLine(label, 8f, 4f);
        button.onClick.AddListener(() => OpenWiki(wikiUrl));
        QuestMapNativeTooltips.Bind(rect.gameObject, () => ClientLocale.Format("tooltip.openWiki",
            ClientLocale.Arg("quest", SelectedQuestName())));
    }

    private static void AddScavBadge(RectTransform parent, float top)
    {
        var badge = UnityUiFactory.CreateRect("ScavBadge", parent);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1, 1);
        badge.anchoredPosition = new Vector2(-10, -top);
        badge.sizeDelta = new Vector2(58, 22);
        badge.gameObject.AddComponent<Image>().color = new Color(0.40f, 0.34f, 0.16f, 0.96f);
        var label = UnityUiFactory.AddText(badge.gameObject, ClientLocale.Text("common.scav"), 10,
            TextAlignmentOptions.Center, new Color(1f, 0.96f, 0.78f, 1f));
        label.fontStyle = FontStyles.Bold;
        UnityUiFactory.FitSingleLine(label, 7f, 3f);
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
        RelevantItems,
    }

}

internal enum QuestDetailsActionKind
{
    Accept,
    Restart,
    Complete,
    Replace,
    Handover,
    SkipObjective,
}

internal sealed class QuestDetailsTextContentSizer : MonoBehaviour
{
    private TextMeshProUGUI? _label;
    private RectTransform? _content;
    private LayoutElement? _sectionLayout;
    private RectTransform? _layoutRoot;
    private float _chromeHeight;
    private float _minimumHeight;
    private int _attempts;

    public QuestDetailsTextContentSizer Bind(
        TextMeshProUGUI label,
        RectTransform content,
        LayoutElement sectionLayout,
        RectTransform layoutRoot,
        float chromeHeight,
        float minimumHeight)
    {
        _label = label;
        _content = content;
        _sectionLayout = sectionLayout;
        _layoutRoot = layoutRoot;
        _chromeHeight = chromeHeight;
        _minimumHeight = minimumHeight;
        return this;
    }

    private void LateUpdate()
    {
        if (_label is null || _content is null || _sectionLayout is null || _layoutRoot is null)
        {
            enabled = false;
            return;
        }
        if (++_attempts < 2 || _label.font is null || _label.rectTransform.rect.width <= 1f) return;

        try
        {
            _label.ForceMeshUpdate(true, true);
            var renderedHeight = _label.GetRenderedValues(false).y;
            var contentHeight = Mathf.Max(1f, renderedHeight + _label.margin.y + _label.margin.w + 4f);
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, contentHeight);
            var exactHeight = Mathf.Max(_minimumHeight,
                _chromeHeight + contentHeight);
            _sectionLayout.minHeight = exactHeight;
            _sectionLayout.preferredHeight = exactHeight;
            LayoutRebuilder.MarkLayoutForRebuild(_layoutRoot);
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
    private LayoutElement? _sectionLayout;
    private RectTransform? _layoutRoot;
    private float _chromeHeight;
    private int _rows;
    private int _passes;

    public QuestDetailsRewardLayoutNormalizer Bind(
        RectTransform container,
        RectTransform content,
        LayoutElement sectionLayout,
        RectTransform layoutRoot,
        float chromeHeight,
        int rows)
    {
        _container = container;
        _content = content;
        _sectionLayout = sectionLayout;
        _layoutRoot = layoutRoot;
        _chromeHeight = chromeHeight;
        _rows = Mathf.Max(1, rows);
        return this;
    }

    private void LateUpdate()
    {
        if (_container is null || _content is null || _sectionLayout is null || _layoutRoot is null)
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
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, height);
            var sectionHeight = _chromeHeight + height;
            _sectionLayout.minHeight = sectionHeight;
            _sectionLayout.preferredHeight = sectionHeight;
            LayoutRebuilder.MarkLayoutForRebuild(_layoutRoot);

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
