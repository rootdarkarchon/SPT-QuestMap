using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestMapPanelGradient : MaskableGraphic
{
    private Color _left = Color.black;
    private Color _right = Color.black;
    private bool _vertical;

    public void SetColors(Color left, Color right)
    {
        _left = left;
        _right = right;
        _vertical = false;
        SetVerticesDirty();
    }

    public void SetVerticalColors(Color bottom, Color top)
    {
        _left = bottom;
        _right = top;
        _vertical = true;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        var area = rectTransform.rect;
        helper.AddVert(new Vector3(area.xMin, area.yMin), _left, Vector2.zero);
        helper.AddVert(new Vector3(area.xMin, area.yMax), _vertical ? _right : _left, Vector2.up);
        helper.AddVert(new Vector3(area.xMax, area.yMax), _right, Vector2.one);
        helper.AddVert(new Vector3(area.xMax, area.yMin), _vertical ? _left : _right, Vector2.right);
        helper.AddTriangle(0, 1, 2);
        helper.AddTriangle(2, 3, 0);
    }
}
