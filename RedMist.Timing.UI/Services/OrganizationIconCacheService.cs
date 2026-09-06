using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Singleton service that caches organization icons as Bitmaps to avoid redundant loading and decoding.
/// Uses CDN for fetching icons, over the persistent store that keeps them between runs.
/// </summary>
public class OrganizationIconCacheService : ImageCacheServiceBase<int>
{
    private readonly OrganizationClient organizationClient;

    public OrganizationIconCacheService(OrganizationClient organizationClient, PersistentImageStore store, ILoggerFactory loggerFactory)
        : base(store, loggerFactory.CreateLogger<OrganizationIconCacheService>())
    {
        this.organizationClient = organizationClient;
    }

    protected override string GetKeyDisplayName(int key) => $"organization {key}";

    protected override string? GetImageUrl(int key) => organizationClient.GetOrganizationIconCdnUrl(key);

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
