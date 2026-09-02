using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.ViewModels.CarDetails;
using RedMist.Timing.UI.Views;
using RedMist.TimingCommon.Models;
using System.Collections;
using System.Reflection;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers how much of a car's lap list is actually built, against the real view.
/// </summary>
/// <remarks>
/// An enduro entry has hundreds of laps and the list showing them is 250px tall, so all but about
/// twenty rows are off-screen. A row is fifteen declared controls and roughly twenty visuals once
/// its container is counted, and a StackPanel builds every one: before this was virtualized,
/// expanding a single car in a 155 lap session put 3379 visuals into the tree in order to show
/// nineteen of them. Collapsing does release them - CarViewModel nulls CarDetailsViewModel, so the
/// items go away with it - but the phone does not hand the memory back, so what is retained is
/// whatever stays expanded.
///
/// The reason this is safe here and not everywhere is row height. VirtualizingStackPanel estimates
/// the scroll extent from the rows it has realized, so with uniform rows the estimate is exact and
/// scrolling is indistinguishable - which VirtualizingKeepsTheScrollExtentExact pins. Where heights
/// vary the estimate is wrong and the scrollbar resizes as it is dragged; see
/// TheCarTableIsDeliberatelyNotVirtualized.
///
/// One blind spot to know about: Avalonia's headless font manager reports the same metrics for
/// every typeface, so the bold row that TimeFontWeight produces measures the same here as a normal
/// one no matter what it would do on a device. These tests can show that the template's own
/// structure keeps rows uniform; they cannot show that font fallback does.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LapListVirtualizationTests
{
    private const int LapCount = 155;

    /// <summary>
    /// Puts the app's own resources and a control theme in reach of the view under test.
    /// </summary>
    /// <remarks>
    /// HeadlessTestApp deliberately loads neither - see its remarks - so the view's StaticResource
    /// lookups for geometries and converters would throw, and with no control theme an ItemsControl
    /// gets no template and so never builds a panel at all. Both are added inside the dispatch and
    /// go away with it, leaving ResourceFallbackTests the bare application it needs.
    ///
    /// The trampoline is how Avalonia's own generated InitializeComponent loads compiled XAML.
    /// AvaloniaXamlLoader.Load cannot stand in for it: that fails on an Application built outside an
    /// AppBuilder, which is exactly the situation here.
    /// </remarks>
    private static void LoadAppResources()
    {
        var app = new RedMist.Timing.UI.App();
        var populate = typeof(RedMist.Timing.UI.App).GetMethod("!XamlIlPopulateTrampoline",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(populate, "Avalonia no longer emits the populate trampoline - this needs another way to reach App.axaml's resources.");
        populate.Invoke(null, [app]);

        // Copied entry by entry: a ResourceDictionary refuses to be merged into a second owner.
        var resources = new ResourceDictionary();
        foreach (var entry in app.Resources)
            resources.Add(entry.Key, entry.Value!);

        Application.Current!.Resources.MergedDictionaries.Add(resources);
        Application.Current!.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }

    /// <summary>
    /// Laps that fill in every part of the row template, so the heights being compared are the
    /// heights of a populated row.
    /// </summary>
    /// <remarks>
    /// The times have to be exactly "hh:mm:ss.fff" and "h:mm:ss.fff" or LapViewModel's TryParseExact
    /// leaves both columns empty, which quietly measures the emptiest possible row. The positions
    /// move so gained and lost arrows appear and disappear; one lap is the fastest, so exactly one
    /// row draws bold through TimeFontWeight; driver names vary in length and some are absent.
    /// </remarks>
    private static List<CarPosition> Laps(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new CarPosition
        {
            Number = "7",
            LastLapCompleted = i,
            // Lap 7 is the quickest, so it is the one that goes bold.
            LastLapTime = i == 7 ? "00:01:48.221" : $"00:01:{52 + i % 7:00}.{i * 7 % 1000:000}",
            TotalTime = $"{i / 30}:{i * 2 % 60:00}:{i * 13 % 60:00}.000",
            OverallPosition = 3 + i % 3,
            ClassPosition = 1 + i % 2,
            DriverName = i % 4 == 0 ? string.Empty : (i % 3 == 0 ? "Alexandra Fitzwilliam-Hart" : "J. Doe"),
            LapIncludedPit = i % 20 == 0,
            TrackFlag = i % 11 == 0 ? Flags.Yellow : Flags.Green,
        })];

    /// <summary>
    /// Builds the real view around one car, expands it, and fills it with laps.
    /// </summary>
    private static (LiveTimingView View, Window Window) ExpandedCarWithLaps(int lapCount)
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [new EventEntry { Number = "7", Name = "Car 7", Team = "Team", Class = "GP1" }]
        }));

        var view = new LiveTimingView { DataContext = vm };
        var window = new Window { Width = 400, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Layout(view);

        var car = vm.Cars[0];
        car.IsDetailsExpanded = true;
        var details = car.CarDetailsViewModel!;

        // Expanding starts a real load against an unreachable localhost, which sets IsLoading and
        // hides the whole tab strip. Standing in for its result is safe, but not because the load
        // loses a race - it gives up after three tries in a couple of seconds and can easily finish
        // first. It is safe because IsLoading is only ever set to true synchronously, on this
        // thread, inside the assignment above: InvokeOnUIThread runs inline when it already has the
        // dispatcher. So nothing can re-hide the tabs after this line. The late continuations are
        // no-ops - a failed lap load returns an empty list, which UpdateLaps ignores.
        details.IsLoading = false;
        details.LapList.UpdateLaps(Laps(lapCount));
        Layout(view);

        return (view, window);
    }

    private static void Layout(Layoutable view)
    {
        view.Measure(new Size(400, 800));
        view.Arrange(new Rect(0, 0, 400, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static ItemsControl ListBoundTo(LiveTimingView view, object itemsSource) =>
        view.GetVisualDescendants().OfType<ItemsControl>().Single(c => ReferenceEquals(c.ItemsSource, itemsSource));

    private static object LapsOf(LiveTimingView view) =>
        ((LiveTimingViewModel)view.DataContext!).Cars[0].CarDetailsViewModel!.LapList.Laps;

    [TestMethod]
    public Task OnlyTheVisibleLapsAreBuilt() => HeadlessTest.OnDispatcher(() =>
    {
        LoadAppResources();
        var (view, window) = ExpandedCarWithLaps(LapCount);

        var laps = ListBoundTo(view, LapsOf(view));
        var realized = laps.ItemsPanelRoot!.Children.Count;

        Assert.AreEqual(LapCount, ((ICollection)laps.ItemsSource!).Count, "Sanity check - every lap reached the list.");
        Assert.IsInstanceOfType<VirtualizingStackPanel>(laps.ItemsPanelRoot, "The lap list has to virtualize; a StackPanel builds every row.");
        Assert.IsLessThan(40, realized, $"Only the rows near a 250px viewport should be built, but {realized} of {LapCount} were.");

        window.Close();
    });

    [TestMethod]
    public Task VirtualizingKeepsTheScrollExtentExact() => HeadlessTest.OnDispatcher(() =>
    {
        // The property that makes this safe, and the whole reason the car table below is left alone:
        // the panel's idea of the full list height has to match the real one, or the scrollbar
        // changes size while it is being dragged.
        //
        // The comparison has to be against a real measurement. VirtualizingStackPanel estimates the
        // extent by extrapolating from the rows it realized, so checking it against the same
        // extrapolation - one row's height times the count - agrees with itself no matter how wrong
        // both are. Measuring the same template through a StackPanel builds every row for real.
        LoadAppResources();
        var (view, window) = ExpandedCarWithLaps(LapCount);

        var laps = ListBoundTo(view, LapsOf(view));
        var virtualizedHeight = laps.Bounds.Height;
        var realized = laps.ItemsPanelRoot!.Children.Count;

        laps.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
        Layout(view);
        var builtInFull = laps.Bounds.Height;

        Assert.IsInstanceOfType<StackPanel>(laps.ItemsPanelRoot, "Sanity check - the panel was swapped.");
        Assert.AreEqual(LapCount, laps.ItemsPanelRoot!.Children.Count, "Sanity check - the StackPanel built every row, so its height is the true one.");
        Assert.IsGreaterThan(0, builtInFull, "Sanity check - the rows measured to something.");
        Assert.AreEqual(expected: builtInFull, actual: virtualizedHeight, delta: 1.0,
            message: $"Virtualized the list is {virtualizedHeight}px tall; built in full it is {builtInFull}px. A scrollbar sized " +
                     $"from the estimate would resize as it was dragged. Row heights are probably no longer uniform ({realized} rows were realized).");

        window.Close();
    });

    /// <summary>
    /// Which lap is sitting at the top of the 250px window, whether or not its row was one of the
    /// ones actually built.
    /// </summary>
    private static int LapUnderViewportTop(ItemsControl laps, double offset)
    {
        var row = laps.ItemsPanelRoot!.Children
            .Single(c => c.Bounds.Top <= offset && offset < c.Bounds.Bottom);
        return ((LapViewModel)row.DataContext!).LapNumber;
    }

    /// <summary>
    /// Scrolls into the list, lets one more lap arrive above the viewer, and reports what they were
    /// looking at either side of that.
    /// </summary>
    private static (int Before, int After, double Extent) LapArrivesWhileScrolled(bool virtualize)
    {
        var (view, window) = ExpandedCarWithLaps(LapCount);
        try
        {
            var laps = ListBoundTo(view, LapsOf(view));
            if (!virtualize)
            {
                laps.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel());
                Layout(view);
            }

            var scroll = laps.FindAncestorOfType<ScrollViewer>()!;
            scroll.Offset = new Vector(0, 600);
            Layout(view);
            Assert.IsGreaterThan(0, scroll.Offset.Y, "Sanity check - the list actually scrolled, or nothing below is being tested.");
            var before = LapUnderViewportTop(laps, scroll.Offset.Y);

            // Laps sort newest first, so a completed lap lands at index 0 - above everything the
            // viewer is looking at.
            var details = ((LiveTimingViewModel)view.DataContext!).Cars[0].CarDetailsViewModel!;
            details.LapList.UpdateLaps(Laps(LapCount + 1).TakeLast(1).ToList());
            Layout(view);

            Assert.AreEqual(LapCount + 1, details.LapList.Laps.Count, "Sanity check - the lap was added.");
            return (before, LapUnderViewportTop(laps, scroll.Offset.Y), laps.Bounds.Height);
        }
        finally { window.Close(); }
    }

    [TestMethod]
    public Task ALapArrivingMovesTheViewNoDifferentlyThanBefore() => HeadlessTest.OnDispatcher(() =>
    {
        // The list updates live during a session and completed laps land at the top, above wherever
        // the viewer is reading. This is the scenario most likely to go wrong, so it is worth
        // walking even where the assertions are hard to break.
        //
        // Be clear about which of them carry weight. The extent check is the one demonstrated to
        // fail: given rows of differing heights it reports 23559px against a true 12312px. The two
        // lap checks are scenario coverage - deliberately asking which lap the viewer is looking at
        // rather than what the scroll offset reads, because the offset stays put either way and so
        // agrees no matter how badly the rows underneath it are mapped. They also insist, through
        // Single, that exactly one realized row covers the top of the window: gaps or overlaps in
        // what the panel laid out would throw there rather than pass quietly.
        LoadAppResources();

        var virtualized = LapArrivesWhileScrolled(virtualize: true);
        var plain = LapArrivesWhileScrolled(virtualize: false);

        Assert.AreEqual(plain.Before, virtualized.Before,
            $"Scrolled to the same place, the virtualized list shows lap {virtualized.Before} where the full one shows {plain.Before}.");
        Assert.AreEqual(plain.After, virtualized.After,
            $"After a lap arrived above them, the virtualized list moved the viewer to lap {virtualized.After} and the full one to {plain.After}.");
        Assert.AreEqual(expected: plain.Extent, actual: virtualized.Extent, delta: 1.0,
            message: $"After the insert the virtualized list measured {virtualized.Extent}px against a true {plain.Extent}px.");
    });

    [TestMethod]
    public Task TheCarTableIsDeliberatelyNotVirtualized() => HeadlessTest.OnDispatcher(() =>
    {
        // Not an oversight. A car row is 33px collapsed and roughly 300px expanded, and
        // VirtualizingStackPanel sizes the scrollbar from the rows it has realized. Measured on 60
        // rows with every fifth expanded, it called the extent 2247px against a true 5184px, and
        // scrolling grew it to 3983px mid-gesture: the thumb resizes and the position jumps. Only
        // virtualize this alongside something that knows the unrealized rows' heights.
        //
        // This covers the flat list. The grouped one a few lines below it in the view uses the same
        // car template under the same scroller and carries the same risk, but it is only built when
        // the grouping is on, so it is not reachable from here.
        LoadAppResources();
        var (view, window) = ExpandedCarWithLaps(LapCount);

        var cars = ListBoundTo(view, ((LiveTimingViewModel)view.DataContext!).Cars);

        // Asserting a negative passes just as happily on a panel that was never built, which is
        // what would happen if this list stopped being the visible one.
        Assert.IsNotNull(cars.ItemsPanelRoot, "The flat car list built no panel, so the assertion below would prove nothing.");
        Assert.IsNotInstanceOfType<VirtualizingPanel>(cars.ItemsPanelRoot,
            "Car rows vary in height, so virtualizing them makes the scrollbar resize while it is dragged.");

        window.Close();
    });
}
