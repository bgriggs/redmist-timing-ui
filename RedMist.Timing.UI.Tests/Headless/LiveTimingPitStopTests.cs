using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.ViewModels.Design;
using RedMist.Timing.UI.Views;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers the pit phase across the grid: a full refresh taking down a stale badge, the one clock
/// that counts up the time stopped for every row, and the badge and details line in the real view.
/// </summary>
/// <remarks>
/// Headless because the refresh and the clock both marshal onto the dispatcher, and the view's badge
/// reaches back from a template whose data is the pit state to the row it sits in - a binding only a
/// realized view can show resolving.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LiveTimingPitStopTests
{
    private const string CarNumber = "5";

    private static readonly DateTime StopStart = new(2026, 10, 17, 13, 57, 1, DateTimeKind.Utc);

    /// <summary>An event client that answers each status call with the next state it was given.</summary>
    private sealed class ScriptedEventClient(RestClientFactory factory, EventAccessCodeStore store, params SessionState[] script)
        : EventClient(factory, new DebugLoggerFactory(), store)
    {
        private readonly Queue<SessionState> states = new(script);

        public override Task<SessionState?> LoadEventStatusAsync(int eventId) => Task.FromResult<SessionState?>(states.Dequeue());
    }

    private readonly List<LiveTimingViewModel> created = [];

    /// <summary>
    /// Runs a test body on the dispatcher and releases its view models there before returning,
    /// whether or not it passed.
    /// </summary>
    /// <remarks>
    /// A live view model with a car in its box holds a running clock, which posts to the dispatcher
    /// every second. Left running, it would post into whichever headless test came next. Disposed
    /// here rather than in a cleanup method because disposing tears down rows, which belongs on the
    /// dispatcher, and the session has let go of it by the time cleanup runs.
    /// </remarks>
    private Task OnDispatcher(Action body) => HeadlessTest.OnDispatcher(() =>
    {
        try
        {
            body();
        }
        finally
        {
            ReleaseTheViewModels();
        }
    });

    /// <inheritdoc cref="OnDispatcher(Action)"/>
    private Task OnDispatcher(Func<Task> body) => HeadlessTest.OnDispatcher(async () =>
    {
        try
        {
            await body();
        }
        finally
        {
            ReleaseTheViewModels();
        }
    });

    private void ReleaseTheViewModels()
    {
        // Live view models act on messenger traffic as well; nothing unregisters them on its own.
        foreach (var vm in created)
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
            vm.Dispose();
        }
        created.Clear();
    }

    private LiveTimingViewModel Track(LiveTimingViewModel vm)
    {
        created.Add(vm);
        return vm;
    }

    private LiveTimingViewModel CreateLive()
    {
        var vm = Track(TestViewModelFactory.CreateLiveTiming());
        vm.IsRealTime = true;
        return vm;
    }

    private static EventEntry Entry(string number) => new() { Number = number, Name = "Car " + number, Team = "T", Class = "GP1" };

    private static SessionStatusNotification StateWith(params CarPosition[] cars) => new(new SessionStatePatch
    {
        EventEntries = [.. cars.Select(c => Entry(c.Number!))],
        CarPositions = [.. cars],
    });

    private static CarPosition InBox(string number, DateTime start) =>
        new() { Number = number, PitPhase = PitPhase.InPitBox, PitStopStartTime = start, IsInPit = true };

    #region Full refresh

    /// <summary>
    /// End to end through the refresh. A refresh delivers each car twice - whole, inside the session
    /// patch, and again as a patch built against an empty car - and neither may leave the badge up.
    /// The second on its own is pinned in <see cref="CarViewModelPitPhaseTests"/>: the first already
    /// clears the badge here, so this would pass without that fix.
    /// </summary>
    [TestMethod]
    public Task FullRefresh_CarOutOfThePit_ClearsAStalePhaseBadge() => OnDispatcher(async () =>
    {
        var configuration = TestViewModelFactory.CreateConfiguration();
        var store = new EventAccessCodeStore(new MockPreferencesService());
        var server = new ScriptedEventClient(new RestClientFactory(configuration), store,
            new SessionState { EventEntries = [Entry(CarNumber)], CarPositions = [InBox(CarNumber, DateTime.UtcNow)] },
            // Left the pit while the hub was down, so no delta ever said so. Every pit field is now
            // its default, which is what a patch built against an empty car drops.
            new SessionState { EventEntries = [Entry(CarNumber)], CarPositions = [new CarPosition { Number = CarNumber, LastLapCompleted = 13, OverallPosition = 2 }] });
        var vm = Track(TestViewModelFactory.CreateLiveTiming(hubClient: null, serverClient: server));
        vm.IsRealTime = true;

        await vm.RefreshStatusAsync();
        Dispatcher.UIThread.RunJobs();
        var car = vm.Cars.Single();
        Assert.AreEqual(PitStates.InPitBox, car.PitState, "Sanity check - the first state put the car in its box.");

        await vm.RefreshStatusAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.AreEqual(PitStates.None, car.PitState);
        Assert.AreEqual(13, car.LastLap, "The rest of the state still applies.");
        Assert.AreEqual(2, car.OverallPosition);
        Assert.IsFalse(vm.IsPitStopClockRunning, "No car is in a box any more.");
    });

    #endregion

    #region The clock

    [TestMethod]
    public Task CarInItsBox_StartsTheClock_AndItCountsUp() => OnDispatcher(async () =>
    {
        var vm = CreateLive();
        Assert.IsFalse(vm.IsPitStopClockRunning, "Sanity check - nothing to count yet.");

        vm.ApplySessionUpdate(StateWith(InBox(CarNumber, DateTime.UtcNow.AddSeconds(-5))));
        var car = vm.Cars.Single();

        Assert.IsTrue(vm.IsPitStopClockRunning);
        var first = car.PitBoxText;
        StringAssert.Matches(first, new System.Text.RegularExpressions.Regex(@"^PIT 0:0[56]$"),
            "The time stopped is shown as soon as the car is in its box, not a second later.");

        await Task.Delay(TimeSpan.FromMilliseconds(2200));
        Dispatcher.UIThread.RunJobs();

        Assert.AreNotEqual(first, car.PitBoxText, "The clock should have ticked the badge on.");
        StringAssert.Matches(car.PitBoxText, new System.Text.RegularExpressions.Regex(@"^PIT 0:0[6-9]$"));
    });

    [TestMethod]
    public Task OneClock_CountsEveryCarInABox() => OnDispatcher(async () =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(StateWith(
            InBox("1", DateTime.UtcNow.AddSeconds(-5)),
            InBox("2", DateTime.UtcNow.AddSeconds(-125)),
            new CarPosition { Number = "3" }));
        var cars = vm.Cars.ToDictionary(c => c.Number);
        var before = (cars["1"].PitBoxText, cars["2"].PitBoxText);

        await Task.Delay(TimeSpan.FromMilliseconds(2200));
        Dispatcher.UIThread.RunJobs();

        Assert.AreNotEqual(before.Item1, cars["1"].PitBoxText);
        Assert.AreNotEqual(before.Item2, cars["2"].PitBoxText);
        StringAssert.StartsWith(cars["2"].PitBoxText, "PIT 2:0");
        Assert.AreEqual(PitStates.None, cars["3"].PitState);
    });

    [TestMethod]
    public Task LastCarLeavingItsBox_StopsTheClock() => OnDispatcher(() =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(StateWith(InBox(CarNumber, DateTime.UtcNow)));
        Assert.IsTrue(vm.IsPitStopClockRunning, "Sanity check.");

        vm.Receive(new CarStatusNotification([new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.ExitingPit }]));
        Dispatcher.UIThread.RunJobs();

        Assert.IsFalse(vm.IsPitStopClockRunning);
        Assert.AreEqual(PitStates.ExitingPit, vm.Cars.Single().PitState);
    });

    /// <summary>
    /// A car whose stop has no start time shows a plain badge, and there is nothing to count.
    /// </summary>
    [TestMethod]
    public Task CarInItsBoxWithNoStartTime_RunsNoClock() => OnDispatcher(() =>
    {
        var vm = CreateLive();

        vm.ApplySessionUpdate(StateWith(new CarPosition { Number = CarNumber, PitPhase = PitPhase.InPitBox, IsInPit = true }));

        Assert.AreEqual(PitStates.InPitBox, vm.Cars.Single().PitState);
        Assert.AreEqual("PIT", vm.Cars.Single().PitBoxText);
        Assert.IsFalse(vm.IsPitStopClockRunning);
    });

    [TestMethod]
    public Task StoredResults_RunNoClock() => OnDispatcher(() =>
    {
        var vm = Track(TestViewModelFactory.CreateLiveTiming());
        Assert.IsFalse(vm.IsRealTime, "Sanity check - the factory builds a stored-results view model.");

        // A car that finished the session in its garage. The time since is not a stop length.
        vm.ApplySessionUpdate(StateWith(InBox(CarNumber, DateTime.UtcNow.AddHours(-3))));

        Assert.AreEqual(PitStates.InPitBox, vm.Cars.Single().PitState);
        Assert.AreEqual("PIT", vm.Cars.Single().PitBoxText);
        Assert.IsFalse(vm.IsPitStopClockRunning);
    });

    [TestMethod]
    public Task LeavingTheGrid_StopsTheClock() => OnDispatcher(() =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(StateWith(InBox(CarNumber, DateTime.UtcNow)));
        Assert.IsTrue(vm.IsPitStopClockRunning, "Sanity check.");

        vm.Dispose();

        Assert.IsFalse(vm.IsPitStopClockRunning);
    });

    /// <summary>
    /// A hub delta, or a refresh still in flight, can land after the event has been left. Starting
    /// the clock then would leave it ticking on a grid nobody is watching, with nothing left to stop
    /// it - the leave that would have has already run.
    /// </summary>
    [TestMethod]
    public Task LateUpdateAfterLeavingTheEvent_DoesNotStartTheClock() => OnDispatcher(async () =>
    {
        var vm = CreateLive();
        vm.ApplySessionUpdate(StateWith(InBox(CarNumber, DateTime.UtcNow)));
        Assert.IsTrue(vm.IsPitStopClockRunning, "Sanity check.");

        await vm.UnsubscribeLiveAsync(vm.CurrentMark, eventId: 1);
        Assert.IsFalse(vm.IsPitStopClockRunning, "Leaving the event stops the clock.");

        vm.Receive(new CarStatusNotification([new CarPositionPatch { Number = CarNumber, LastLapCompleted = 4 }]));
        Dispatcher.UIThread.RunJobs();

        Assert.AreEqual(4, vm.Cars.Single().LastLap, "Sanity check - the late update still reached the row.");
        Assert.IsFalse(vm.IsPitStopClockRunning, "A late update must not start the clock again.");

        // Opening the event again is what lets it run.
        vm.BeginOpening(1);
        vm.Receive(new CarStatusNotification([new CarPositionPatch { Number = CarNumber, LastLapCompleted = 5 }]));
        Dispatcher.UIThread.RunJobs();

        Assert.IsTrue(vm.IsPitStopClockRunning);
    });

    #endregion

    #region The view

    private static (LiveTimingView View, Window Window) Show(LiveTimingViewModel vm)
    {
        var view = new LiveTimingView { DataContext = vm };
        var window = new Window { Width = 400, Height = 800, Content = view };
        window.Show();
        Layout(view);
        return (view, window);
    }

    private static void Layout(Layoutable view)
    {
        Dispatcher.UIThread.RunJobs();
        view.Measure(new Size(400, 800));
        view.Arrange(new Rect(0, 0, 400, 800));
        Dispatcher.UIThread.RunJobs();
    }

    private static string[] PitBadges(LiveTimingView view) =>
        [.. view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("pit")).Select(t => t.Text ?? string.Empty)];

    /// <summary>
    /// The badge template's data is the pit state, so its text is reached through the row. If that
    /// binding resolved against the wrong ancestor the badge would render empty, and nothing else
    /// would say so.
    /// </summary>
    [TestMethod]
    public Task InPitBoxBadge_ShowsTheTimeStopped() => OnDispatcher(() =>
    {
        HeadlessAppResources.Load();
        var vm = Track(TestViewModelFactory.CreateLiveTiming());
        vm.ApplySessionUpdate(StateWith(InBox(CarNumber, StopStart)));
        var (view, window) = Show(vm);

        vm.Cars.Single().UpdatePitStopClock(StopStart.AddSeconds(42));
        Layout(view);

        CollectionAssert.AreEqual(new[] { "PIT 0:42" }, PitBadges(view));
        window.Close();
    });

    [TestMethod]
    [DataRow(PitPhase.EnteredPit, "ENTERED PIT")]
    [DataRow(PitPhase.ExitingPit, "EXITING PIT")]
    [DataRow(PitPhase.ExitedPit, "EXITED PIT")]
    [DataRow(PitPhase.InPit, "PIT")]
    public Task PhaseBadge_ShowsItsLabel(PitPhase phase, string label) => OnDispatcher(() =>
    {
        HeadlessAppResources.Load();
        var vm = Track(TestViewModelFactory.CreateLiveTiming());
        vm.ApplySessionUpdate(StateWith(new CarPosition { Number = CarNumber, PitPhase = phase }));
        var (view, window) = Show(vm);

        CollectionAssert.AreEqual(new[] { label }, PitBadges(view));
        window.Close();
    });

    [TestMethod]
    public Task CarDetails_ShowTheLastPitStop() => OnDispatcher(() =>
    {
        HeadlessAppResources.Load();
        var vm = Track(TestViewModelFactory.CreateLiveTiming());
        vm.ApplySessionUpdate(StateWith(new CarPosition { Number = CarNumber, LastPitStopDurationMs = 83_400 }));
        var (view, window) = Show(vm);

        // Expanding starts a real load against an unreachable localhost; see LapListVirtualizationTests.
        vm.Cars.Single().IsDetailsExpanded = true;
        Layout(view);

        var label = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Last pit stop:");
        var line = (StackPanel)label.GetVisualParent()!;
        Assert.IsTrue(label.IsEffectivelyVisible);
        Assert.AreEqual("1:23", ((TextBlock)line.Children[1]).Text);
        window.Close();
    });

    [TestMethod]
    public Task CarDetails_NoStopCompleted_HideTheLastPitStop() => OnDispatcher(() =>
    {
        HeadlessAppResources.Load();
        var vm = Track(TestViewModelFactory.CreateLiveTiming());
        vm.ApplySessionUpdate(StateWith(new CarPosition { Number = CarNumber }));
        var (view, window) = Show(vm);

        vm.Cars.Single().IsDetailsExpanded = true;
        Layout(view);

        var label = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Last pit stop:");
        Assert.IsFalse(label.IsEffectivelyVisible);
        window.Close();
    });

    #endregion
}
