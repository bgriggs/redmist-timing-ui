using Avalonia.Platform;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Guards the dimensions of every embedded PNG asset.
/// </summary>
/// <remarks>
/// A decoded bitmap costs width x height x 4 bytes no matter how small it is drawn, and nothing
/// else in the build enforces any relationship between an asset's size and its use. That is not a
/// hypothetical: the streaming-provider logos shipped at 6159x1541 - 36 MB decoded, each - to draw
/// an icon 13 units tall, and sat in memory from startup because the legend binds them whether or
/// not it is shown. The largest legitimate asset is around 300 pixels wide, so the cap below has
/// headroom while still catching that class of mistake by three orders of magnitude.
///
/// The dimensions are read straight out of the PNG header rather than through Avalonia: the
/// headless platform's drawing stub does not really decode, so a bitmap's reported size under test
/// bears no relation to the file. IHDR is mandatory, first, and fixed-layout, so the parse is two
/// big-endian reads at fixed offsets.
///
/// Runs under the headless session only for <see cref="AssetLoader"/>, which enumerates what is
/// actually embedded - a file present on disk but missing from AvaloniaResource would silently
/// escape a directory scan of the source tree.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AssetImageTests
{
    private const int MaxDimension = 512;

    [TestMethod]
    public Task EmbeddedPngs_StayReasonablySized() => HeadlessTest.OnDispatcher(() =>
    {
        var assets = AssetLoader.GetAssets(new Uri("avares://RedMist.Timing.UI/Assets/"), null)
            .Where(uri => uri.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        // If enumeration ever comes back empty the loop below would pass vacuously, and the guard
        // would be gone without a single test failing.
        Assert.IsTrue(assets.Length > 0, "No embedded PNG assets found; the asset guard is not guarding anything.");

        foreach (var uri in assets)
        {
            using var stream = AssetLoader.Open(uri);
            var header = new byte[24];
            stream.ReadExactly(header);

            // 137 P N G - anything else means the .png extension is lying about the contents,
            // which deserves its own failure rather than a nonsense dimension read.
            Assert.IsTrue(header[0] == 137 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
                $"{uri} does not start with a PNG signature.");

            var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];

            Assert.IsTrue(width <= MaxDimension && height <= MaxDimension,
                $"{uri} is {width}x{height}, which decodes to {width * (long)height * 4 / 1024} KB. " +
                $"Resize the asset to what it is drawn at (cap {MaxDimension}px) instead of shipping " +
                "the original artwork; the decoded bitmap stays in memory for the whole session.");
        }
    });
}
