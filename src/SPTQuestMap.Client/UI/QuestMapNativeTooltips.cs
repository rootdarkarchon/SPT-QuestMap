using System;
using EFT.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SPTQuestMap.Client.UI;

internal static class QuestMapNativeTooltips
{
    private const float HoverDelaySeconds = 0.35f;
    private const float MaximumWidth = 420f;

    public static void Bind(GameObject target, string text) => Bind(target, () => text);

    public static void Bind(GameObject target, Func<string> text)
    {
        var trigger = target.GetComponent<QuestMapNativeTooltipTrigger>()
            ?? target.AddComponent<QuestMapNativeTooltipTrigger>();
        trigger.Bind(text, HoverDelaySeconds, MaximumWidth);
    }
}

internal sealed class QuestMapNativeTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private static QuestMapNativeTooltipTrigger? _owner;
    private Func<string>? _text;
    private SimpleTooltip? _tooltip;
    private float _delay;
    private float _maxWidth;
    private string? _displayedText;

    public void Bind(Func<string> text, float delay, float maxWidth)
    {
        _text = text;
        _delay = delay;
        _maxWidth = maxWidth;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        var value = _text?.Invoke();
        if (string.IsNullOrWhiteSpace(value)) return;
        _tooltip = ItemUiContext.Instance?.Tooltip;
        if (_tooltip is null) return;
        _owner = this;
        _displayedText = value;
        _tooltip.Show(value, null, _delay, _maxWidth);
    }

    private void Update()
    {
        if (!ReferenceEquals(_owner, this) || _tooltip is null) return;
        var value = _text?.Invoke();
        if (string.Equals(value, _displayedText, StringComparison.Ordinal)) return;
        if (string.IsNullOrWhiteSpace(value))
        {
            CloseIfOwner();
            return;
        }
        _displayedText = value;
        _tooltip.Show(value, null, 0f, _maxWidth);
    }

    public void OnPointerExit(PointerEventData eventData) => CloseIfOwner();

    private void OnDisable() => CloseIfOwner();

    private void OnDestroy() => CloseIfOwner();

    private void CloseIfOwner()
    {
        if (!ReferenceEquals(_owner, this)) return;
        _owner = null;
        if (_tooltip is not null && _tooltip.isActiveAndEnabled) _tooltip.Close();
        _tooltip = null;
        _displayedText = null;
    }
}
