using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Singleton service that caches organization icons as Bitmaps to avoid redundant loading and decoding.
/// Uses CDN for fetching icons and maintains an in-memory cache for performance.
/// </summary>
public class OrganizationIconCacheService
{
    private readonly OrganizationClient organizationClient;
    private readonly ILogger<OrganizationIconCacheService> logger;
    private readonly ConcurrentDictionary<int, Bitmap?> iconCache = new();
    private readonly ConcurrentDictionary<int, Task<Bitmap?>> ongoingRequests = new();
    private const int MaxCacheSize = 100;
    private const int DecodeWidth = 165;

    public OrganizationIconCacheService(OrganizationClient organizationClient, ILoggerFactory loggerFactory)
    {
        this.organizationClient = organizationClient;
        logger = loggerFactory.CreateLogger<OrganizationIconCacheService>();
    }

    /// <summary>
    /// Gets the organization icon as a Bitmap from cache or loads it from CDN.
    /// Returns the same Bitmap instance for subsequent calls with the same organizationId.
    /// </summary>
    /// <param name="organizationId">The organization ID to fetch the icon for.</param>
    /// <returns>The organization icon as a Bitmap, or null if not available.</returns>
    public async Task<Bitmap?> GetOrganizationIconAsync(int organizationId)
    {
        // Check if already in cache
        if (iconCache.TryGetValue(organizationId, out var cachedBitmap))
        {
            return cachedBitmap;
        }

        // Check if there's already an ongoing request for this organization
        Task<Bitmap?>? existingTask;
        if (ongoingRequests.TryGetValue(organizationId, out existingTask))
        {
            return await existingTask;
        }

        // Create and store the loading task
        var loadTask = LoadIconFromCdnAsync(organizationId);
        if (ongoingRequests.TryAdd(organizationId, loadTask))
        {
            try
            {
                return await loadTask;
            }
            finally
            {
                // Remove from ongoing requests
                ongoingRequests.TryRemove(organizationId, out _);
            }
        }
        else
        {
            // Another thread beat us to it, use their task
            if (ongoingRequests.TryGetValue(organizationId, out existingTask))
            {
                return await existingTask;
            }
            // Fallback to our task
            return await loadTask;
        }
    }

    private async Task<Bitmap?> LoadIconFromCdnAsync(int organizationId)
    {
        try
        {
            var iconBytes = await organizationClient.GetOrganizationIconCdnAsync(organizationId);
            if (iconBytes != null && iconBytes.Length > 0)
            {
                using var ms = new MemoryStream(iconBytes);
                var bitmap = Bitmap.DecodeToWidth(ms, DecodeWidth);

                // Add to cache
                AddToCache(organizationId, bitmap);

                logger.LogDebug("Loaded and cached icon for organization {OrganizationId}", organizationId);
                return bitmap;
            }

            logger.LogDebug("No icon found for organization {OrganizationId}", organizationId);
            AddToCache(organizationId, null);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load icon for organization {OrganizationId}", organizationId);
            AddToCache(organizationId, null);
            return null;
        }
    }

    /// <summary>
    /// Synchronously gets a cached icon if available. Returns null if not in cache.
    /// Use GetOrganizationIconAsync for loading if not cached.
    /// </summary>
    /// <param name="organizationId">The organization ID.</param>
    /// <returns>The cached Bitmap or null if not in cache.</returns>
    public Bitmap? GetCachedIcon(int organizationId)
    {
        iconCache.TryGetValue(organizationId, out var bitmap);
        return bitmap;
    }

    /// <summary>
    /// Preloads icons for multiple organizations in parallel.
    /// </summary>
    /// <param name="organizationIds">The organization IDs to preload.</param>
    public async Task PreloadIconsAsync(params int[] organizationIds)
    {
        var tasks = new Task<Bitmap?>[organizationIds.Length];
        for (int i = 0; i < organizationIds.Length; i++)
        {
            tasks[i] = GetOrganizationIconAsync(organizationIds[i]);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Clears the icon cache.
    /// </summary>
    public void ClearCache()
    {
        // Dispose all bitmaps
        foreach (var bitmap in iconCache.Values)
        {
            bitmap?.Dispose();
        }
        iconCache.Clear();
        logger.LogInformation("Icon cache cleared");
    }

    private void AddToCache(int organizationId, Bitmap? bitmap)
    {
        // Simple size-based eviction
        if (iconCache.Count >= MaxCacheSize)
        {
            // Remove oldest entries (first added)
            var entriesToRemove = iconCache.Count - MaxCacheSize + 1;
            foreach (var key in iconCache.Keys)
            {
                if (entriesToRemove <= 0) break;
                if (iconCache.TryRemove(key, out var oldBitmap))
                {
                    oldBitmap?.Dispose();
                    entriesToRemove--;
                }
            }
        }

        iconCache.TryAdd(organizationId, bitmap);
    }
}
