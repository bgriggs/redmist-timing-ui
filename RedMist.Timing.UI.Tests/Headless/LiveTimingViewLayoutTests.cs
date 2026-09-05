using RedMist.Timing.UI.Views;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers the width the timing view caps its table at.
/// </summary>
/// <remarks>
/// The view sets MaxWidth from its own width less a margin, from inside the arrange pass that
/// raised the size change. Avalonia rejects a negative or NaN MaxWidth, and an exception there does
/// not merely mislay the layout - it reaches the dispatcher's unhandled handler, which the app
/// treats as fatal. A Galaxy Z Fold part way through a fold reported a width of zero, the
/// subtraction produced -10, and the app went down.
///
/// These drive the arithmetic rather than a real view. Constructing <c>LiveTimingView</c> needs the
/// resource dictionaries that <see cref="HeadlessTestApp"/> deliberately does not load - its
/// StaticResource lookups throw without them - and adding those resources would stop
/// <see cref="ResourceFallbackTests"/> testing anything, which that class documents as the reason
/// not to. So the guard is tested where it lives; that <c>OnSizeChanged</c> calls it is left to
/// inspection.
/// </remarks>
[TestClass]
public sealed class LiveTimingViewLayoutTests
{
    [TestMethod]
    [DataRow(0d, DisplayName = "Zero, as a fold reports mid-transition")]
    [DataRow(-1d, DisplayName = "Negative")]
    [DataRow(double.NaN, DisplayName = "Not measured yet")]
    public void ADegenerateWidth_YieldsNoWidthAtAll(double viewWidth)
    {
        Assert.IsNull(LiveTimingView.TableMaxWidthFor(viewWidth),
            "There is nothing to lay out at this size, and the caller has to be told so rather than handed a number.");
    }

    /// <summary>
    /// The margin is subtracted, so widths at or below it are where the sign flips.
    /// </summary>
    [TestMethod]
    [DataRow(1d)]
    [DataRow(9d)]
    [DataRow(10d)]
    [DataRow(11d)]
    public void AWidthAroundTheMargin_IsNeverNegative(double viewWidth)
    {
        var maxWidth = LiveTimingView.TableMaxWidthFor(viewWidth);

        Assert.IsNotNull(maxWidth, "A positive width is usable, however small.");
        Assert.IsTrue(maxWidth >= 0, $"Avalonia rejects a negative MaxWidth; got {maxWidth} for {viewWidth}.");
    }

    [TestMethod]
    public void AnOrdinaryWidth_LeavesTheMargin()
    {
        Assert.AreEqual(290d, LiveTimingView.TableMaxWidthFor(300));
    }

    [TestMethod]
    public void TheResult_IsNeverNaN()
    {
        // NaN is rejected the same as a negative, and it propagates silently through the subtraction.
        foreach (var width in new[] { 0d, 1d, 10d, 300d, double.NaN, double.MaxValue })
        {
            var maxWidth = LiveTimingView.TableMaxWidthFor(width);
            Assert.IsFalse(maxWidth is double value && double.IsNaN(value), $"Width {width} produced NaN.");
        }
    }
}
