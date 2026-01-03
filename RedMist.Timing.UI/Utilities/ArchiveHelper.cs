using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Utilities;

internal class ArchiveHelper
{
    public static async Task<T?> LoadArchivedData<T>(IHttpClientFactory httpClientFactory, string url)
    {
        using var httpClient = httpClientFactory.CreateClient();
        var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to download archived laps from {url}: {response.StatusCode}");
            return default;
        }

        // Get the compressed stream
        await using var compressedStream = await response.Content.ReadAsStreamAsync();

        // Decompress using GZipStream
        await using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);

        // Read the decompressed content for debugging
        using var memoryStream = new MemoryStream();
        var data = await JsonSerializer.DeserializeAsync<T>(gzipStream);
        return data;
    }
}
