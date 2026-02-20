using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Base class for image cache services that caches Bitmaps to avoid redundant loading and decoding.
/// Uses an in-memory cache with size-based eviction and request deduplication.
/// </summary>
public abstract class ImageCacheServiceBase<TKey> where TKey : notnull
{
    private readonly ILogger logger;
    private readonly ConcurrentDictionary<TKey, Bitmap?> iconCache = new();
    private readonly ConcurrentDictionary<TKey, Task<Bitmap?>> ongoingRequests = new();

    protected virtual int MaxCacheSize => 100;
    protected virtual int DecodeWidth => 165;

    protected ImageCacheServiceBase(ILogger logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Loads the raw image bytes for the given key. Implemented by derived classes.
    /// </summary>
    protected abstract Task<byte[]> LoadImageBytesAsync(TKey key);

    /// <summary>
    /// Gets a display name for the key, used in log messages.
    /// </summary>
    protected virtual string GetKeyDisplayName(TKey key) => key.ToString() ?? string.Empty;

    /// <summary>
    /// Gets an image as a Bitmap from cache or loads it.
    /// Returns the same Bitmap instance for subsequent calls with the same key.
    /// </summary>
    public async Task<Bitmap?> GetImageAsync(TKey key)
    {
        // Check if already in cache
        if (iconCache.TryGetValue(key, out var cachedBitmap))
        {
            return cachedBitmap;
        }

        // Check if there's already an ongoing request for this key
        Task<Bitmap?>? existingTask;
        if (ongoingRequests.TryGetValue(key, out existingTask))
        {
            return await existingTask;
        }

        // Create and store the loading task
        var loadTask = LoadAndCacheImageAsync(key);
        if (ongoingRequests.TryAdd(key, loadTask))
        {
            try
            {
                return await loadTask;
            }
            finally
            {
                // Remove from ongoing requests
                ongoingRequests.TryRemove(key, out _);
            }
        }
        else
        {
            // Another thread beat us to it, use their task
            if (ongoingRequests.TryGetValue(key, out existingTask))
            {
                return await existingTask;
            }
            // Fallback to our task
            return await loadTask;
        }
    }

    private async Task<Bitmap?> LoadAndCacheImageAsync(TKey key)
    {
        try
        {
            var imageBytes = await LoadImageBytesAsync(key);
            if (imageBytes != null && imageBytes.Length > 0)
            {
                using var ms = new MemoryStream(imageBytes);
                var bitmap = Bitmap.DecodeToWidth(ms, DecodeWidth);

                // Add to cache
                AddToCache(key, bitmap);

                logger.LogDebug("Loaded and cached image for {Key}", GetKeyDisplayName(key));
                return bitmap;
            }

            logger.LogDebug("No image found for {Key}", GetKeyDisplayName(key));
            AddToCache(key, null);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load image for {Key}", GetKeyDisplayName(key));
            AddToCache(key, null);
            return null;
        }
    }

    /// <summary>
    /// Synchronously gets a cached image if available. Returns null if not in cache.
    /// Use GetImageAsync for loading if not cached.
    /// </summary>
    public Bitmap? GetCachedImage(TKey key)
    {
        iconCache.TryGetValue(key, out var bitmap);
        return bitmap;
    }

    /// <summary>
    /// Preloads images for multiple keys in parallel.
    /// </summary>
    public async Task PreloadImagesAsync(params TKey[] keys)
    {
        var tasks = new Task<Bitmap?>[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            tasks[i] = GetImageAsync(keys[i]);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Clears the image cache.
    /// </summary>
    public void ClearCache()
    {
        // Dispose all bitmaps
        foreach (var bitmap in iconCache.Values)
        {
            bitmap?.Dispose();
        }
        iconCache.Clear();
        logger.LogInformation("Image cache cleared");
    }

    private void AddToCache(TKey key, Bitmap? bitmap)
    {
        // Simple size-based eviction
        if (iconCache.Count >= MaxCacheSize)
        {
            // Remove oldest entries (first added)
            var entriesToRemove = iconCache.Count - MaxCacheSize + 1;
            foreach (var cacheKey in iconCache.Keys)
            {
                if (entriesToRemove <= 0) break;
                if (iconCache.TryRemove(cacheKey, out var oldBitmap))
                {
                    oldBitmap?.Dispose();
                    entriesToRemove--;
                }
            }
        }

        iconCache.TryAdd(key, bitmap);
    }
}
