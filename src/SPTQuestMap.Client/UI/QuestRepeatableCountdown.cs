using System;
using SPTQuestMap.Client.Localization;
using SPTQuestMap.Core.Rules;
using TMPro;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

/// <summary>Updates only its label; never rebuilds the task row or detail pane.</summary>
internal sealed class QuestRepeatableCountdown : MonoBehaviour
{
    private TextMeshProUGUI? _label;
    private long _expirationTime;
    private bool _includePrefix;
    private float _nextUpdate;

    public void Bind(TextMeshProUGUI label, long expirationTime, bool includePrefix = false)
    {
        _label = label;
        _expirationTime = expirationTime;
        _includePrefix = includePrefix;
        Refresh();
    }

    private void OnEnable() => _nextUpdate = 0;

    private void Update()
    {
        if (Time.unscaledTime < _nextUpdate) return;
        Refresh();
    }

    private void Refresh()
    {
        _nextUpdate = Time.unscaledTime + 1f;
        if (_label is null) return;
        var seconds = QuestRepeatableTimeRules.RemainingSeconds(_expirationTime, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var text = seconds == 0 ? ClientLocale.Text("state.expired") : FormatRemaining(seconds);
        if (_includePrefix && seconds > 0)
            text = ClientLocale.Format("common.expires", ClientLocale.Arg("remaining", text));
        if (_label.text != text) _label.text = text;
    }

    private static string FormatRemaining(long seconds) => seconds >= 86400
        ? ClientLocale.Format("common.remainingDays", ClientLocale.Arg("days", seconds / 86400),
            ClientLocale.Arg("hours", seconds % 86400 / 3600), ClientLocale.Arg("minutes", seconds % 3600 / 60))
        : ClientLocale.Format("common.remainingHours", ClientLocale.Arg("hours", seconds / 3600),
            ClientLocale.Arg("minutes", seconds % 3600 / 60));
}
