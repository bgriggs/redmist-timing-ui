using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RedMist.Timing.UI.Controls;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers the contract of <see cref="SharedControlHost"/> on its own, away from the timing view.
/// </summary>
/// <remarks>
/// <see cref="LapChartHostingTests"/> shows the real template no longer crashes; these pin down the
/// behavior it relies on, each in the smallest arrangement that exercises it.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class SharedControlHostTests
{
    private static void Layout(Layoutable root)
    {
        root.Measure(new Size(400, 400));
        root.Arrange(new Rect(0, 0, 400, 400));
        Dispatcher.UIThread.RunJobs();
    }

    private static (Window Window, StackPanel Panel) Shown(params Control[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children)
            panel.Children.Add(child);

        var window = new Window { Width = 400, Height = 400, Content = panel };
        window.Show();
        Layout(panel);
        return (window, panel);
    }

    [TestMethod]
    public Task TheChild_GoesToWhicheverHostIsShowing() => HeadlessTest.OnDispatcher(() =>
    {
        var shared = new Border { Height = 50 };
        var first = new SharedControlHost { Child = shared };
        var second = new SharedControlHost { Child = shared, IsVisible = false };
        var (window, panel) = Shown(first, second);
        try
        {
            Assert.AreSame(first, shared.GetVisualParent(), "Sanity check - the only visible host has it.");

            first.IsVisible = false;
            second.IsVisible = true;
            Layout(panel);
            Assert.AreSame(second, shared.GetVisualParent(), "Shown in the second host once that is the one on screen.");
            Assert.AreSame(second, shared.Parent, "The logical parent moves with the visual one.");

            second.IsVisible = false;
            first.IsVisible = true;
            Layout(panel);
            Assert.AreSame(first, shared.GetVisualParent(), "And back again, which needs the first host to have asked a second time.");
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task AHiddenHost_DoesNotTakeTheChild() => HeadlessTest.OnDispatcher(() =>
    {
        // The whole approach rests on this: only a host being laid out claims the child.
        var shared = new Border { Height = 50 };
        var visible = new SharedControlHost { Child = shared };
        var (window, panel) = Shown(visible);
        try
        {
            panel.Children.Add(new SharedControlHost { Child = shared, IsVisible = false });
            Layout(panel);

            Assert.AreSame(visible, shared.GetVisualParent());
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task AHostTakenOutOfTheTree_StillGivesTheChildUp() => HeadlessTest.OnDispatcher(() =>
    {
        // How a tab switched away from, or a row torn down late, lets go: it has left the tree but
        // still has the child, and nothing will measure it again to tell it otherwise.
        var shared = new Border { Height = 50 };
        var removed = new SharedControlHost { Child = shared };
        var (window, panel) = Shown(removed);
        try
        {
            panel.Children.Remove(removed);
            var replacement = new SharedControlHost { Child = shared };
            panel.Children.Add(replacement);
            Layout(panel);

            Assert.AreSame(shared, removed.Child, "Sanity check - the removed host was never told to let go.");
            Assert.AreSame(replacement, shared.GetVisualParent());
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task TwoHostsOnScreen_LeaveTheChildWhereItIs() => HeadlessTest.OnDispatcher(() =>
    {
        // Not a layout the app builds, but one a duplicated row would. Taking the child back and
        // forth would invalidate each host in turn and layout would never settle, so the host that
        // has it keeps it and the other shows nothing.
        var shared = new Border { Height = 50 };
        var holder = new SharedControlHost { Child = shared };
        var (window, panel) = Shown(holder);
        try
        {
            var latecomer = new SharedControlHost { Child = shared };
            panel.Children.Add(latecomer);

            // Measured directly rather than through the layout manager, so what is asserted is the
            // first answer and not whichever host won the last of many passes.
            panel.Measure(new Size(400, 400));

            Assert.AreSame(holder, shared.GetVisualParent());
            Assert.AreEqual(0, latecomer.DesiredSize.Height, "The host without the child takes no space.");
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task ReplacingTheChild_ReleasesTheOldOne() => HeadlessTest.OnDispatcher(() =>
    {
        var before = new Border { Height = 50 };
        var after = new Border { Height = 50 };
        var host = new SharedControlHost { Child = before };
        var (window, panel) = Shown(host);
        try
        {
            host.Child = after;
            Layout(panel);

            Assert.IsNull(before.GetVisualParent(), "The replaced child must be free to go elsewhere.");
            Assert.IsNull(before.Parent);
            Assert.AreSame(host, after.GetVisualParent());
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task ClearingTheChild_ReleasesItWithoutWaitingForLayout() => HeadlessTest.OnDispatcher(() =>
    {
        // A row being torn down loses its data context and may never be measured again, so the
        // release cannot wait for a layout pass that will not come.
        var shared = new Border { Height = 50 };
        var host = new SharedControlHost { Child = shared };
        var (window, _) = Shown(host);
        try
        {
            host.Child = null;

            Assert.IsNull(shared.GetVisualParent());
            Assert.IsNull(shared.Parent);
        }
        finally { window.Close(); }
    });

    [TestMethod]
    public Task AChildHeldByAnotherKindOfParent_IsLeftAlone() => HeadlessTest.OnDispatcher(() =>
    {
        // Only a host can be asked to give its child up. Anything else is left in place, and the host
        // shows nothing, because the alternative is an exception from inside layout.
        var shared = new Border { Height = 50 };
        var owner = new Decorator { Child = shared };
        var host = new SharedControlHost { Child = shared };
        var (window, panel) = Shown(owner, host);
        try
        {
            Layout(panel);

            Assert.AreSame(owner, shared.GetVisualParent());
            Assert.AreEqual(0, host.DesiredSize.Height, "The host takes no space for a child it does not have.");
        }
        finally { window.Close(); }
    });
}
