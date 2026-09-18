using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Rules;

public sealed record RaidProgressNotification(
    QuestGraphNode Quest,
    QuestObjectiveDefinition? Objective,
    QuestObjectiveProgress? Progress,
    string ExactStatus);

/// <summary>Coalesces delayed kill progress per quest using a monotonic caller clock.</summary>
public sealed class RaidProgressNotificationQueue
{
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    // Returns an immediate notification, or null when the notification is held.
    public RaidProgressNotification? Schedule(RaidProgressNotification change, double delaySeconds, double now)
    {
        var questId = change.Quest.Id;
        if (change.Objective is null && _pending.TryGetValue(questId, out var pending))
        {
            // A final-kill status event can arrive separately from its counter event.
            _pending[questId] = pending with
            {
                Change = pending.Change with { ExactStatus = change.ExactStatus },
            };
            return null;
        }

        var delay = double.IsNaN(delaySeconds) ? 0 : Math.Max(0, Math.Min(10, delaySeconds));
        if (change.Objective?.IsKillObjective == true)
        {
            if (delay > 0)
            {
                _pending[questId] = new Pending(change, now + delay);
                return null;
            }

            _pending.Remove(questId);
        }

        return change;
    }

    public bool TryDequeue(double now, out RaidProgressNotification change)
    {
        Pending? next = null;
        foreach (var pending in _pending.Values)
        {
            if (pending.DueAt <= now && (next is null || pending.DueAt < next.DueAt)) next = pending;
        }

        if (next is null)
        {
            change = null!;
            return false;
        }

        _pending.Remove(next.Change.Quest.Id);
        change = next.Change;
        return true;
    }

    public void Clear() => _pending.Clear();

    private sealed record Pending(RaidProgressNotification Change, double DueAt);
}
