using System;
using System.Collections.Generic;
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
    void RefreshQuests(QuestProfileOverlay overlay, IReadOnlyCollection<string> questIds);
    void SetSelected(string? questId);
}
