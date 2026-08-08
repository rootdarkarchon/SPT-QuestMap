using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;

namespace SPTQuestMap.Client.Data;

internal sealed class RaidQuestProgressMonitor : IDisposable
{
    private readonly AbstractQuestControllerClass _controller;
    private readonly Action<string, string?, RaidQuestChangeKind> _changed;
    private readonly HashSet<QuestClass> _quests = new(ReferenceComparer<QuestClass>.Instance);
    private readonly Dictionary<ConditionProgressChecker, CheckerBinding> _bindings =
        new(ReferenceComparer<ConditionProgressChecker>.Instance);
    private readonly List<CheckerBinding> _pollBindings = new();
    private int _pollIndex;
    private bool _disposed;

    public RaidQuestProgressMonitor(
        AbstractQuestControllerClass controller,
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

        var currentQuests = new HashSet<QuestClass>(_controller.Quests, ReferenceComparer<QuestClass>.Instance);
        foreach (var quest in _quests.Where(quest => !currentQuests.Contains(quest)).ToArray())
            UnsubscribeQuest(quest);
        foreach (var quest in currentQuests) SubscribeQuest(quest);

        var currentCheckers = new Dictionary<ConditionProgressChecker, CheckerBinding>(
            ReferenceComparer<ConditionProgressChecker>.Instance);
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

    private void SubscribeQuest(QuestClass quest)
    {
        if (!_quests.Add(quest)) return;
        quest.OnStatusChanged += OnQuestStatusChanged;
    }

    private void UnsubscribeQuest(QuestClass quest)
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

    private void OnQuestStatusChanged(QuestClass quest, bool _)
    {
        _changed(quest.Id, null, RaidQuestChangeKind.Status);
        Resync();
    }

    private void OnNewQuestAdded(QuestClass quest)
    {
        Resync();
        _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnQuestAdded(QuestClass quest)
    {
        Resync();
        _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnQuestRemoved(QuestClass quest)
    {
        Resync();
        _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnQuestsAdded(IEnumerable<QuestClass> quests)
    {
        Resync();
        foreach (var quest in quests) _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnQuestsRemoved(IEnumerable<QuestClass> quests)
    {
        Resync();
        foreach (var quest in quests) _changed(quest.Id, null, RaidQuestChangeKind.Book);
    }

    private void OnAllQuestsRemoved()
    {
        Resync();
        _changed(string.Empty, null, RaidQuestChangeKind.Book);
    }

    private static bool IsActive(QuestClass quest) =>
        quest.QuestStatus == EQuestStatus.Started
        || quest.QuestStatus == EQuestStatus.AvailableForFinish
        || quest.QuestStatus == EQuestStatus.MarkedAsFailed;

    private sealed class CheckerBinding
    {
        public CheckerBinding(
            QuestClass quest,
            string objectiveId,
            ConditionProgressChecker checker,
            double lastValue)
        {
            Quest = quest;
            ObjectiveId = objectiveId;
            Checker = checker;
            LastValue = lastValue;
        }

        public QuestClass Quest { get; }

        public string ObjectiveId { get; }

        public ConditionProgressChecker Checker { get; }

        public double LastValue { get; set; }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static ReferenceComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
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
