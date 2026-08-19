using System;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal sealed class GraphViewportObserver : MonoBehaviour
{
    private Action? _onResized;

    public void Bind(Action onResized)
    {
        _onResized = onResized;
    }

    private void OnRectTransformDimensionsChange()
    {
        _onResized?.Invoke();
    }
}
