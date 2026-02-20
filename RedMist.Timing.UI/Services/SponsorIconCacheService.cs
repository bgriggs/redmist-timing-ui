using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Singleton service that caches sponsor images as Bitmaps to avoid redundant loading and decoding.
/// Fetches images by URL and maintains an in-memory cache keyed by image URL for performance.
/// </summary>
public class SponsorIconCacheService : ImageCacheServiceBase<string>
{
    private readonly IHttpClientFactory httpClientFactory;

    public SponsorIconCacheService(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        : base(loggerFactory.CreateLogger<SponsorIconCacheService>())
    {
        this.httpClientFactory = httpClientFactory;
    }

    protected override string GetKeyDisplayName(string key) => $"sponsor image {key}";

    protected override async Task<byte[]> LoadImageBytesAsync(string key)
    {
        var httpClient = httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(key);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// Gets a sponsor image as a Bitmap from cache or loads it from the URL.
    /// </summary>
    public Task<Bitmap?> GetSponsorImageAsync(string imageUrl) => GetImageAsync(imageUrl);

    /// <summary>
    /// Synchronously gets a cached sponsor image if available. Returns null if not in cache.
    /// </summary>
    public Bitmap? GetCachedSponsorImage(string imageUrl) => GetCachedImage(imageUrl);
}
