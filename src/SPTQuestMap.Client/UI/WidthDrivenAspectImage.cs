using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal sealed class WidthDrivenAspectImage : MonoBehaviour
{
    private RectTransform? _parent;
    private RectTransform? _rect;
    private float _aspectRatio;

    public void Bind(RectTransform parent, float aspectRatio)
    {
        _parent = parent;
        _rect = transform as RectTransform;
        _aspectRatio = Mathf.Max(0.01f, aspectRatio);
        RefreshHeight();
    }

    private void OnRectTransformDimensionsChange()
    {
        RefreshHeight();
    }

    private void RefreshHeight()
    {
        if (_parent is null || _rect is null || _aspectRatio <= 0) return;
        _rect.sizeDelta = new Vector2(0, _parent.rect.width / _aspectRatio);
    }
}
