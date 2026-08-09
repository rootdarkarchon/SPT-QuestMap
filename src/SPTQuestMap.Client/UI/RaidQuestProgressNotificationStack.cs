using System;
using System.Collections.Generic;
using BepInEx.Logging;
using SPTQuestMap.Client.Data;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal sealed class RaidQuestProgressNotificationStack : MonoBehaviour, IDisposable
{
    private readonly Dictionary<string, RaidQuestProgressNotificationView> _cards = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private ManualLogSource? _log;
    private Func<float>? _opacity;
    private Func<bool>? _minimal;
    private Func<float>? _fadeDuration;
    private Func<float>? _displayDuration;
    private QuestAssetSpriteCache? _assetCache;

    public static RaidQuestProgressNotificationStack Create(
        Transform owner,
        ManualLogSource log,
        Func<float> opacity,
        Func<bool> minimal,
        Func<float> fadeDuration,
        Func<float> displayDuration,
        QuestAssetSpriteCache assetCache)
    {
        var host = new GameObject("QuestMapRaidProgressNotificationStack");
        host.transform.SetParent(owner, false);
        var stack = host.AddComponent<RaidQuestProgressNotificationStack>();
        stack._log = log;
        stack._opacity = opacity;
        stack._minimal = minimal;
        stack._fadeDuration = fadeDuration;
        stack._displayDuration = displayDuration;
        stack._assetCache = assetCache;
        QuestMapDebugLog.Info(log, "QUESTMAP_M06_RAID_NOTIFICATION_STACK active=True; grouping=trader+quest; sharedAssetCache=True");
        return stack;
    }

    public void Show(InRaidQuestProgressChange change)
    {
        if (_log is null || _opacity is null || _minimal is null || _fadeDuration is null
            || _displayDuration is null || _assetCache is null) return;
        var questId = change.Quest.Id;
        if (!_cards.TryGetValue(questId, out var card))
        {
            card = RaidQuestProgressNotificationView.Create(
                transform, _log, _opacity, _minimal, _fadeDuration, _displayDuration, _assetCache);
            _cards.Add(questId, card);
        }
        _order.Remove(questId);
        _order.Insert(0, questId);
        card.Show(change);
        Layout();
    }

    public void Hide()
    {
        foreach (var card in _cards.Values) card.Dispose();
        _cards.Clear();
        _order.Clear();
    }

    public void Dispose()
    {
        Hide();
        _log = null;
        _opacity = null;
        _minimal = null;
        _fadeDuration = null;
        _displayDuration = null;
        _assetCache = null;
        if (gameObject != null) Destroy(gameObject);
    }

    private void Update()
    {
        var changed = false;
        for (var index = _order.Count - 1; index >= 0; index--)
        {
            var questId = _order[index];
            if (_cards.TryGetValue(questId, out var card) && card.Displaying) continue;
            card?.Dispose();
            _cards.Remove(questId);
            _order.RemoveAt(index);
            changed = true;
        }
        if (changed) Layout();
    }

    private void Layout()
    {
        var top = 28f;
        for (var index = 0; index < _order.Count; index++)
        {
            if (!_cards.TryGetValue(_order[index], out var card)) continue;
            card.SetStackTop(top);
            top += card.CardHeight + 8f;
        }
    }
}
