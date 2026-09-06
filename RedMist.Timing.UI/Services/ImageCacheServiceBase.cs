using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Base class for image cache services that caches Bitmaps to avoid redundant loading and decoding.
/// Uses an in-memory cache with size-based eviction and request deduplication, over a
/// <see cref="PersistentImageStore"/> that keeps the bytes between runs.
/// </summary>
/// <remarks>
/// Cached bitmaps are handed directly to bindings (Image.Source), so this cache never disposes
/// them: the cache has no way to know whether a bitmap it is evicting is still on screen, and
/// disposing one that is frees the underlying Skia surface out from under the renderer, which
/// takes the process down with a native crash. Evicted entries are simply dropped and reclaimed
/// by the GC once nothing references them. That applies equally to a bitmap replaced by
/// revalidation, which is the most likely one of all to still be on screen.
/// </remarks>
public abstract class ImageCacheServiceBase<TKey> where TKey : notnull
{
    private readonly ILogger logger;
    private readonly PersistentImageStore store;
    private readonly ConcurrentDictionary<TKey, Bitmap?> iconCache = new();
    private readonly ConcurrentDictionary<TKey, Task<Bitmap?>> ongoingRequests = new();

    /// <summary>
    /// Keys whose bitmap came off disk and has not yet been confirmed against the server, with the
    /// URL to confirm it at. A key is removed once the server has answered, and only then.
    /// </summary>
    /// <remarks>
    /// The check is owed once per image per run, not once per screen that shows it. That
    /// distinction matters more than it looks: the events list rebuilds its rows whenever the
    /// schedule changes and on every return to the foreground, and each rebuild reloads every icon.
    /// Checking per load rather than per session would turn pocketing a phone and taking it out
    /// again into a full round of requests each time, for logos that change a few times a year.
    ///
    /// Tracking what is owed rather than what has been attempted is what makes the retry real.
    /// Marking a key as checked when the request was started would strand every logo on a launch
    /// with no signal - the request fails, the mark stands, and nothing tries again until the app
    /// is restarted, which is precisely the situation the disk tier exists to serve.
    /// </remarks>
    private readonly ConcurrentDictionary<TKey, string> unconfirmed = new();

    /// <summary>Keys with a check in flight, so concurrent loads do not start several.</summary>
    private readonly ConcurrentDictionary<TKey, byte> checking = new();

    protected virtual int MaxCacheSize => 100;
    protected virtual int DecodeWidth => 165;

    /// <summary>
    /// Raised when revalidation has replaced an image that was already handed out, with the key
    /// whose bitmap is now different and the bitmap itself.
    /// </summary>
    /// <remarks>
    /// Needed because the first paint deliberately does not wait for the server. A view that is on
    /// screen while the check completes - the events list, in practice - shows the stored logo and
    /// swaps in the new one here. Views built after the check simply read the updated bitmap out of
    /// the cache and never see this fire.
    ///
    /// The new bitmap comes with the key rather than being looked up again by the handler. It is
    /// the same instance the cache now holds, so nothing is weakened by passing it - and a handler
    /// that re-read the cache could find the entry already evicted and silently drop the update,
    /// with nothing left to raise it again.
    ///
    /// Raised on whichever thread the revalidation completed on; handlers that touch bound state are
    /// responsible for getting to the UI thread.
    /// </remarks>
    public event Action<TKey, Bitmap>? ImageUpdated;

    protected ImageCacheServiceBase(PersistentImageStore store, ILogger logger)
    {
        this.store = store;
        this.logger = logger;
    }

    /// <summary>
    /// The URL the image for this key is fetched from. Implemented by derived classes.
    /// </summary>
    /// <returns>The URL, or null when this key has no image to fetch.</returns>
    protected abstract string? GetImageUrl(TKey key);

    /// <summary>
    /// Gets a display name for the key, used in log messages.
    /// </summary>
    protected virtual string GetKeyDisplayName(TKey key) => key.ToString() ?? string.Empty;

    /// <summary>
    /// Gets an image as a Bitmap from cache or loads it.
    /// Returns the same Bitmap instance for subsequent calls with the same key, until revalidation
    /// replaces it.
    /// </summary>
    public async Task<Bitmap?> GetImageAsync(TKey key)
    {
        // Check if already in cache
        if (iconCache.TryGetValue(key, out var cachedBitmap))
        {
            // A hit is not proof the image has been checked. The first load may have been the one
            // that had no signal, and every later load answers from here without ever reaching the
            // code below - so without this, a check that failed once would never be retried.
            ScheduleRevalidation(key);
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
            var url = GetImageUrl(key);
            if (string.IsNullOrEmpty(url))
            {
                AddToCache(key, null);
                return null;
            }

            // ConfigureAwait, so the decode below does not land back on the UI thread. The events
            // list starts this from there, and a page of PNG decodes at the moment the list is
            // painting is the same stall the store moves its file reads off-thread to avoid. Every
            // caller marshals explicitly with InvokeOnUIThread after awaiting, so none of them
            // depends on resuming there.
            var stored = await store.GetAsync(url).ConfigureAwait(false);
            if (stored.Bytes.Length > 0)
            {
                Bitmap bitmap;
                try
                {
                    bitmap = Decode(stored.Bytes);
                }
                catch (Exception ex)
                {
                    // Bytes that arrived with a 200 and a validator but are not an image - an error
                    // page from something in front of the CDN, or a file the filesystem lost part
                    // of. The store cannot tell (these assets are served as
                    // application/octet-stream), so this is the only point anything finds out, and
                    // saying so is what keeps a bad entry from being re-read and re-rejected every
                    // launch until it aged out three months later.
                    logger.LogWarning(ex, "Stored image for {Key} could not be decoded; discarding it", GetKeyDisplayName(key));
                    await store.InvalidateAsync(url);
                    AddToCache(key, null);
                    return null;
                }

                // Add to cache
                AddToCache(key, bitmap);

                logger.LogDebug("Loaded and cached image for {Key}", GetKeyDisplayName(key));

                // Only bytes that came off disk are worth checking; anything just fetched is
                // current. Recorded and started after the bitmap is cached, so the caller is never
                // held up by the check.
                if (stored.FromDisk)
                {
                    unconfirmed[key] = url;
                    ScheduleRevalidation(key);
                }

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
    /// Turns stored bytes into a bitmap. Virtual so a test can make decoding fail on demand: the
    /// headless platform's decoder accepts anything, so there is otherwise no way to exercise what
    /// happens when bytes that arrived with a 200 turn out not to be an image.
    /// </summary>
    protected virtual Bitmap Decode(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return Bitmap.DecodeToWidth(ms, DecodeWidth);
    }

    private void ScheduleRevalidation(TKey key)
    {
        if (!unconfirmed.TryGetValue(key, out var url))
        {
            // Already answered this session, or the bytes came off the network and are current.
            return;
        }

        if (!checking.TryAdd(key, 0))
        {
            return;
        }

        _ = RevalidateAsync(key, url);
    }

    private async Task RevalidateAsync(TKey key, string url)
    {
        try
        {
            var result = await store.RevalidateAsync(url).ConfigureAwait(false);

            // Whether the server settled it, not whether the call returned. The store reports every
            // failure as a value rather than an exception, so anything keyed off "did this throw"
            // would retire a check that never completed.
            if (result.Answered)
            {
                unconfirmed.TryRemove(key, out _);
            }

            if (result.Bytes is not { Length: > 0 } fresh)
            {
                // Unchanged, or nothing worth acting on. The cached bitmap stays exactly as it is.
                return;
            }

            Bitmap bitmap;
            try
            {
                bitmap = Decode(fresh);
            }
            catch (Exception ex)
            {
                // The replacement does not decode. The bitmap on screen stays, and the entry the
                // store just wrote is dropped so the next launch fetches rather than re-reading
                // something known to be unusable. The check itself was answered.
                logger.LogWarning(ex, "Replacement image for {Key} could not be decoded; discarding it", GetKeyDisplayName(key));
                await store.InvalidateAsync(url);
                return;
            }

            // Replaced rather than removed, and the bitmap it displaces is not disposed - see the
            // remarks on this class. Whatever is still bound to the old one keeps rendering it
            // until the binding drops it.
            iconCache[key] = bitmap;

            logger.LogInformation("Image for {Key} changed on the server and was replaced", GetKeyDisplayName(key));
            ImageUpdated?.Invoke(key, bitmap);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to revalidate the image for {Key}", GetKeyDisplayName(key));
        }
        finally
        {
            checking.TryRemove(key, out _);
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
    /// Clears the image cache. Bitmaps are dropped, not disposed - see the remarks on this class.
    /// </summary>
    public void ClearCache()
    {
        iconCache.Clear();
        unconfirmed.Clear();
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
                if (iconCache.TryRemove(cacheKey, out _))
                {
                    entriesToRemove--;
                }
            }
        }

        iconCache.TryAdd(key, bitmap);
    }
}
