using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Singleton service that caches organization icons as Bitmaps to avoid redundant loading and decoding.
/// Uses CDN for fetching icons and maintains an in-memory cache for performance.
/// </summary>
public class OrganizationIconCacheService : ImageCacheServiceBase<int>
{
    private readonly OrganizationClient organizationClient;

    public OrganizationIconCacheService(OrganizationClient organizationClient, ILoggerFactory loggerFactory)
        : base(loggerFactory.CreateLogger<OrganizationIconCacheService>())
    {
        this.organizationClient = organizationClient;
    }

    protected override string GetKeyDisplayName(int key) => $"organization {key}";

    protected override async Task<byte[]> LoadImageBytesAsync(int key)
    {
        return await organizationClient.GetOrganizationIconCdnAsync(key);
    }

    /// <summary>
    /// Gets the organization icon as a Bitmap from cache or loads it from CDN.
    /// </summary>
    public Task<Bitmap?> GetOrganizationIconAsync(int organizationId) => GetImageAsync(organizationId);

    /// <summary>
    /// Synchronously gets a cached icon if available. Returns null if not in cache.
    /// </summary>
    public Bitmap? GetCachedIcon(int organizationId) => GetCachedImage(organizationId);

    /// <summary>
    /// Preloads icons for multiple organizations in parallel.
    /// </summary>
    public Task PreloadIconsAsync(params int[] organizationIds) => PreloadImagesAsync(organizationIds);
}
