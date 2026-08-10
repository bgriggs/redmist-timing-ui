using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Mappers;
using System.Collections.Specialized;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers the thread affinity of the live timing grid.
/// </summary>
/// <remarks>
/// Hub notifications arrive on a background thread, and <c>Cars</c> and <c>GroupedCars</c> are
/// projected straight into the ItemsControls by DynamicData's SortAndBind, so every one of these
/// paths has to marshal before it touches them.
///
/// What actually happens when the marshalling is removed is worth knowing, because it is not a
/// crash: building a row constructs a <c>SolidColorBrush</c> for the class color, Avalonia objects
/// have thread affinity, and the resulting exception is caught and logged by ApplySessionUpdate.
/// The grid simply stays empty and the app carries on looking healthy. That is why these tests
/// assert the update landed as well as which thread it landed on - a watcher alone would see no
/// mutations and conclude everything was fine.
///
/// An ordinary unit test cannot see any of this: with no Avalonia platform, CheckAccess answers
/// true everywhere, no object has thread affinity, and the marshalling is skipped without complaint.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LiveTimingDispatcherTests
{
    private static EventEntry Entry(string number) => new() { Number = number, Name = "Car " + number, Team = "T", Class = "GP1" };

    private readonly List<object> created = [];

    /// <summary>
    /// These view models are live, unlike the ones the non-headless tests build, so they act on
    /// messenger traffic. Nothing unregisters them on its own and the messenger holds them until
    /// they are collected, so without this a leaked view model from an earlier test keeps
    /// responding to anything a later test sends - doing real work whose failures are swallowed.
    /// </summary>
    [TestCleanup]
    public void UnregisterFromTheMessenger()
    {
        foreach (var vm in created)
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
        created.Clear();
    }

    private LiveTimingViewModel CreateLive()
    {
        var vm = Track(TestViewModelFactory.CreateLiveTiming());
        vm.IsRealTime = true;
        return vm;
    }

    private T Track<T>(T vm) where T : notnull
    {
        created.Add(vm);
        return vm;
    }

    /// <summary>
    /// Records the thread every collection mutation happened on. Only meaningful alongside an
    /// assertion that the update landed at all - see the note on the class.
    /// </summary>
    private sealed class AffinityWatcher
    {
        private readonly List<string> offDispatcher = [];
        public int Mutations { get; private set; }

        public AffinityWatcher(INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += (_, e) =>
            {
                Mutations++;
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    offDispatcher.Add($"{e.Action} on thread {Environment.CurrentManagedThreadId}");
                }
            };
        }

        public void AssertAllOnDispatcher(int expectedAtLeast = 1)
        {
            Assert.IsTrue(Mutations >= expectedAtLeast,
                $"Expected the collection to change at least {expectedAtLeast} time(s), saw {Mutations}.");
            Assert.AreEqual(0, offDispatcher.Count,
                "Bound collection mutated off the dispatcher: " + string.Join("; ", offDispatcher));
        }
    }

    [TestMethod]
    public Task SessionUpdateFromABackgroundThread_BuildsTheGridOnTheDispatcher() => HeadlessTest.OnDispatcher(async () =>
    {
        var vm = CreateLive();
        var watcher = new AffinityWatcher(vm.Cars);
        var update = new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2"), Entry("3")],
        });

        await HeadlessTest.OffDispatcherThen(() => vm.Receive(update));

        Assert.AreEqual(3, vm.Cars.Count, "The update should have reached the grid.");
        watcher.AssertAllOnDispatcher();
    });

    [TestMethod]
    public Task CarPositionUpdateFromABackgroundThread_ReordersTheGridOnTheDispatcher() => HeadlessTest.OnDispatcher(async () =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2")],
        }));
        var watcher = new AffinityWatcher(vm.Cars);

        // Positions change the sort key, so this moves rows inside the bound collection.
        var patches = new[]
        {
            CarPositionMapper.ToPatch(new CarPosition { Number = "1", OverallPosition = 2 }),
            CarPositionMapper.ToPatch(new CarPosition { Number = "2", OverallPosition = 1 }),
        };
        await HeadlessTest.OffDispatcherThen(() => vm.Receive(new CarStatusNotification(patches)));

        CollectionAssert.AreEqual(new[] { "2", "1" }, vm.Cars.Select(c => c.Number).ToArray());
        watcher.AssertAllOnDispatcher();
    });

    /// <summary>
    /// The riskiest path of the three: a reset empties the cache outright, so the bound collection
    /// is cleared rather than appended to.
    /// </summary>
    /// <remarks>
    /// The only test here that starts network work: the reset handler also fires a status refresh
    /// at the unreachable localhost URL. That is three attempts with backoff on a pool thread, then
    /// it logs and gives up - it cannot fail or hang the run, but it explains the delay and the
    /// connection-refused noise in the log.
    /// </remarks>
    [TestMethod]
    public Task ResetFromABackgroundThread_ClearsTheGridOnTheDispatcher() => HeadlessTest.OnDispatcher(async () =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2")],
        }));
        Assert.AreEqual(2, vm.Cars.Count, "Sanity check.");
        var watcher = new AffinityWatcher(vm.Cars);

        await HeadlessTest.OffDispatcherThen(() => vm.Receive(new ResetNotification()));

        Assert.AreEqual(0, vm.Cars.Count, "A reset before the flag drops clears the field.");
        watcher.AssertAllOnDispatcher();
    });

    [TestMethod]
    public Task GroupedRowsAreAlsoBuiltOnTheDispatcher() => HeadlessTest.OnDispatcher(async () =>
    {
        var vm = CreateLive();
        var watcher = new AffinityWatcher(vm.GroupedCars);
        var update = new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2")],
        });

        await HeadlessTest.OffDispatcherThen(() => vm.Receive(update));

        Assert.AreEqual(1, vm.GroupedCars.Count, "Both cars are in one class.");
        watcher.AssertAllOnDispatcher();
    });

    [TestMethod]
    public Task StoredResultsIgnoreLiveNotifications() => HeadlessTest.OnDispatcher(async () =>
    {
        var vm = Track(TestViewModelFactory.CreateLiveTiming());
        Assert.IsFalse(vm.IsRealTime, "Sanity check - the factory builds a stored-results view model.");
        var update = new SessionStatusNotification(new SessionStatePatch { EventEntries = [Entry("1")] });

        await HeadlessTest.OffDispatcherThen(() => vm.Receive(update));

        Assert.AreEqual(0, vm.Cars.Count, "A replayed session must not be overwritten by live traffic.");
    });

    /// <summary>
    /// Typing in the search box adds and removes rows from the bound collections, and it does so
    /// from an Rx timer callback 400ms later - a different thread again from the hub notifications,
    /// with marshalling just as load-bearing.
    /// </summary>
    [TestMethod]
    public Task SearchFilter_AppliedFromTheDebounceTimer_FiltersOnTheDispatcher() => HeadlessTest.OnDispatcher(async () =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("22"), Entry("333")],
        }));
        var watcher = new AffinityWatcher(vm.Cars);

        vm.SearchText = "22";

        // The debounce is 400ms; give it room, then drain what it posted.
        await Task.Delay(TimeSpan.FromMilliseconds(900));
        Dispatcher.UIThread.RunJobs();

        CollectionAssert.AreEqual(new[] { "22" }, vm.Cars.Select(c => c.Number).ToArray(),
            "Only the matching car should be left in the grid.");
        watcher.AssertAllOnDispatcher();
    });

    [TestMethod]
    public Task SearchFilter_Cleared_RestoresTheField() => HeadlessTest.OnDispatcher(async () =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("22")],
        }));
        vm.SearchText = "22";
        await Task.Delay(TimeSpan.FromMilliseconds(900));
        Dispatcher.UIThread.RunJobs();
        Assert.AreEqual(1, vm.Cars.Count, "Sanity check - the filter applied.");

        vm.SearchText = string.Empty;
        await Task.Delay(TimeSpan.FromMilliseconds(900));
        Dispatcher.UIThread.RunJobs();

        Assert.AreEqual(2, vm.Cars.Count);
    });

    /// <summary>
    /// The race clock drives every lap progress bar, and it does so through a background-priority
    /// post rather than inline. Nothing outside a real dispatcher exercises that hop.
    /// </summary>
    [TestMethod]
    public Task RaceTimeUpdate_ReachesTheLapProgressBars() => HeadlessTest.OnDispatcher(() =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [Entry("1")],
            CarPositions =
            [
                new CarPosition
                {
                    Number = "1",
                    LastLapCompleted = 4,
                    TotalTime = "00:10:00.000",
                    ProjectedLapTimeMs = 120_000,
                },
            ],
        }));
        var car = vm.Cars.Single();
        Assert.AreEqual(0, car.LapProgressFraction, 1e-9, "Sanity check - no race time yet.");

        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            RunningRaceTime = "00:11:00.000",
        }));
        Dispatcher.UIThread.RunJobs();

        Assert.AreEqual(0.5, car.LapProgressFraction, 1e-9,
            "A minute into a two minute projected lap puts the bar half way.");
    });

    /// <summary>
    /// The measured track position arrives with the car patches rather than with the race clock, so
    /// the bars have to be redrawn on that path too instead of waiting for the next clock tick.
    /// </summary>
    [TestMethod]
    public Task MeasuredTrackPosition_ReachesTheLapProgressBarsWithoutAClockTick() => HeadlessTest.OnDispatcher(() =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(new SessionStatusNotification(new SessionStatePatch
        {
            EventEntries = [Entry("1")],
            RunningRaceTime = "00:11:00.000",
            CarPositions =
            [
                new CarPosition
                {
                    Number = "1",
                    LastLapCompleted = 4,
                    TotalTime = "00:10:00.000",
                    ProjectedLapTimeMs = 120_000,
                },
            ],
        }));
        Dispatcher.UIThread.RunJobs();
        var car = vm.Cars.Single();
        Assert.AreEqual(0.5, car.LapProgressFraction, 1e-9, "Sanity check - the projection is driving it.");

        vm.Receive(new CarStatusNotification([new CarPositionPatch { Number = "1", LapPositionPercent = 25 }]));
        Dispatcher.UIThread.RunJobs();

        Assert.AreEqual(0.25, car.LapProgressFraction, 1e-9);
        Assert.IsTrue(car.IsLapProgressMeasured);
    });
}
