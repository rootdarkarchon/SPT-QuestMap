using System;
using SPTQuestMap.Core.Models;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal interface IGlobalTasksContentView : IDisposable
{
    RectTransform Root { get; }
    int ActiveNodeCount { get; }
    Vector2 ViewportSize { get; }
    QuestAssetSpriteCache AssetCache { get; }
    GraphViewportState CaptureViewportState();
    void RefreshOverlay(QuestProfileOverlay overlay, string? selectedQuestId);
    void RefreshQuest(QuestProfileOverlay overlay, string questId);
    void SetSelected(string? questId);
}
