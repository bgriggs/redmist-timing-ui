using RedMist.Timing.UI.Views;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers which rows the timing view counts as being on screen when the standings are shared.
/// </summary>
/// <remarks>
/// The card promises to show what the viewer can see. The view works that out by measuring each
/// realized row against the scroll viewport - the table is a plain ItemsControl, so every row exists
/// whether or not it is visible - and this is the arithmetic behind that.
///
/// Driven directly rather than through a real view, for the reason
/// <see cref="LiveTimingViewLayoutTests"/> sets out: constructing <c>LiveTimingView</c> needs the
/// resource dictionaries <see cref="HeadlessTestApp"/> deliberately does not load. That leaves the
/// visual-tree walk itself - picking the visible list, skipping group headers, and excluding a
/// folded class - to inspection.
/// </remarks>
[TestClass]
public sealed class LiveTimingViewOnScreenTests
{
    private const double Viewport = 800;
    private const double RowHeight = 60;

    [TestMethod]
    public void ARowInTheMiddle_IsOnScreen()
    {
        Assert.IsTrue(LiveTimingView.IsRowOnScreen(top: 300, RowHeight, Viewport));
    }

    [TestMethod]
    public void ARowScrolledOffTheTop_IsNot()
    {
        Assert.IsFalse(LiveTimingView.IsRowOnScreen(top: -RowHeight, RowHeight, Viewport));
    }

    [TestMethod]
    public void ARowBelowTheFold_IsNot()
    {
        Assert.IsFalse(LiveTimingView.IsRowOnScreen(top: Viewport, RowHeight, Viewport));
    }

    /// <summary>
    /// A row counts when any part of it is inside the viewport, which is the same rule the web card
    /// applies to its DOM rows - a half-visible row at either edge is one the viewer can see.
    /// </summary>
    [TestMethod]
    [DataRow(-1d, DisplayName = "One pixel above the top edge")]
    [DataRow(-(RowHeight - 1), DisplayName = "All but one pixel above the top edge")]
    [DataRow(Viewport - 1, DisplayName = "One pixel showing at the bottom")]
    public void APartlyVisibleRow_Counts(double top)
    {
        Assert.IsTrue(LiveTimingView.IsRowOnScreen(top, RowHeight, Viewport));
    }

    /// <summary>
    /// Both edges are exclusive: a row resting exactly on one contributes no visible pixels.
    /// </summary>
    [TestMethod]
    public void ARowRestingExactlyOnAnEdge_DoesNot()
    {
        Assert.IsFalse(LiveTimingView.IsRowOnScreen(top: -RowHeight, RowHeight, Viewport),
            "Its bottom edge is the top of the viewport.");
        Assert.IsFalse(LiveTimingView.IsRowOnScreen(top: Viewport, RowHeight, Viewport),
            "Its top edge is the bottom of the viewport.");
    }

    /// <summary>
    /// A row that has not been arranged has no height, and a row inside a collapsed group can report
    /// one. Neither is something the viewer is looking at.
    /// </summary>
    [TestMethod]
    [DataRow(0d, DisplayName = "Never arranged")]
    [DataRow(-5d, DisplayName = "Nonsense height")]
    public void ARowWithNoHeight_IsNot(double height)
    {
        Assert.IsFalse(LiveTimingView.IsRowOnScreen(top: 100, height, Viewport));
    }

    /// <summary>
    /// A NaN reaches here the same way it reached the table width guard - a foldable part way
    /// through a fold - and would otherwise make every comparison false in a way that reads as a
    /// deliberate answer.
    /// </summary>
    [TestMethod]
    public void ARowThatCannotBeMeasured_IsNot()
    {
        Assert.IsFalse(LiveTimingView.IsRowOnScreen(double.NaN, RowHeight, Viewport));
        Assert.IsFalse(LiveTimingView.IsRowOnScreen(100, double.NaN, Viewport));
    }

    /// <summary>An expanded row is taller than the window and is certainly on screen.</summary>
    [TestMethod]
    public void ARowTallerThanTheViewport_IsOnScreen()
    {
        Assert.IsTrue(LiveTimingView.IsRowOnScreen(top: -100, height: Viewport * 2, Viewport));
    }
}
