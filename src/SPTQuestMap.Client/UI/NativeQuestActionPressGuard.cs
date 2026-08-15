using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal enum NativeQuestActionKind
{
    Accept,
    Complete,
    Handover,
}

/// <summary>
/// Prevents the same native quest transaction from being submitted through
/// two QuestMap surfaces during the short interval after its first press.
/// </summary>
internal sealed class NativeQuestActionPressGuard
{
    private const float CooldownSeconds = 1f;
    private readonly Dictionary<string, float> _lastPressByAction = new(StringComparer.Ordinal);

    public bool TryBegin(
        string questId,
        NativeQuestActionKind action,
        string? objectiveId,
        out float pressedAt)
    {
        pressedAt = Time.realtimeSinceStartup;
        var key = $"{action}\n{questId}\n{objectiveId ?? string.Empty}";
        if (_lastPressByAction.TryGetValue(key, out var prior)
            && pressedAt - prior < CooldownSeconds)
            return false;

        _lastPressByAction[key] = pressedAt;
        return true;
    }

    public static async Task WaitForCooldown(float pressedAt)
    {
        var remaining = CooldownSeconds - (Time.realtimeSinceStartup - pressedAt);
        if (remaining > 0f)
            await Task.Delay(TimeSpan.FromSeconds(remaining));
    }
}
