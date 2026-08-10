using Avalonia.Controls;
using Avalonia.Data;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Checks that the timing grid's computed columns actually reach a binding.
/// </summary>
/// <remarks>
/// Gap, Difference and Position are computed from several backing fields and are notified by a mix
/// of [NotifyPropertyChangedFor] attributes and hand-written OnPropertyChanged calls in the mode
/// setters. Adding a new source field and forgetting to wire the notification breaks the column
/// silently: the value is right whenever the row happens to be rebuilt, and stale otherwise.
/// Asserting on the view model alone cannot catch that, because reading the property recomputes it.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class BindingTests
{
    private static TextBlock BoundTo(object viewModel, string path)
    {
        var text = new TextBlock { DataContext = viewModel };
        text.Bind(TextBlock.TextProperty, new Binding(path));
        return text;
    }

    private static CarViewModel CarWithGaps()
    {
        var car = TestViewModelFactory.CreateCar();
        car.OverallGap = "2.000";
        car.OverallDifference = "0.800";
        car.InClassGap = "1.234";
        car.OverallGapByFastTime = "5.300";
        car.InClassGapByFastTime = "3.100";
        return car;
    }

    [TestMethod]
    public Task GapColumn_FollowsTheSortMode() => HeadlessTest.OnDispatcher(() =>
    {
        var car = CarWithGaps();
        var cell = BoundTo(car, nameof(CarViewModel.Gap));
        Assert.AreEqual("2.000", cell.Text, "Sanity check - the binding evaluated.");

        car.CurrentSortMode = SortMode.Fastest;

        Assert.AreEqual("5.300", cell.Text, "Switching to the fastest sort swaps in the fast-time gap.");
    });

    [TestMethod]
    public Task GapColumn_FollowsTheGrouping() => HeadlessTest.OnDispatcher(() =>
    {
        var car = CarWithGaps();
        car.CurrentSortMode = SortMode.Fastest;
        var cell = BoundTo(car, nameof(CarViewModel.Gap));

        car.CurrentGroupMode = GroupMode.Class;

        Assert.AreEqual("3.100", cell.Text);
    });

    [TestMethod]
    public Task GapColumn_FollowsAServerUpdate() => HeadlessTest.OnDispatcher(() =>
    {
        var car = CarWithGaps();
        var cell = BoundTo(car, nameof(CarViewModel.Gap));

        car.ApplyPatch(new CarPositionPatch { Number = "1", OverallGap = "9.999" });

        Assert.AreEqual("9.999", cell.Text);
    });

    [TestMethod]
    public Task PositionColumn_FollowsTheGrouping() => HeadlessTest.OnDispatcher(() =>
    {
        var car = TestViewModelFactory.CreateCar();
        car.OverallPosition = 7;
        car.ClassPosition = 2;
        var cell = BoundTo(car, nameof(CarViewModel.Position));
        Assert.AreEqual("7", cell.Text);

        car.CurrentGroupMode = GroupMode.Class;

        Assert.AreEqual("2", cell.Text);
    });

    /// <summary>
    /// The grid's ItemsControl is bound to the collection DynamicData projects into, so the
    /// add/remove/move notifications have to survive the trip from the cache to the control.
    /// </summary>
    [TestMethod]
    public Task TheGridControlTracksTheProjectedCollection() => HeadlessTest.OnDispatcher(() =>
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        var grid = new ItemsControl { ItemsSource = vm.Cars };

        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries =
            [
                new EventEntry { Number = "1", Name = "One", Team = "T", Class = "GP1" },
                new EventEntry { Number = "2", Name = "Two", Team = "T", Class = "GP1" },
            ],
        }));
        Assert.AreEqual(2, grid.ItemCount, "Entries should have reached the control.");

        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [new EventEntry { Number = "2", Name = "Two", Team = "T", Class = "GP1" }],
        }));

        Assert.AreEqual(1, grid.ItemCount, "A withdrawn car should have left the control too.");
    });
}
