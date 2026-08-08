using UnityEngine;
using UnityEngine.UI;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestMapPanelGradient : MaskableGraphic
{
    private Color _left = Color.black;
    private Color _right = Color.black;

    public void SetColors(Color left, Color right)
    {
        _left = left;
        _right = right;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper helper)
    {
        helper.Clear();
        var area = rectTransform.rect;
        helper.AddVert(new Vector3(area.xMin, area.yMin), _left, Vector2.zero);
        helper.AddVert(new Vector3(area.xMin, area.yMax), _left, Vector2.up);
        helper.AddVert(new Vector3(area.xMax, area.yMax), _right, Vector2.one);
        helper.AddVert(new Vector3(area.xMax, area.yMin), _right, Vector2.right);
        helper.AddTriangle(0, 1, 2);
        helper.AddTriangle(2, 3, 0);
    }
}
