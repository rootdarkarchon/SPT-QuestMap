using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;

namespace SPTQuestMap.Client.Data;

internal sealed class RaidQuestProgressMonitor : IDisposable
{
    private readonly EFT.Quests.QuestController _controller;
    private readonly Action<string, string?, RaidQuestChangeKind> _changed;
    private readonly HashSet<EFT.Quests.Quest> _quests = new(ReferenceEqualityComparer<EFT.Quests.Quest>.Instance);
    private readonly Dictionary<ConditionProgressChecker, CheckerBinding> _bindings =
        new(ReferenceEqualityComparer<ConditionProgressChecker>.Instance);
    private readonly List<CheckerBinding> _pollBindings = new();
    private int _pollIndex;
    private bool _disposed;

    public RaidQuestProgressMonitor(
        EFT.Quests.QuestController controller,
        Action<string, string?, RaidQuestChangeKind> changed)
    {
        _controller = controller;
        _changed = changed;
        controller.OnNewQuestsAdded += OnNewQuestAdded;
        controller.Quests.ItemAdded += OnQuestAdded;
        controller.Quests.ItemRemoved += OnQuestRemoved;
        controller.Quests.ItemsAdded += OnQuestsAdded;
        controller.Quests.ItemsRemoved += OnQuestsRemoved;
        controller.Quests.AllItemsRemoved += OnAllQuestsRemoved;
        Resync();
    }

    public RaidQuestPollResult Poll(int checkerBudget)
    {
        if (_disposed || checkerBudget <= 0 || _pollBindings.Count == 0)
            return default;

        var scanned = Math.Min(checkerBudget, _pollBindings.Count);
        var changes = 0;
        for (var index = 0; index < scanned; index++)
        {
            if (_pollIndex >= _pollBindings.Count) _pollIndex = 0;
            if (Observe(_pollBindings[_pollIndex++])) changes++;
        }

        return new RaidQuestPollResult(scanned, changes);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.OnNewQuestsAdded -= OnNewQuestAdded;
        _controller.Quests.ItemAdded -= OnQuestAdded;
        _controller.Quests.ItemRemoved -= OnQuestRemoved;
        _controller.Quests.ItemsAdded -= OnQuestsAdded;
        _controller.Quests.ItemsRemoved -= OnQuestsRemoved;
        _controller.Quests.AllItemsRemoved -= OnAllQuestsRemoved;
        foreach (var binding in _bindings.Values.ToArray()) UnsubscribeChecker(binding);
        foreach (var quest in _quests.ToArray()) UnsubscribeQuest(quest);
        _bindings.Clear();
        _pollBindings.Clear();
    }

    private void Resync()
    {
        if (_disposed) return;

        var currentQuests = new HashSet<EFT.Quests.Quest>(_controller.Quests, ReferenceEqualityComparer<EFT.Quests.Quest>.Instance);
        foreach (var quest in _quests.Where(quest => !currentQuests.Contains(quest)).ToArray())
            UnsubscribeQuest(quest);
        foreach (var quest in currentQuests) SubscribeQuest(quest);

        var currentCheckers = new Dictionary<ConditionProgressChecker, CheckerBinding>(
            ReferenceEqualityComparer<ConditionProgressChecker>.Instance);
        foreach (var quest in currentQuests.Where(IsActive))
        {
            foreach (var pair in quest.ProgressCheckers)
            {
                currentCheckers[pair.Value] = new CheckerBinding(
                    quest,
                    pair.Key.id.ToString(),
                    pair.Value,
                    pair.Value.CurrentValue);
            }
        }

        foreach (var binding in _bindings.Values
                     .Where(binding => !currentCheckers.ContainsKey(binding.Checker))
                     .ToArray())
        {
            UnsubscribeChecker(binding);
        }
        foreach (var candidate in currentCheckers.Values)
        {
            if (_bindings.ContainsKey(candidate.Checker)) continue;
            SubscribeChecker(candidate);
        }

        _pollBindings.Clear();
        _pollBindings.AddRange(_bindings.Values
            .OrderBy(binding => binding.Quest.Id, StringComparer.Ordinal)
            .ThenBy(binding => binding.ObjectiveId, StringComparer.Ordinal));
        if (_pollIndex >= _pollBindings.Count) _pollIndex = 0;
    }

    private void SubscribeQuest(EFT.Quests.Quest quest)
    {
        if (!_quests.Add(quest)) return;
        quest.OnStatusChanged += OnQuestStatusChanged;
    }

    private void UnsubscribeQuest(EFT.Quests.Quest quest)
    {
        if (!_quests.Remove(quest)) return;
        quest.OnStatusChanged -= OnQuestStatusChanged;
    }

    private void SubscribeChecker(CheckerBinding binding)
    {
        _bindings.Add(binding.Checker, binding);
        binding.Checker.OnConditionChanged += OnCheckerChanged;
        binding.Checker.OnReset += OnCheckerReset;
        binding.Checker.OnDisconnect += OnCheckerDisconnected;
    }

    private void UnsubscribeChecker(CheckerBinding binding)
    {
        if (!_bindings.Remove(binding.Checker)) return;
        binding.Checker.OnConditionChanged -= OnCheckerChanged;
        binding.Checker.OnReset -= OnCheckerReset;
        binding.Checker.OnDisconnect -= OnCheckerDisconnected;
    }

    private bool Observe(CheckerBinding binding)
    {
        var current = binding.Checker.CurrentValue;
        if (Math.Abs(current - binding.LastValue) <= 0.0001d) return false;
        binding.LastValue = current;
        _changed(binding.Quest.Id, binding.ObjectiveId, RaidQuestChangeKind.Objective);
        return true;
    }

    private void OnCheckerChanged(ConditionProgressChecker checker)
    {
        if (_bindings.TryGetValue(checker, out var binding)) Observe(binding);
    }

    private void OnCheckerReset(ConditionProgressChecker checker)
    {
        if (_bindings.TryGetValue(checker, out var binding)) Observe(binding);
    }

    private void OnCheckerDisconnected(ConditionProgressChecker checker)
    {
        if (_bindings.TryGetValue(checker, out var binding)) UnsubscribeChecker(binding);
        Resync();
    }

    private void OnQuestStatusChanged(EFT.Quests.Quest quest, bool _)
    {
        _changed(quest.Id, null, RaidQuestChangeKind.Status);
        Resync();
    }

    private void OnNewQuestAdded(EFT.Quests.Quest quest)
    {
        Resync();
        _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnQuestAdded(EFT.Quests.Quest quest)
    {
        Resync();
        _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnQuestRemoved(EFT.Quests.Quest quest)
    {
        Resync();
        _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnQuestsAdded(IEnumerable<EFT.Quests.Quest> quests)
    {
        Resync();
        foreach (var quest in quests) _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnQuestsRemoved(IEnumerable<EFT.Quests.Quest> quests)
    {
        Resync();
        foreach (var quest in quests) _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnAllQuestsRemoved()
    {
        Resync();
        _changed(string.Empty, null, RaidQuestChangeKind.Book);
    }

    private static bool IsActive(EFT.Quests.Quest quest) =>
        quest.QuestStatus == EQuestStatus.Started
        || quest.QuestStatus == EQuestStatus.AvailableForFinish
        || quest.QuestStatus == EQuestStatus.MarkedAsFailed;

    private sealed class CheckerBinding
    {
        public CheckerBinding(
            EFT.Quests.Quest quest,
            string objectiveId,
            ConditionProgressChecker checker,
            double lastValue)
        {
            Quest = quest;
            ObjectiveId = objectiveId;
            Checker = checker;
            LastValue = lastValue;
        }

        public EFT.Quests.Quest Quest { get; }

        public string ObjectiveId { get; }

        public ConditionProgressChecker Checker { get; }

        public double LastValue { get; set; }
    }

}

internal enum RaidQuestChangeKind
{
    Objective,
    Status,
    Book,
}

internal readonly struct RaidQuestPollResult
{
    public RaidQuestPollResult(int scannedCheckers, int changes)
    {
        ScannedCheckers = scannedCheckers;
        Changes = changes;
    }

    public int ScannedCheckers { get; }

    public int Changes { get; }
}
