using UnityEngine;
using UnityEngine.EventSystems;

namespace SPTQuestMap.Client.UI;

internal sealed class GraphPanZoomHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    private const float MinimumScale = 0.2f;
    private const float MaximumScale = 1.8f;
    private RectTransform? _content;
    private RectTransform? _viewport;

    public void Bind(RectTransform viewport, RectTransform content)
    {
        _viewport = viewport;
        _content = content;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_content is null) return;
        var canvas = GetComponentInParent<Canvas>();
        var scaleFactor = canvas is null ? 1f : Mathf.Max(0.01f, canvas.scaleFactor);
        _content.anchoredPosition += eventData.delta / scaleFactor;
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (_content is null || _viewport is null) return;
        if (Mathf.Abs(eventData.scrollDelta.y) < 0.01f) return;
        var oldScale = _content.localScale.x;
        var newScale = Mathf.Clamp(oldScale * (eventData.scrollDelta.y > 0 ? 1.12f : 0.89f), MinimumScale, MaximumScale);
        if (Mathf.Approximately(oldScale, newScale)) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _viewport,
            eventData.position,
            eventData.pressEventCamera,
            out var viewportLocalPointer);
        // RectTransformUtility returns coordinates relative to the viewport pivot, while the
        // graph content's anchor and pivot are both its parent's top-left corner. Convert the
        // cursor into that same anchor space before preserving the point beneath it.
        var anchorSpacePointer = viewportLocalPointer - new Vector2(_viewport.rect.xMin, _viewport.rect.yMax);
        var before = (anchorSpacePointer - _content.anchoredPosition) / oldScale;
        _content.localScale = new Vector3(newScale, newScale, 1f);
        _content.anchoredPosition = anchorSpacePointer - before * newScale;
    }
}
