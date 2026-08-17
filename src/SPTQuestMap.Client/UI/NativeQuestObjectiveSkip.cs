using System;
using System.Linq;
using BepInEx.Configuration;
using SPTQuestMap.Client.Input;
using BepInEx.Logging;
using SPTQuestMap.Client.Localization;
using EFT;
using EFT.Quests;
using EFT.UI;
using SPTQuestMap.Client.Diagnostics;
using SPTQuestMap.Core.Models;
using SPTQuestMap.Core.Rules;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

/// <summary>
/// Exact EFT 0.16.9.40087 objective-completion bridge. This deliberately
/// changes one condition only; quest turn-in remains Tarkov-owned.
/// </summary>
internal static class NativeQuestObjectiveSkip
{
    public static void ShowConfirmation(
        AbstractQuestControllerClass questController,
        QuestClass quest,
        QuestObjectiveDefinition objective,
        string questName,
        Func<bool> stillAllowed,
        Action completed,
        ManualLogSource log)
    {
        var objectiveId = objective.Id;
        if (!stillAllowed())
        {
            QuestMapDebugLog.Info(log,
                $"QUESTMAP_OBJECTIVE_SKIP_AVAILABLE quest={quest.Id}; objective={objectiveId}; available=False; reason=feature or screen state changed");
            return;
        }

        if (!TryResolve(questController, quest, objective, out _, out _, out _, out var reason))
        {
            QuestMapDebugLog.Info(log,
                $"QUESTMAP_OBJECTIVE_SKIP_AVAILABLE quest={quest.Id}; objective={objectiveId}; available=False; reason={reason}");
            return;
        }

        var context = ItemUiContext.Instance;
        if (context == null)
        {
            log.LogWarning(
                $"QUESTMAP_OBJECTIVE_SKIP quest={quest.Id}; objective={objectiveId}; completed=False; reason=ItemUiContext unavailable");
            return;
        }

        context.ShowMessageWindow(
            description: ClientLocale.Format("skip.confirmation",
                ClientLocale.Arg("quest", questName), ClientLocale.Arg("objective", objective.Text)),
            acceptAction: () =>
            {
                if (!stillAllowed())
                {
                    log.LogWarning(
                        $"QUESTMAP_OBJECTIVE_SKIP quest={quest.Id}; objective={objectiveId}; completed=False; reason=feature or screen state changed");
                    return;
                }

                if (!TrySkip(questController, quest, objective, log)) return;
                completed();
            },
            cancelAction: () => { },
            caption: ClientLocale.Text("skip.caption"));
    }

    private static bool TrySkip(
        AbstractQuestControllerClass questController,
        QuestClass quest,
        QuestObjectiveDefinition objective,
        ManualLogSource log)
    {
        var objectiveId = objective.Id;
        if (!TryResolve(questController, quest, objective, out var condition, out var checker,
                out var resetStaleCompletion, out var reason))
        {
            log.LogWarning(
                $"QUESTMAP_OBJECTIVE_SKIP quest={quest.Id}; objective={objectiveId}; completed=False; reason={reason}");
            return false;
        }

        try
        {
            // EFT's supported reset event removes the stale completed-condition
            // marker and zeros its task counter through the initialized quest
            // controller. This is required after a failed one-session objective:
            // the profile can retain the marker while the live counter is reset.
            if (resetStaleCompletion)
            {
                checker.Reset();
                if (quest.IsConditionDone(condition))
                {
                    log.LogWarning(
                        $"QUESTMAP_OBJECTIVE_SKIP quest={quest.Id}; objective={objectiveId}; completed=False; reason=native reset retained stale completion");
                    return false;
                }
            }

            // SPT-Skipper 1.1.4 uses the same two-part operation. The getter
            // override is required because many objective types derive their
            // value from inventory/game state instead of the task counter.
            checker.SetCurrentValueGetter(_ => condition.value);
            ((GClass4005)questController).GClass4024_0.SetConditionCurrentValue(
                quest,
                EQuestStatus.AvailableForFinish,
                condition,
                condition.value,
                true);

            var checkerComplete = checker.Test();
            var objectiveComplete = quest.IsConditionDone(condition);
            QuestMapDebugLog.Info(log,
                "QUESTMAP_OBJECTIVE_SKIP " +
                $"quest={quest.Id}; objective={objectiveId}; completed={checkerComplete}; " +
                $"objectiveTreeComplete={objectiveComplete}; questStatus={quest.QuestStatus}; target={condition.value}");
            if (checkerComplete) return true;

            log.LogWarning(
                $"QUESTMAP_OBJECTIVE_SKIP quest={quest.Id}; objective={objectiveId}; completed=False; reason=target value did not satisfy condition");
            return false;
        }
        catch (Exception exception)
        {
            log.LogError(
                $"QUESTMAP_OBJECTIVE_SKIP quest={quest.Id}; objective={objectiveId}; completed=False; {exception}");
            return false;
        }
    }

    private static bool TryResolve(
        AbstractQuestControllerClass questController,
        QuestClass quest,
        QuestObjectiveDefinition objective,
        out Condition condition,
        out ConditionProgressChecker checker,
        out bool resetStaleCompletion,
        out string reason)
    {
        condition = null!;
        checker = null!;
        resetStaleCompletion = false;
        reason = string.Empty;
        var objectiveId = objective.Id;

        if (questController is not GClass4005 exactController || exactController.GClass4024_0 is null)
        {
            reason = $"unsupported controller {questController.GetType().Name}";
            return false;
        }

        if (quest.QuestStatus != EQuestStatus.Started)
        {
            reason = $"quest status {quest.QuestStatus}";
            return false;
        }

        if (!quest.Template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish, out var finishConditions))
        {
            reason = "no finish conditions";
            return false;
        }

        condition = finishConditions.IEnumerable_0.FirstOrDefault(value =>
            string.Equals(value.id.ToString(), objectiveId, StringComparison.Ordinal))!;
        if (condition is null)
        {
            reason = "objective condition not found";
            return false;
        }

        if (!quest.ProgressCheckers.TryGetValue(condition, out checker!))
        {
            reason = "objective progress checker not found";
            return false;
        }

        var recordedComplete = quest.IsConditionDone(condition);
        var counterStateAuthoritative = objective.OneSessionOnly
            && !objective.DoNotResetIfCounterCompleted;
        var effectivelyComplete = QuestProgressRules.IsComplete(
            recordedComplete,
            checker.CurrentValue,
            objective.RequiredValue ?? condition.value,
            objective.Compare,
            counterStateAuthoritative);
        if (effectivelyComplete)
        {
            reason = "objective already complete";
            return false;
        }

        resetStaleCompletion = recordedComplete && counterStateAuthoritative;

        return true;
    }
}

/// <summary>
/// One hotkey poller per QuestMap surface. It swaps SKIP into the existing
/// left-side objective action slot and touches row layout only when occupancy
/// actually changes.
/// </summary>
internal sealed class QuestObjectiveSkipVisibility : MonoBehaviour
{
    private readonly System.Collections.Generic.List<QuestObjectiveSkipSlot> _slots = [];
    private Func<bool>? _enabled;
    private Func<KeyboardShortcut>? _hotkey;
    private bool _showSkip;

    public void Bind(Func<bool> enabled, Func<KeyboardShortcut> hotkey)
    {
        _enabled = enabled;
        _hotkey = hotkey;
        RefreshVisibility();
    }

    public QuestObjectiveSkipSlot Register(
        GameObject skip,
        GameObject? fallback,
        Action<bool>? setOccupied = null)
    {
        var slot = new QuestObjectiveSkipSlot(this, skip, fallback, setOccupied);
        _slots.Add(slot);
        Apply(slot);
        return slot;
    }

    public void Clear()
    {
        _slots.Clear();
    }

    private void OnEnable() => RefreshVisibility();

    private void Update()
    {
        var next = _enabled?.Invoke() == true && _hotkey is not null && QuestMapKeyboardShortcut.IsPressed(_hotkey());
        if (next != _showSkip)
        {
            _showSkip = next;
            ApplyAll();
        }
        else if (Time.frameCount % 60 == 0)
        {
            PruneDestroyed();
        }
    }

    private void RefreshVisibility()
    {
        _showSkip = _enabled?.Invoke() == true && _hotkey is not null && QuestMapKeyboardShortcut.IsPressed(_hotkey());
        ApplyAll();
    }

    private void ApplyAll()
    {
        PruneDestroyed();
        foreach (var slot in _slots) Apply(slot);
    }

    private void Apply(QuestObjectiveSkipSlot slot)
    {
        if (!slot.IsAlive) return;
        slot.Apply(_showSkip);
    }

    private void PruneDestroyed() => _slots.RemoveAll(slot => !slot.IsAlive);

    internal void FallbackChanged(QuestObjectiveSkipSlot slot) => Apply(slot);
}

internal sealed class QuestObjectiveSkipSlot
{
    private readonly QuestObjectiveSkipVisibility _owner;
    private readonly GameObject _skip;
    private readonly GameObject? _fallback;
    private readonly Action<bool>? _setOccupied;
    private bool _fallbackAvailable;
    private bool? _lastShowSkip;
    private bool? _lastOccupied;

    public QuestObjectiveSkipSlot(
        QuestObjectiveSkipVisibility owner,
        GameObject skip,
        GameObject? fallback,
        Action<bool>? setOccupied)
    {
        _owner = owner;
        _skip = skip;
        _fallback = fallback;
        _setOccupied = setOccupied;
        _fallbackAvailable = fallback != null;
    }

    public bool IsAlive => _skip != null;

    public void SetFallbackAvailable(bool available)
    {
        if (_fallbackAvailable == available) return;
        _fallbackAvailable = available;
        _owner.FallbackChanged(this);
    }

    internal void Apply(bool showSkip)
    {
        if (_lastShowSkip != showSkip)
        {
            _skip.SetActive(showSkip);
            if (_fallback != null) _fallback.SetActive(_fallbackAvailable && !showSkip);
            _lastShowSkip = showSkip;
        }
        else if (_fallback != null && _fallback.activeSelf != (_fallbackAvailable && !showSkip))
        {
            _fallback.SetActive(_fallbackAvailable && !showSkip);
        }

        var occupied = showSkip || _fallbackAvailable;
        if (_lastOccupied == occupied) return;
        _setOccupied?.Invoke(occupied);
        _lastOccupied = occupied;
    }
}
