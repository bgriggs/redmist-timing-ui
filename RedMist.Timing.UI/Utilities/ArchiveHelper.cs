using Microsoft.Extensions.Logging;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Utilities;

internal class ArchiveHelper
{
    public static async Task<T?> DownloadArchivedDataAsync<T>(IHttpClientFactory httpClientFactory, string url, ILogger logger)
    {
        using var httpClient = httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            // A missing object is routine - not every event has every archived artifact - so don't
            // spend a slot in the retained warning buffer on it.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogDebug("No archived data at {Url}", url);
            }
            else
            {
                logger.LogWarning("Failed to download archived data from {Url}: {StatusCode}", url, response.StatusCode);
            }
            return default;
        }

        // Get the compressed stream
        await using var compressedStream = await response.Content.ReadAsStreamAsync();

        // Decompress using GZipStream
        await using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);

        return await JsonSerializer.DeserializeAsync<T>(gzipStream);
    }
}
