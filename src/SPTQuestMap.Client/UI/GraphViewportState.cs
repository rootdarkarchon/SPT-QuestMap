using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal readonly struct GraphViewportState
{
    public GraphViewportState(float scale, Vector2 anchoredPosition)
    {
        Scale = scale;
        AnchoredPosition = anchoredPosition;
    }

    public float Scale { get; }
    public Vector2 AnchoredPosition { get; }
}
