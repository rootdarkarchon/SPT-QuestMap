using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using EFT.Quests;

namespace SPTQuestMap.Client.Data;

internal sealed class ReactiveQuestMonitor : IDisposable
{
    private readonly AbstractQuestControllerClass _controller;
    private readonly Action<string, string?> _invalidate;
    private readonly HashSet<QuestClass> _quests = new(ReferenceComparer<QuestClass>.Instance);
    private readonly HashSet<ConditionProgressChecker> _checkers = new(ReferenceComparer<ConditionProgressChecker>.Instance);
    private readonly HashSet<Profile.TraderInfo> _traders = new(ReferenceComparer<Profile.TraderInfo>.Instance);
    private InventoryController? _inventoryController;
    private bool _disposed;

    public ReactiveQuestMonitor(
        AbstractQuestControllerClass controller,
        Action<string, string?> invalidate)
    {
        _controller = controller;
        _invalidate = invalidate;

        controller.OnConditionalStatusChanged += OnConditionalStatusChanged;
        controller.OnNewQuestsAdded += OnNewQuestAdded;
        controller.Quests.ItemAdded += OnQuestAdded;
        controller.Quests.ItemRemoved += OnQuestRemoved;
        controller.Quests.ItemsAdded += OnQuestsAdded;
        controller.Quests.ItemsRemoved += OnQuestsRemoved;
        controller.Quests.ItemUpdated += OnQuestUpdated;
        controller.Quests.AllItemsRemoved += OnAllQuestsRemoved;
        controller.Quests.OnQuestExpired += OnQuestExpired;
        controller.Profile.OnTraderStandingChanged += OnProfileTraderChanged;
        controller.Profile.OnTraderLoyaltyChanged += OnProfileTraderChanged;
        Resync();
    }

    public void SetInventoryController(InventoryController? inventoryController)
    {
        if (ReferenceEquals(_inventoryController, inventoryController)) return;
        var monitoredTransactions = _inventoryController is not null;
        if (_inventoryController is not null) _inventoryController.OnProfileUpdate -= OnInventoryProfileUpdate;
        _inventoryController = inventoryController;
        if (_inventoryController is not null) _inventoryController.OnProfileUpdate += OnInventoryProfileUpdate;
        // SalesSum changes on every trader transaction, so scope it to the same
        // visible quest-screen lifetime as inventory-driven objective refreshes.
        var monitorTransactions = _inventoryController is not null;
        if (monitoredTransactions == monitorTransactions) return;
        foreach (var trader in _traders)
        {
            if (monitorTransactions) trader.OnSalesSumChanged += OnTraderSalesSumChanged;
            else trader.OnSalesSumChanged -= OnTraderSalesSumChanged;
        }
    }

    public void Resync()
    {
        if (_disposed) return;

        var currentQuests = new HashSet<QuestClass>(_controller.Quests, ReferenceComparer<QuestClass>.Instance);
        foreach (var quest in _quests.Where(quest => !currentQuests.Contains(quest)).ToArray()) UnsubscribeQuest(quest);
        foreach (var quest in currentQuests) SubscribeQuest(quest);

        var currentCheckers = new HashSet<ConditionProgressChecker>(
            currentQuests.SelectMany(quest => quest.ProgressCheckers.Values),
            ReferenceComparer<ConditionProgressChecker>.Instance);
        foreach (var checker in _checkers.Where(checker => !currentCheckers.Contains(checker)).ToArray()) UnsubscribeChecker(checker);
        foreach (var checker in currentCheckers) SubscribeChecker(checker);

        var currentTraders = new HashSet<Profile.TraderInfo>(
            _controller.Profile.TradersInfo.Values,
            ReferenceComparer<Profile.TraderInfo>.Instance);
        foreach (var trader in _traders.Where(trader => !currentTraders.Contains(trader)).ToArray()) UnsubscribeTrader(trader);
        foreach (var trader in currentTraders) SubscribeTrader(trader);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SetInventoryController(null);
        _controller.OnConditionalStatusChanged -= OnConditionalStatusChanged;
        _controller.OnNewQuestsAdded -= OnNewQuestAdded;
        _controller.Quests.ItemAdded -= OnQuestAdded;
        _controller.Quests.ItemRemoved -= OnQuestRemoved;
        _controller.Quests.ItemsAdded -= OnQuestsAdded;
        _controller.Quests.ItemsRemoved -= OnQuestsRemoved;
        _controller.Quests.ItemUpdated -= OnQuestUpdated;
        _controller.Quests.AllItemsRemoved -= OnAllQuestsRemoved;
        _controller.Quests.OnQuestExpired -= OnQuestExpired;
        _controller.Profile.OnTraderStandingChanged -= OnProfileTraderChanged;
        _controller.Profile.OnTraderLoyaltyChanged -= OnProfileTraderChanged;
        foreach (var checker in _checkers.ToArray()) UnsubscribeChecker(checker);
        foreach (var quest in _quests.ToArray()) UnsubscribeQuest(quest);
        foreach (var trader in _traders.ToArray()) UnsubscribeTrader(trader);
    }

    private void SubscribeQuest(QuestClass quest)
    {
        if (!_quests.Add(quest)) return;
        quest.OnStatusChanged += OnQuestStatusChanged;
        quest.OnConditionChanged += OnQuestConditionChanged;
    }

    private void UnsubscribeQuest(QuestClass quest)
    {
        if (!_quests.Remove(quest)) return;
        quest.OnStatusChanged -= OnQuestStatusChanged;
        quest.OnConditionChanged -= OnQuestConditionChanged;
    }

    private void SubscribeChecker(ConditionProgressChecker checker)
    {
        if (!_checkers.Add(checker)) return;
        checker.OnConditionChanged += OnCheckerChanged;
        checker.OnReset += OnCheckerReset;
        checker.OnDisconnect += OnCheckerDisconnected;
    }

    private void UnsubscribeChecker(ConditionProgressChecker checker)
    {
        if (!_checkers.Remove(checker)) return;
        checker.OnConditionChanged -= OnCheckerChanged;
        checker.OnReset -= OnCheckerReset;
        checker.OnDisconnect -= OnCheckerDisconnected;
    }

    private void SubscribeTrader(Profile.TraderInfo trader)
    {
        if (!_traders.Add(trader)) return;
        trader.OnAvailabilityChanged += OnTraderAvailabilityChanged;
        trader.OnStandingChanged += OnTraderStandingChanged;
        trader.OnLoyaltyChanged += OnTraderLoyaltyChanged;
        if (_inventoryController is not null) trader.OnSalesSumChanged += OnTraderSalesSumChanged;
    }

    private void UnsubscribeTrader(Profile.TraderInfo trader)
    {
        if (!_traders.Remove(trader)) return;
        trader.OnAvailabilityChanged -= OnTraderAvailabilityChanged;
        trader.OnStandingChanged -= OnTraderStandingChanged;
        trader.OnLoyaltyChanged -= OnTraderLoyaltyChanged;
        if (_inventoryController is not null) trader.OnSalesSumChanged -= OnTraderSalesSumChanged;
    }

    private void OnQuestStatusChanged(QuestClass quest, bool _) => _invalidate("quest-status", quest.Id);

    private void OnQuestConditionChanged(QuestClass quest) => _invalidate("quest-condition", quest.Id);

    private void OnCheckerChanged(ConditionProgressChecker _) => _invalidate("objective-progress", null);

    private void OnCheckerReset(ConditionProgressChecker _) => _invalidate("objective-reset", null);

    private void OnCheckerDisconnected(ConditionProgressChecker checker)
    {
        UnsubscribeChecker(checker);
        _invalidate("objective-disconnected", null);
    }

    private void OnConditionalStatusChanged() => _invalidate("conditional-status", null);

    private void OnNewQuestAdded(QuestClass quest)
    {
        SubscribeQuest(quest);
        _invalidate("new-quest", quest.Id);
    }

    private void OnQuestAdded(QuestClass quest)
    {
        SubscribeQuest(quest);
        _invalidate("quest-book-added", quest.Id);
    }

    private void OnQuestRemoved(QuestClass quest)
    {
        UnsubscribeQuest(quest);
        _invalidate("quest-book-removed", quest.Id);
    }

    private void OnQuestsAdded(IEnumerable<QuestClass> quests)
    {
        foreach (var quest in quests) SubscribeQuest(quest);
        _invalidate("quest-book-added-range", null);
    }

    private void OnQuestsRemoved(IEnumerable<QuestClass> quests)
    {
        foreach (var quest in quests) UnsubscribeQuest(quest);
        _invalidate("quest-book-removed-range", null);
    }

    private void OnQuestUpdated(QuestClass quest) => _invalidate("quest-book-updated", quest.Id);

    private void OnAllQuestsRemoved() => _invalidate("quest-book-cleared", null);

    private void OnQuestExpired(GClass3996 quest) => _invalidate("repeatable-expired", quest.Id);

    private void OnProfileTraderChanged(Profile.TraderInfo _) => _invalidate("trader-profile", null);

    private void OnTraderAvailabilityChanged() => _invalidate("trader-availability", null);

    private void OnTraderStandingChanged() => _invalidate("trader-standing", null);

    private void OnTraderLoyaltyChanged() => _invalidate("trader-loyalty", null);

    private void OnTraderSalesSumChanged() => _invalidate("trader-sales", null);

    private void OnInventoryProfileUpdate() => _invalidate("inventory-profile", null);

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static ReferenceComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
