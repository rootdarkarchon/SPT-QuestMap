using System.Collections.Generic;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestGraphNodePool
{
    private readonly RectTransform _parent;
    private readonly QuestAssetSpriteCache _assetCache;
    private readonly Stack<QuestGraphCardNodeView> _available = new();

    public QuestGraphNodePool(RectTransform parent, QuestAssetSpriteCache assetCache)
    {
        _parent = parent;
        _assetCache = assetCache;
    }

    public int TotalCreated { get; private set; }
    public int AvailableCount => _available.Count;

    public QuestGraphCardNodeView Acquire()
    {
        if (_available.Count > 0) return _available.Pop();
        TotalCreated++;
        return QuestGraphCardNodeView.Create(_parent, _assetCache);
    }

    public void Release(QuestGraphCardNodeView view)
    {
        view.Recycle();
        _available.Push(view);
    }
}
