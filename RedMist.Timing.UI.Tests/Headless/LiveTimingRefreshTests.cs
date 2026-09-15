using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.ViewModels.Design;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers the two behaviors that make standing the periodic refresh down safe.
/// </summary>
/// <remarks>
/// <see cref="LivePollingPolicyTests"/> pins when a refresh is owed. These pin what the view model
/// does about it: that concurrent refreshes cannot stack up, and that the screen resyncs when the
/// hub subscription is restored - which is the case the gate itself cannot see, because a freshly
/// reconnected hub looks perfectly healthy while the grid behind it is missing everything that
/// changed during the gap.
///
/// Headless because a refresh applies its result through the same recipients the hub feeds, and
/// those marshal onto the dispatcher.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LiveTimingRefreshTests
{
    /// <summary>An event client whose status call can be held open, so a refresh can be caught mid-flight.</summary>
    public sealed class GatedEventClient(RestClientFactory factory, EventAccessCodeStore store)
        : EventClient(factory, new DebugLoggerFactory(), store)
    {
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;

        public int Calls => Volatile.Read(ref calls);
        public Task Entered => entered.Task;

        /// <summary>How the server behaves once the gate opens.</summary>
        public FailureMode Fails { get; set; } = FailureMode.None;

        public enum FailureMode
        {
            None,

            /// <summary>Every attempt fails, which is what ExecuteWithRetryAsync turns into a null.</summary>
            GivesUp,

            /// <summary>The call itself throws out of the refresh.</summary>
            Throws,

            /// <summary>The server says the event has nothing live.</summary>
            NotLive,
        }

        public override async Task<SessionState?> LoadEventStatusAsync(int eventId)
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await gate.Task;

            return Fails switch
            {
                // Every attempt refused, which is what ExecuteWithRetryAsync turns into a null and
                // the refresh turns into its early return.
                FailureMode.GivesUp => throw new HttpRequestException("Request failed with status code TooManyRequests"),

                // Past the retry wrapper, so this reaches the refresh's own catch: applying a state
                // with no car positions throws where the patches are built.
                FailureMode.Throws => new SessionState { CarPositions = null! },

                // What the real client makes of a 404 - see EventClientNotLiveTests.
                FailureMode.NotLive => throw new EventNotLiveException(eventId),

                _ => new SessionState { CarPositions = [] },
            };
        }

        public void Release() => gate.TrySetResult();
    }

    /// <summary>A hub client that reports whatever state a test wants, without a connection.</summary>
    private sealed class FakeHubClient(IConfiguration configuration, EventAccessCodeStore store)
        : HubClient(new DebugLoggerFactory(), configuration, store)
    {
        public bool Connected { get; set; } = true;
        public DateTime? LastMessage { get; set; } = DateTime.UtcNow;

        public override bool IsConnected => Connected;
        public override DateTime? LastEventMessageUtc => LastMessage;
    }

    private readonly List<object> created = [];

    /// <summary>Everything the view model logs - MSTest builds a fresh instance of this class per test.</summary>
    private readonly RecordingLoggerFactory logs = new();

    [TestCleanup]
    public void UnregisterFromTheMessenger()
    {
        // These view models are live and act on messenger traffic; nothing unregisters them on
        // their own. See the note on LiveTimingDispatcherTests.
        foreach (var vm in created)
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
        created.Clear();
    }

    private (LiveTimingViewModel Vm, GatedEventClient Server, FakeHubClient Hub) CreateLive(int eventId = 1)
    {
        var configuration = TestViewModelFactory.CreateConfiguration();
        var store = new EventAccessCodeStore(new MockPreferencesService());
        var server = new GatedEventClient(new RestClientFactory(configuration), store);
        var hub = new FakeHubClient(configuration, store);

        var vm = TestViewModelFactory.CreateLiveTiming(hub, server, logs);
        vm.EventModel = new Event { EventId = eventId };
        vm.IsRealTime = true;
        created.Add(vm);
        return (vm, server, hub);
    }

    [TestMethod]
    public Task ARefreshAlreadyRunning_TurnsTheNextOneAway() => HeadlessTest.OnDispatcher(async () =>
    {
        var (vm, server, _) = CreateLive();

        // A refresh retries twice with a growing delay, so on a bad connection it outlives the
        // five-second tick that started it - and the ticks do not wait for each other. Stacking
        // them means the worse the network gets, the more requests the app makes of it.
        var first = vm.RefreshStatusAsync();
        await server.Entered;

        // Not awaited outright: without the guard the second call queues behind the first, which is
        // still held open, and the test would hang instead of reporting what is wrong.
        var second = vm.RefreshStatusAsync();
        var settled = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.AreSame(second, settled, "The second refresh must turn back immediately, not wait for the first.");
        Assert.AreEqual(1, server.Calls, "The second refresh must not have reached the server.");

        server.Release();
        await first;

        // A tick is dropped rather than remembered: the refresh it lost to answers the same
        // question, and another tick is along in five seconds regardless.
        Assert.AreEqual(1, server.Calls);
    });

    [TestMethod]
    public Task OnceARefreshFinishes_TheNextOneIsLetThrough() => HeadlessTest.OnDispatcher(async () =>
    {
        // The guard has to be released on every path out, including the failure paths, or the
        // screen stops refreshing for the rest of the session.
        var (vm, server, _) = CreateLive();

        var first = vm.RefreshStatusAsync();
        await server.Entered;
        server.Release();
        await first;

        await vm.RefreshStatusAsync();

        Assert.AreEqual(2, server.Calls);
    });

    [TestMethod]
    [DataRow(GatedEventClient.FailureMode.GivesUp, DisplayName = "The server refused every attempt")]
    [DataRow(GatedEventClient.FailureMode.Throws, DisplayName = "The refresh threw")]
    public Task AFailedRefresh_StillReleasesTheGuard(GatedEventClient.FailureMode failure) => HeadlessTest.OnDispatcher(async () =>
    {
        // The path that matters most, because it is the one that happens during the incident this
        // was written for: a 429 storm fails the refresh. A guard left held there stops the screen
        // refreshing for the rest of the session.
        var (vm, server, _) = CreateLive();
        server.Fails = failure;

        var first = vm.RefreshStatusAsync();
        await server.Entered;
        server.Release();
        await first;

        // Attempts, not refreshes: the retry wrapper makes up to three of these per refresh, so the
        // question is whether the count moved at all.
        var afterTheFailure = server.Calls;
        await vm.RefreshStatusAsync();

        Assert.IsTrue(server.Calls > afterTheFailure, "A failure must not wedge the guard shut.");
    });

    [TestMethod]
    public Task AFailedRefresh_LeavesTheScreenStillOwedAWholeState() => HeadlessTest.OnDispatcher(async () =>
    {
        // The failure that made this necessary: entering an event whose first load is refused
        // leaves an empty grid, and the hub only carries deltas for rows nothing has created. If the
        // failed attempt counted as a resync, the gate would sit the poll down for the whole refresh
        // floor and the user would watch an empty table with a perfectly healthy hub behind it.
        var (vm, server, hub) = CreateLive();
        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow;
        server.Fails = GatedEventClient.FailureMode.GivesUp;
        server.Release();

        await vm.RefreshStatusAsync();

        Assert.IsTrue(vm.ShouldRefreshNow(), "A refresh that failed cannot count as the screen being in sync.");
    });

    [TestMethod]
    public Task AResyncTurnedAwayByTheGuard_IsServedAfterwards() => HeadlessTest.OnDispatcher(async () =>
    {
        // An app resume, a session reset and a hub resubscribe each know the screen is stale and
        // have no second chance. Dropping one because a slow refresh happened to hold the guard
        // would trade the old stacking problem for a frozen grid.
        var (vm, server, _) = CreateLive(eventId: 7);

        var first = vm.RefreshStatusAsync();
        await server.Entered;

        vm.Receive(new HubResubscribedNotification(7));
        await Task.Delay(50);
        Assert.AreEqual(1, server.Calls, "Sanity check - the resync was turned away while the first ran.");

        server.Release();
        await first;

        await WaitForCallsAsync(server, 2);
        Assert.AreEqual(2, server.Calls, "The resync the guard turned away still has to happen.");
    });

    [TestMethod]
    public Task WhenTheHubResubscribes_TheScreenTakesAWholeStateAgain() => HeadlessTest.OnDispatcher(async () =>
    {
        // The case the gate cannot see. SubscribeToEventV2 only adds the connection to the event's
        // group - the server sends no state - so the delta stream resumes with a gap behind it
        // while the hub looks healthy and the poll stands down.
        var (vm, server, hub) = CreateLive(eventId: 7);
        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow;
        server.Release();

        vm.Receive(new HubResubscribedNotification(7));

        await WaitForCallsAsync(server, 1);
        Assert.AreEqual(1, server.Calls, "A reconnect leaves the grid stale, and nothing else asks for the repair.");
    });

    [TestMethod]
    public Task AResubscribeForAnotherEvent_IsIgnored() => HeadlessTest.OnDispatcher(async () =>
    {
        // The view model is a singleton reused for every event, so a notification can arrive while
        // EventModel has already moved on - or before it has caught up. Matching on the event id is
        // what stops a resync being spent on a screen that is no longer showing that event.
        var (vm, server, _) = CreateLive(eventId: 7);
        server.Release();

        vm.Receive(new HubResubscribedNotification(8));

        await Task.Delay(150);
        Assert.AreEqual(0, server.Calls);
    });

    [TestMethod]
    public Task AnAppResume_WhileAnEventIsFollowed_TakesAWholeState() => HeadlessTest.OnDispatcher(async () =>
    {
        // What a resume is for: nothing arrived while the app was in the background, so the grid is
        // stale however healthy the hub looks the moment it comes back.
        var (vm, server, _) = CreateLive(eventId: 7);
        server.Release();
        vm.StartPeriodicRefresh();
        try
        {
            vm.Receive(new AppResumeNotification());

            await WaitForCallsAsync(server, 1);
            Assert.AreEqual(1, server.Calls);
        }
        finally
        {
            await vm.UnsubscribeLiveAsync();
        }
    });

    [TestMethod]
    public Task AnAppResume_BeforeAnyEventIsOpened_AsksForNothing() => HeadlessTest.OnDispatcher(async () =>
    {
        // The app is activated on a cold start too, and this singleton exists before any event is
        // opened, holding an empty Event whose id is 0. The real client answers that with null and no
        // request, so what reached it was a refresh that logged a warning at every launch; this client
        // counts the call instead.
        var (vm, server, _) = CreateLive(eventId: 0);
        server.Release();

        vm.Receive(new AppResumeNotification());

        await Task.Delay(150);
        Assert.AreEqual(0, server.Calls);
    });

    [TestMethod]
    public Task AnAppResume_AfterTheEventIsLeft_AsksForNothing() => HeadlessTest.OnDispatcher(async () =>
    {
        // Leaving an event stops its refresh but keeps it as EventModel, so a resume on the home screen
        // fetched the state of the event just left, for a screen nobody was looking at.
        var (vm, server, _) = CreateLive(eventId: 7);
        server.Release();
        vm.StartPeriodicRefresh();
        await vm.UnsubscribeLiveAsync();

        vm.Receive(new AppResumeNotification());

        await Task.Delay(150);
        Assert.AreEqual(0, server.Calls);
    });

    [TestMethod]
    public Task ASessionReset_LeavesTheScreenOwedAWholeState() => HeadlessTest.OnDispatcher(async () =>
    {
        // The view model is a singleton reused for every event and every session, so a success from
        // the last one is still on the clock. A reset empties the grid; if its reload then fails,
        // the gate must not read that stale success, decide the screen is in sync, and leave an
        // empty table sitting in front of a hub that is delivering deltas for rows nothing created.
        var (vm, server, hub) = CreateLive();
        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow;
        server.Release();
        await vm.RefreshStatusAsync();
        Assert.IsFalse(vm.ShouldRefreshNow(), "Sanity check - a fresh success stands the poll down.");

        server.Fails = GatedEventClient.FailureMode.GivesUp;
        var beforeReset = server.Calls;
        vm.Receive(new ResetNotification());
        await WaitForCallsAsync(server, beforeReset + 1);

        hub.LastMessage = DateTime.UtcNow;
        Assert.IsTrue(vm.ShouldRefreshNow(), "An emptied grid whose reload failed is not in sync.");
    });

    // --- The gate, as the view model asks it -------------------------------------------------

    [TestMethod]
    public Task AHealthyHub_StandsTheViewModelsPollDown() => HeadlessTest.OnDispatcher(async () =>
    {
        // LivePollingPolicyTests pins the decision. This pins that the view model actually consults
        // it, and hands it the hub's state rather than something of its own - the reverts that would
        // otherwise pass are the worst ones available: never polling, or always polling.
        var (vm, server, hub) = CreateLive();
        server.Release();
        await vm.RefreshStatusAsync();

        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow;

        Assert.IsFalse(vm.ShouldRefreshNow());
    });

    [TestMethod]
    public Task ADisconnectedHub_MakesTheViewModelPoll() => HeadlessTest.OnDispatcher(async () =>
    {
        var (vm, server, hub) = CreateLive();
        server.Release();
        await vm.RefreshStatusAsync();

        hub.Connected = false;
        hub.LastMessage = DateTime.UtcNow;

        Assert.IsTrue(vm.ShouldRefreshNow());
    });

    [TestMethod]
    public Task ASilentHub_MakesTheViewModelPoll() => HeadlessTest.OnDispatcher(async () =>
    {
        var (vm, server, hub) = CreateLive();
        server.Release();
        await vm.RefreshStatusAsync();

        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow - LivePollingPolicy.HubSilenceBeforeRefreshing;

        Assert.IsTrue(vm.ShouldRefreshNow());
    });

    [TestMethod]
    public Task AHubThatHasNeverDelivered_MakesTheViewModelPoll() => HeadlessTest.OnDispatcher(async () =>
    {
        // Null has to read as "forever ago", not as "just now" - it is how every screen starts.
        var (vm, server, hub) = CreateLive();
        server.Release();
        await vm.RefreshStatusAsync();

        hub.Connected = true;
        hub.LastMessage = null;

        Assert.IsTrue(vm.ShouldRefreshNow());
    });

    // --- An event with nothing live -----------------------------------------------------------

    [TestMethod]
    public Task AnEventWithNothingLive_IsAskedOnceAndNotReported() => HeadlessTest.OnDispatcher(async () =>
    {
        // REDMIST-APP-X: a screen left open on a finished event polled every five seconds, tried each
        // poll three times, and filed an error at the end of every one - for most of a day.
        var (vm, server, _) = CreateLive();
        server.Fails = GatedEventClient.FailureMode.NotLive;
        server.Release();

        await vm.RefreshStatusAsync();

        Assert.AreEqual(1, server.Calls, "Nothing live is an answer, and asking again half a second later gets the same one.");
        var reported = logs.Entries.Where(e => e.Level >= LogLevel.Error).Select(e => e.Message).ToList();
        Assert.IsEmpty(reported, "An event with nothing running is not a fault to report: " + string.Join("; ", reported));
    });

    [TestMethod]
    public Task ANotLiveRunThatGoesOn_HoldsTheTickOff() => HeadlessTest.OnDispatcher(async () =>
    {
        // A finished event's hub is silent, and silence is what makes the gate poll - so without the
        // hold this screen would ask every five seconds for as long as it stayed open.
        var (vm, server, hub) = CreateLive();
        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow - TimeSpan.FromMinutes(5);
        vm.RecordNotLiveAnswer(DateTime.UtcNow - TimeSpan.FromMinutes(2));
        server.Fails = GatedEventClient.FailureMode.NotLive;
        server.Release();

        // The server says so again, which is what keeps the hold current.
        await vm.RefreshStatusAsync();

        Assert.IsFalse(vm.ShouldRefreshNow());
    });

    [TestMethod]
    public Task AnEventStartingUp_IsNotHeldOff() => HeadlessTest.OnDispatcher(async () =>
    {
        // The first answers of a run are an event whose processor is still starting, and the viewer
        // who opened it early has to get a whole state as soon as there is one.
        var (vm, server, hub) = CreateLive();
        hub.Connected = true;
        hub.LastMessage = null;
        server.Fails = GatedEventClient.FailureMode.NotLive;
        server.Release();

        await vm.RefreshStatusAsync();

        Assert.IsTrue(vm.ShouldRefreshNow());
    });

    [TestMethod]
    public Task PatchesArriving_LiftTheHold() => HeadlessTest.OnDispatcher(() =>
    {
        var (vm, _, hub) = CreateLive();
        hub.Connected = true;
        var now = DateTime.UtcNow;
        vm.RecordNotLiveAnswer(now - TimeSpan.FromMinutes(2));
        vm.RecordNotLiveAnswer(now - TimeSpan.FromSeconds(1));
        hub.LastMessage = now - TimeSpan.FromMinutes(5);
        Assert.IsFalse(vm.ShouldRefreshNow(), "Sanity check - held off while the hub is quiet.");

        // The event has come back: patches are arriving after the server last said otherwise.
        hub.LastMessage = now;

        Assert.IsTrue(vm.ShouldRefreshNow(), "A screen that has never had a whole state needs one as soon as the event is running.");
    });

    [TestMethod]
    public Task AWholeState_EndsTheRun() => HeadlessTest.OnDispatcher(async () =>
    {
        var (vm, server, hub) = CreateLive();
        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow - TimeSpan.FromMinutes(5);
        vm.RecordNotLiveAnswer(DateTime.UtcNow - TimeSpan.FromMinutes(2));
        vm.RecordNotLiveAnswer(DateTime.UtcNow);
        Assert.IsFalse(vm.ShouldRefreshNow(), "Sanity check - held off.");

        server.Release();
        await vm.RefreshStatusAsync();

        // A running event whose hub has gone quiet has to be polled. A hold left over from the run
        // would stand the poll down exactly when it is needed.
        Assert.IsTrue(vm.ShouldRefreshNow());
    });

    [TestMethod]
    public Task ASessionReset_StartsTheQuestionOver() => HeadlessTest.OnDispatcher(async () =>
    {
        // A reset comes from a running event, and the reload it triggers can still answer not live
        // while the processor settles. A run carried across from before would hold that screen off at once.
        var (vm, server, hub) = CreateLive();
        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow - TimeSpan.FromMinutes(5);
        vm.RecordNotLiveAnswer(DateTime.UtcNow - TimeSpan.FromMinutes(2));
        vm.RecordNotLiveAnswer(DateTime.UtcNow);
        server.Fails = GatedEventClient.FailureMode.NotLive;
        server.Release();

        var beforeReset = server.Calls;
        vm.Receive(new ResetNotification());
        await WaitForCallsAsync(server, beforeReset + 1);

        Assert.IsTrue(vm.ShouldRefreshNow());
    });

    [TestMethod]
    public Task ARunLeftOvernight_StartsAgain() => HeadlessTest.OnDispatcher(async () =>
    {
        // A multi-day event left open overnight. The first answer of the morning belongs to a new run,
        // with the same grace as a first open, and not to last night's - which would hold the screen
        // off before it had asked a second time.
        var (vm, server, hub) = CreateLive();
        hub.Connected = true;
        hub.LastMessage = DateTime.UtcNow - TimeSpan.FromHours(10);
        vm.RecordNotLiveAnswer(DateTime.UtcNow - TimeSpan.FromHours(10));
        vm.RecordNotLiveAnswer(DateTime.UtcNow - TimeSpan.FromHours(9));
        server.Fails = GatedEventClient.FailureMode.NotLive;
        server.Release();

        await vm.RefreshStatusAsync();

        Assert.IsTrue(vm.ShouldRefreshNow());
    });

    private static async Task WaitForCallsAsync(GatedEventClient server, int expected)
    {
        // The handler starts the refresh on a background task rather than blocking the messenger.
        for (var i = 0; i < 100 && server.Calls < expected; i++)
        {
            await Task.Delay(10);
        }
    }
}
