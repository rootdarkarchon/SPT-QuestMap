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
    private readonly Dictionary<string, Sprite?> _sprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingSprite> _pending = new(StringComparer.Ordinal);
    private readonly List<Texture2D> _ownedTextures = new();
    private readonly List<Sprite> _ownedSprites = new();
    private ManualLogSource? _log;
    private int _cacheHits;
    private int _pendingHits;
    private int _networkRequests;

    public int CachedCount => _sprites.Count;
    public int PendingCount => _pending.Count;
    public int CacheHits => _cacheHits;
    public int PendingHits => _pendingHits;
    public int NetworkRequests => _networkRequests;

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

        try
        {
            _networkRequests++;
            _pending[url] = new PendingSprite(RequestHandler.GetDataAsync(url), completed);
        }
        catch (Exception exception)
        {
            _sprites[url] = null;
            _log?.LogWarning($"QUESTMAP_M06_ASSET url={url}; loaded=False; reason={exception.GetType().Name}");
            completed(null);
        }
    }

    private void Update()
    {
        foreach (var pair in _pending.Where(pair => pair.Value.Request.IsCompleted).ToArray())
        {
            _pending.Remove(pair.Key);
            var sprite = Complete(pair.Key, pair.Value.Request);
            _sprites[pair.Key] = sprite;
            foreach (var callback in pair.Value.Callbacks)
            {
                try
                {
                    callback(sprite);
                }
                catch (Exception exception)
                {
                    _log?.LogWarning($"QUESTMAP_M06_ASSET_CALLBACK url={pair.Key}; reason={exception.GetType().Name}");
                }
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
        public PendingSprite(Task<byte[]> request, Action<Sprite?> callback)
        {
            Request = request;
            Callbacks = [callback];
        }

        public Task<byte[]> Request { get; }
        public List<Action<Sprite?>> Callbacks { get; }
    }
}
