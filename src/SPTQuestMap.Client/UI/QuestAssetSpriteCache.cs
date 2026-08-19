using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx.Logging;
using SPT.Common.Http;
using UnityEngine;

namespace SPTQuestMap.Client.UI;

internal sealed class QuestAssetSpriteCache : MonoBehaviour
{
    private const int MaxConcurrentRequests = 8;
    private const int MaxCompletionsPerFrame = 1;
    private readonly Dictionary<string, Sprite?> _sprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingSprite> _pending = new(StringComparer.Ordinal);
    private readonly Queue<string> _requestQueue = new();
    private readonly List<Texture2D> _ownedTextures = new();
    private readonly List<Sprite> _ownedSprites = new();
    private ManualLogSource? _log;
    private int _cacheHits;
    private int _pendingHits;
    private int _networkRequests;
    private int _activeRequests;

    public int CachedCount => _sprites.Count;
    public int PendingCount => _pending.Count;
    public int CacheHits => _cacheHits;
    public int PendingHits => _pendingHits;
    public int NetworkRequests => _networkRequests;
    public int ActiveRequestCount => _activeRequests;
    public int QueuedCount => _requestQueue.Count;

    public void Bind(ManualLogSource log)
    {
        _log = log;
    }

    public void Request(string? rawUrl, Action<Sprite?> completed)
    {
        var url = Normalize(rawUrl);
        if (url is null)
        {
            completed(null);
            return;
        }
        if (_sprites.TryGetValue(url, out var cached))
        {
            _cacheHits++;
            completed(cached);
            return;
        }
        if (_pending.TryGetValue(url, out var pending))
        {
            _pendingHits++;
            pending.Callbacks.Add(completed);
            return;
        }

        _pending[url] = new PendingSprite(completed);
        _requestQueue.Enqueue(url);
        StartQueuedRequests();
    }

    private void Update()
    {
        for (var completion = 0; completion < MaxCompletionsPerFrame; completion++)
        {
            var pair = _pending.FirstOrDefault(candidate => candidate.Value.Request?.IsCompleted == true);
            if (pair.Key is null || pair.Value.Request is null) break;
            _pending.Remove(pair.Key);
            _activeRequests = Math.Max(0, _activeRequests - 1);
            var sprite = Complete(pair.Key, pair.Value.Request);
            _sprites[pair.Key] = sprite;
            CompleteCallbacks(pair.Key, pair.Value, sprite);
        }
        StartQueuedRequests();
    }

    private void StartQueuedRequests()
    {
        while (_activeRequests < MaxConcurrentRequests && _requestQueue.Count > 0)
        {
            var url = _requestQueue.Dequeue();
            if (!_pending.TryGetValue(url, out var pending) || pending.Request is not null) continue;
            try
            {
                _networkRequests++;
                pending.Request = RequestHandler.GetDataAsync(url);
                _activeRequests++;
            }
            catch (Exception exception)
            {
                _pending.Remove(url);
                _sprites[url] = null;
                _log?.LogWarning($"QUESTMAP_M06_ASSET url={url}; loaded=False; reason={exception.GetType().Name}");
                CompleteCallbacks(url, pending, null);
            }
        }
    }

    private void CompleteCallbacks(string url, PendingSprite pending, Sprite? sprite)
    {
        foreach (var callback in pending.Callbacks)
        {
            try
            {
                callback(sprite);
            }
            catch (Exception exception)
            {
                _log?.LogWarning($"QUESTMAP_M06_ASSET_CALLBACK url={url}; reason={exception.GetType().Name}");
            }
        }
    }

    private Sprite? Complete(string url, Task<byte[]> request)
    {
        if (request.IsCanceled || request.IsFaulted)
        {
            var reason = request.IsCanceled ? "canceled" : request.Exception?.GetBaseException().GetType().Name ?? "faulted";
            _log?.LogWarning($"QUESTMAP_M06_ASSET url={url}; loaded=False; reason={reason}");
            return null;
        }
        var bytes = request.Result;
        if (bytes.Length == 0) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes, true))
        {
            UnityEngine.Object.Destroy(texture);
            _log?.LogWarning($"QUESTMAP_M06_ASSET url={url}; loaded=False; reason=decode");
            return null;
        }
        texture.name = $"QuestMap-{url}";
        var sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100,
            0,
            SpriteMeshType.FullRect);
        _ownedTextures.Add(texture);
        _ownedSprites.Add(sprite);
        return sprite;
    }

    private void OnDestroy()
    {
        _pending.Clear();
        _requestQueue.Clear();
        _activeRequests = 0;
        foreach (var sprite in _ownedSprites) UnityEngine.Object.Destroy(sprite);
        foreach (var texture in _ownedTextures) UnityEngine.Object.Destroy(texture);
        _ownedSprites.Clear();
        _ownedTextures.Clear();
    }

    private static string? Normalize(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return null;
        var url = rawUrl.Trim().Replace('\\', '/');
        return url.StartsWith("/", StringComparison.Ordinal) ? url : $"/{url}";
    }

    private sealed class PendingSprite
    {
        public PendingSprite(Action<Sprite?> callback)
        {
            Callbacks = [callback];
        }

        public Task<byte[]>? Request { get; set; }
        public List<Action<Sprite?>> Callbacks { get; }
    }
}
