using System.Collections.Generic;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestGraphNodePool
{
    private readonly RectTransform _parent;
    private readonly Stack<QuestGraphNodeView> _available = new();

    public QuestGraphNodePool(RectTransform parent)
    {
        _parent = parent;
    }

    public int TotalCreated { get; private set; }
    public int AvailableCount => _available.Count;

    public QuestGraphNodeView Acquire()
    {
        if (_available.Count > 0) return _available.Pop();
        TotalCreated++;
        return QuestGraphNodeView.Create(_parent);
    }

    public void Release(QuestGraphNodeView view)
    {
        view.Recycle();
        _available.Push(view);
    }
}
