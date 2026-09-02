using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Concurrent;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Decodes embedded image assets once and hands every caller the same bitmap.
/// </summary>
/// <remarks>
/// Exists because these images were being decoded per use site, and in the legend's case per
/// binding evaluation - a plain getter did <c>new Bitmap(...)</c> every time it was read. That was
/// survivable only while nobody noticed the price: a decoded bitmap costs width x height x 4 bytes
/// regardless of how small it is drawn, and the streaming-provider logo shipped at 6159x1541 - 36 MB
/// decoded, to draw a 13-pixel icon. The asset is right-sized now, and
/// <c>AssetImageTests</c> fails the build if an oversized one ever comes back, but decoding once is
/// correct at any size.
///
/// Keyed by URI rather than by resource key, because the theme dictionaries map one key to a
/// different file per variant - a light and a dark logo cached under one key would pin whichever
/// variant asked first for the rest of the session.
///
/// Never disposes what it hands out, for the same reason as <see cref="ImageCacheServiceBase{TKey}"/>:
/// the bitmaps go straight into Image.Source bindings, and disposing one still on screen frees the
/// Skia surface out from under the renderer. Nothing evicts either - the cache is bounded by the
/// set of embedded assets. A race on first use can decode the same asset twice and drop one copy;
/// with right-sized assets that is kilobytes, not worth a lock.
/// </remarks>
internal static class AssetImageCache
{
    private static readonly ConcurrentDictionary<string, IImage> cache = new();

    /// <summary>
    /// The image an avares URI points at, decoded on first use.
    /// </summary>
    public static IImage Get(string avaresUri) =>
        cache.GetOrAdd(avaresUri, static uri =>
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            return new Bitmap(stream);
        });

    /// <summary>
    /// Resolves a theme-variant resource key to its image, or null when it cannot - the designer,
    /// and unit tests, where there is no application or no resource dictionary to resolve against.
    /// </summary>
    public static IImage? GetThemed(string resourceKey)
    {
        if (Application.Current?.FindResource(Application.Current.ActualThemeVariant, resourceKey) is string uri)
        {
            return Get(uri);
        }

        return null;
    }
}
