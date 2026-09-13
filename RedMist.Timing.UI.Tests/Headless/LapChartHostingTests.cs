using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.Views;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers where a car's position chart goes when more than one place in the timing table can show
/// that car.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LapChartHostingTests
{
    /// <summary>The chart's fixed height, set where ChartViewModel builds it.</summary>
    private const double ChartHeight = 240;

    private static List<CarPosition> Laps(int count) =>
        [.. Enumerable.Range(1, count).Select(i => new CarPosition
        {
            Number = "7",
            LastLapCompleted = i,
            LastLapTime = $"00:01:{50 + i % 7:00}.000",
            OverallPosition = 1,
            ClassPosition = 1,
        })];

    private static void Layout(Layoutable view)
    {
        view.Measure(new Size(400, 800));
        view.Arrange(new Rect(0, 0, 400, 800));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Builds the real view around one car, expands it, and leaves its Positions tab showing.
    /// </summary>
    private static (LiveTimingViewModel ViewModel, LiveTimingView View, Window Window) CarShowingItsChart()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [new EventEntry { Number = "7", Name = "Car 7", Team = "Team", Class = "GP1" }]
        }));

        var view = new LiveTimingView { DataContext = vm };
        var window = new Window { Width = 400, Height = 800, Content = view };
        window.Show();
        Layout(view);

        var car = vm.Cars[0];
        car.IsDetailsExpanded = true;
        var details = car.CarDetailsViewModel!;

        // Safe for the reason LapListVirtualizationTests.ExpandedCarWithLaps gives: IsLoading is only
        // ever set true synchronously inside the expansion above, so nothing re-hides the tabs.
        details.IsLoading = false;
        var laps = Laps(30);
        details.LapList.UpdateLaps(laps);
        details.Chart.UpdateLaps(laps);
        details.IsChartTabSelected = true;
        Layout(view);

        return (vm, view, window);
    }

    private static ItemsControl GroupedList(LiveTimingView view, LiveTimingViewModel vm) =>
        view.GetVisualDescendants().OfType<ItemsControl>().Single(c => ReferenceEquals(c.ItemsSource, vm.GroupedCars));

    private static ItemsControl FlatList(LiveTimingView view, LiveTimingViewModel vm) =>
        view.GetVisualDescendants().OfType<ItemsControl>().Single(c => ReferenceEquals(c.ItemsSource, vm.Cars));

    /// <summary>
    /// On screen in <paramref name="list"/>, and laid out there rather than merely parented - a host
    /// that took the chart without arranging it would leave the area blank.
    /// </summary>
    private static void AssertShownIn(ItemsControl list, Control chart, string where)
    {
        Assert.IsTrue(list.IsVisualAncestorOf(chart), $"The chart belongs in the {where} list.");
        Assert.IsTrue(chart.IsEffectivelyVisible, $"The chart is in the {where} list but hidden.");
        Assert.IsTrue(chart.IsArrangeValid, $"The chart is in the {where} list but was never laid out there.");
        Assert.AreEqual(ChartHeight, chart.Bounds.Height, 1d, $"The chart is in the {where} list but not at its own size.");
    }

    [TestMethod]
    public Task GroupingByClass_MovesTheChartInsteadOfParentingItTwice() => HeadlessTest.OnDispatcher(() =>
    {
        HeadlessAppResources.Load();
        var (vm, view, window) = CarShowingItsChart();
        try
        {
            var chart = vm.Cars[0].CarDetailsViewModel!.Chart.Chart;
            AssertShownIn(FlatList(view, vm), chart, "flat");

            vm.CurrentGrouping = GroupMode.Class;
            Layout(view);

            // Class groups start collapsed, and a collapsed group builds no rows - opening one is what
            // realizes this car a second time.
            var group = GroupedList(view, vm).GetVisualDescendants().OfType<Expander>().First(e => e.DataContext is GroupHeaderViewModel);
            group.IsExpanded = true;
            Layout(view);

            AssertShownIn(GroupedList(view, vm), chart, "grouped");

            vm.CurrentGrouping = GroupMode.Overall;
            Layout(view);

            AssertShownIn(FlatList(view, vm), chart, "flat");
        }
        finally { window.Close(); }
    });
}
