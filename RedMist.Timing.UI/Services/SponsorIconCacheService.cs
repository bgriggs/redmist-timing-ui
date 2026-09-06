using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Singleton service that caches sponsor images as Bitmaps to avoid redundant loading and decoding.
/// Fetches images by URL and maintains an in-memory cache keyed by image URL for performance, over
/// the persistent store that keeps them between runs.
/// </summary>
/// <remarks>
/// Sponsor images are the keys, so the store's URL keying is already what this cache is keyed by.
/// Whether they revalidate depends on the host they are served from: the ones on the timing CDN
/// send Last-Modified and are checked like organization logos, and any host that sends no validator
/// is fetched once per session and aged out on the store's own lifetime instead.
/// </remarks>
public class SponsorIconCacheService : ImageCacheServiceBase<string>
{
    public SponsorIconCacheService(PersistentImageStore store, ILoggerFactory loggerFactory)
        : base(store, loggerFactory.CreateLogger<SponsorIconCacheService>())
    {
    }

    protected override string GetKeyDisplayName(string key) => $"sponsor image {key}";

    protected override string? GetImageUrl(string key) => key;

    /// <summary>
    /// Gets a sponsor image as a Bitmap from cache or loads it from the URL.
    /// </summary>
    public Task<Bitmap?> GetSponsorImageAsync(string imageUrl) => GetImageAsync(imageUrl);

    /// <summary>
    /// Synchronously gets a cached sponsor image if available. Returns null if not in cache.
    /// </summary>
    public Bitmap? GetCachedSponsorImage(string imageUrl) => GetCachedImage(imageUrl);
}
