using MessagePack;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.ViewModels.Design;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Configuration;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers reloading the events list without disturbing what is on screen.
/// </summary>
/// <remarks>
/// Every reload used to empty the list and rebuild it - and the view hides the list entirely behind
/// "Loading..." while that runs, so coming back to the app or pressing refresh blinked the schedule
/// out and put it back, almost always identical. Rebuilding also drops each row's organization logo
/// until the icon pass restores it, so what looked like a redraw was the whole screen being made
/// again.
///
/// The question these ask is whether the view is left alone when the answer has not changed, and
/// still updated when it has.
/// </remarks>
[TestClass]
public sealed class EventsListRefreshTests
{
    /// <summary>An event client returning whatever a test sets, without a server.</summary>
    private sealed class StubEventClient(RestClientFactory factory, EventAccessCodeStore store)
        : EventClient(factory, new DebugLoggerFactory(), store)
    {
        public List<EventListSummary>? Events { get; set; } = [];
        public Exception? Throws { get; set; }
        public int Calls { get; private set; }

        /// <summary>When set, the fetch waits on this - the only way to catch two reloads overlapping.</summary>
        public TaskCompletionSource? Gate { get; set; }

        /// <summary>What the archive query returns, independent of the live one.</summary>
        public List<EventListSummary> Archive { get; set; } = [];

        public int ArchiveCalls { get; private set; }

        public override async Task<List<EventListSummary>> LoadRecentEventsAsync()
        {
            Calls++;
            if (Gate is not null)
            {
                await Gate.Task;
            }
            if (Throws is not null)
            {
                throw Throws;
            }
            return Events ?? [];
        }

        public override Task<List<EventListSummary>> LoadArchivedEventsAsync(int offset, int take)
        {
            ArchiveCalls++;
            return Task.FromResult(Archive.Skip(offset).Take(take).ToList());
        }
    }

    private static EventListSummary Event(int id, string name = "Race", bool live = false, string date = "2026-09-05") => new()
    {
        Id = id,
        OrganizationId = 10,
        OrganizationName = "ChampCar",
        EventName = name,
        EventDate = date,
        IsLive = live,
        TrackName = "Mid Ohio",
    };

    private static (EventsListViewModel Vm, StubEventClient Server) Create()
    {
        var configuration = TestViewModelFactory.CreateConfiguration();
        var httpClientFactory = new DesignHttpClientFactory();
        var loggerFactory = new DebugLoggerFactory();
        var restClientFactory = new RestClientFactory(configuration);
        var server = new StubEventClient(restClientFactory, new EventAccessCodeStore(new MockPreferencesService()));

        var vm = new EventsListViewModel(
            server,
            new OrganizationClient(configuration, httpClientFactory, restClientFactory),
            new OrganizationIconCacheService(new OrganizationClient(configuration, httpClientFactory, restClientFactory), loggerFactory),
            loggerFactory);

        return (vm, server);
    }

    private static async Task<(EventsListViewModel Vm, StubEventClient Server, EventViewModel[] Rows)> Loaded(
        params EventListSummary[] events)
    {
        var (vm, server) = Create();
        server.Events = [.. events];
        await vm.InitializeAsync();

        Assert.AreEqual(events.Length, vm.Events.Count, "Sanity check - the first load has to put the events up.");
        return (vm, server, [.. vm.Events]);
    }

    [TestMethod]
    public async Task AReloadThatFindsTheSameSchedule_LeavesTheViewAlone()
    {
        // The common case by far: a schedule changes far more slowly than people look at it.
        var (vm, server, rows) = await Loaded(Event(1, live: true), Event(2));

        server.Events = [Event(1, live: true), Event(2)];
        await vm.RefreshIfChangedAsync();

        CollectionAssert.AreEqual(rows, vm.Events.ToArray(),
            "Identical events must leave the very same rows in place - rebuilding them blinks the list and drops its logos.");
    }

    [TestMethod]
    public async Task AReloadIsStillMade()
    {
        // Leaving the view alone is not the same as not asking. A stale list nobody rechecks would
        // be a worse bug than the blink this removes.
        var (vm, server, _) = await Loaded(Event(1));
        var beforeReload = server.Calls;

        await vm.RefreshIfChangedAsync();

        Assert.IsTrue(server.Calls > beforeReload);
    }

    [TestMethod]
    public async Task AQuietReload_NeverRaisesIsLoading()
    {
        // IsLoading is what hides the list behind "Loading...", so raising it at any point during a
        // reload is the blink itself, however briefly it is set.
        var (vm, server, _) = await Loaded(Event(1));
        var everLoading = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsLoading) && vm.IsLoading)
            {
                everLoading = true;
            }
        };

        server.Events = [Event(1), Event(2)];
        await vm.RefreshIfChangedAsync();

        Assert.IsFalse(everLoading, "The list must never be hidden by a reload the user did not ask to watch.");
    }

    [TestMethod]
    public async Task AnEventBecomingLive_RebuildsTheView()
    {
        // The change that matters most on this screen, and the one that also reorders the list.
        var (vm, server, _) = await Loaded(Event(1, live: false), Event(2, live: false));

        server.Events = [Event(1, live: false), Event(2, live: true)];
        await vm.RefreshIfChangedAsync();

        Assert.IsTrue(vm.Events[0].IsLive, "A live event has to move to the top.");
        Assert.AreEqual(2, vm.Events[0].EventModel.Id);
    }

    [TestMethod]
    public async Task AnEventAppearingOrDisappearing_RebuildsTheView()
    {
        var (vm, server, _) = await Loaded(Event(1));

        server.Events = [Event(1), Event(2)];
        await vm.RefreshIfChangedAsync();
        Assert.AreEqual(2, vm.Events.Count);

        server.Events = [Event(2)];
        await vm.RefreshIfChangedAsync();
        Assert.AreEqual(1, vm.Events.Count);
    }

    /// <summary>
    /// The guard against the comparison falling behind the model.
    /// </summary>
    /// <remarks>
    /// Every settable field of the summary is changed in turn and the reload has to notice. A
    /// comparison written out by hand would pass this on the day it was written and quietly stop
    /// covering whatever was added to <see cref="EventListSummary"/> afterwards - and the symptom
    /// would not be a failure, it would be an event that stops updating on screen.
    /// </remarks>
    [TestMethod]
    public async Task EveryFieldOfAnEvent_IsCompared()
    {
        var scalars = typeof(EventListSummary).GetProperties()
            .Where(p => p.CanWrite && (p.PropertyType == typeof(int) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(string)))
            .ToList();

        Assert.IsTrue(scalars.Count >= 10, "Sanity check - the summary's fields should have been found by reflection.");

        foreach (var property in scalars)
        {
            var (vm, server, rows) = await Loaded(Event(1));

            var changed = Event(1);
            property.SetValue(changed, property.PropertyType switch
            {
                var t when t == typeof(int) => (int)property.GetValue(changed)! + 1,
                var t when t == typeof(bool) => !(bool)property.GetValue(changed)!,
                _ => (object)((string?)property.GetValue(changed) + "-changed"),
            });

            // EventDate has to stay parseable, since it is what the ordering reads.
            if (property.Name == nameof(EventListSummary.EventDate))
            {
                changed.EventDate = "2026-09-04";
            }

            server.Events = [changed];
            await vm.RefreshIfChangedAsync();

            Assert.AreNotSame(rows[0], vm.Events[0], $"A change to {property.Name} went unnoticed.");
        }
    }

    [TestMethod]
    public async Task LeavingAnEventDoesNotFetchTheListTwice()
    {
        // Going back from an event asks for this twice - the router as it navigates, the view as it
        // loads - and both want the same list.
        var (vm, server, _) = await Loaded(Event(1));
        var beforeReload = server.Calls;

        // Held open, because two reloads that each finish before the next begins never overlap and
        // would pass this whether or not anything guarded them.
        server.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fromTheRouter = vm.RefreshIfChangedAsync();
        var fromTheView = vm.RefreshIfChangedAsync();
        server.Gate.SetResult();
        await Task.WhenAll(fromTheRouter, fromTheView);

        Assert.AreEqual(beforeReload + 1, server.Calls, "One back button is one request for the list.");
    }

    [TestMethod]
    public async Task AChangeToTheScheduleAlone_RebuildsTheView()
    {
        // The schedule is not a scalar, so the reflection sweep below cannot reach it - and it is
        // the field whose content is rendered inside each row.
        var (vm, server, rows) = await Loaded(Event(1));

        var withSchedule = Event(1);
        withSchedule.Schedule = new EventSchedule { Name = "Saturday" };
        server.Events = [withSchedule];
        await vm.RefreshIfChangedAsync();

        Assert.AreNotSame(rows[0], vm.Events[0], "A change to the schedule went unnoticed.");
    }

    [TestMethod]
    public void TheDayTurningOver_CountsAsAChange()
    {
        // A row shows the sessions belonging to today, worked out once from the clock when the row
        // is built - see EventViewModel. The server sends the same schedule either side of midnight,
        // which is exactly when this comparison says there is nothing to do, so without the date in
        // here a phone left open overnight would show yesterday's sessions all through the next day
        // and no reload would ever repair it.
        List<EventListSummary> unchanged = [Event(1)];

        var today = EventsListViewModel.Encode(unchanged, new DateTime(2026, 9, 5));
        var tomorrow = EventsListViewModel.Encode(unchanged, new DateTime(2026, 9, 6));

        CollectionAssert.AreNotEqual(today, tomorrow);
        CollectionAssert.AreEqual(today, EventsListViewModel.Encode([Event(1)], new DateTime(2026, 9, 5)),
            "Sanity check - the same events on the same day still have to compare equal.");
    }

    [TestMethod]
    public async Task ALateAnswerFromTheOtherTab_IsNotPaintedOverThisOne()
    {
        // Toggling while a reload is in flight changes the question. The old answer would otherwise
        // be ordered by the new mode's rules, compared against the new mode's list, and displayed
        // over it - live events sitting under the Older Events heading.
        var (vm, server, _) = await Loaded(Event(1, name: "Live event", live: true));
        server.Archive = [Event(99, name: "Archived event")];

        server.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Events = [Event(1, name: "Live event", live: true), Event(2, name: "Another live event", live: true)];
        var stale = vm.RefreshIfChangedAsync();

        // The user gives up waiting and switches tabs while that is still out.
        vm.LiveAndUpcomingEventsShown = false;
        await vm.InitializeAsync();
        Assert.AreEqual(99, vm.Events[0].EventModel.Id, "Sanity check - the archive is showing.");

        server.Gate.SetResult();
        await stale;

        Assert.AreEqual(1, vm.Events.Count, "The live answer must not be painted over the archive.");
        Assert.AreEqual(99, vm.Events[0].EventModel.Id);
    }

    [TestMethod]
    public async Task AReloadThatBringsBackNothing_LeavesThePagingAlone()
    {
        // The Next button is driven by HasMorePages. A reload that comes back empty is not trusted
        // enough to replace the list, so it must not be trusted enough to take the button away
        // either - that would strand the user on a full page of results.
        var (vm, server) = Create();
        vm.LiveAndUpcomingEventsShown = false;
        server.Archive = [.. Enumerable.Range(1, 30).Select(i => Event(i))];
        await vm.InitializeAsync();
        Assert.IsTrue(vm.HasMorePages, "Sanity check - there is a second page.");

        server.Archive = [];
        await vm.RefreshIfChangedAsync();

        Assert.IsTrue(vm.HasMorePages, "An answer not good enough to display is not good enough to disable paging.");
        Assert.AreEqual(25, vm.Events.Count, "And the page of results has to still be there.");
    }

    [TestMethod]
    public async Task ARefreshPressedDuringAReload_IsStillServed()
    {
        // The refresh button draws no spinner, so dropping its press silently means it did nothing
        // at all. The two reloads that fire together on the way out of an event are different -
        // they want the same list, so one of them is genuinely redundant.
        var (vm, server, _) = await Loaded(Event(1));
        var beforeReload = server.Calls;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Gate = gate;
        var running = vm.RefreshIfChangedAsync();
        var pressed = vm.RefreshIfChangedAsync(userRequested: true);

        // Cleared before releasing, so the rerun the press earns does not wait on a spent gate.
        server.Gate = null;
        gate.SetResult();
        await Task.WhenAll(running, pressed);

        Assert.AreEqual(beforeReload + 2, server.Calls, "The press has to be served once the reload holding the guard is done.");
    }

    [TestMethod]
    public async Task AReloadThatCannotReachTheServer_LeavesTheListUp()
    {
        // There is a good list on screen. Replacing it with an error because a check the user did
        // not watch failed would take away something still worth reading.
        var (vm, server, rows) = await Loaded(Event(1), Event(2));
        server.Throws = new HttpRequestException("Connection failure");

        await vm.RefreshIfChangedAsync();

        CollectionAssert.AreEqual(rows, vm.Events.ToArray());
        Assert.IsFalse(vm.HasMessage, "A failed background check must not put an error over a working list.");
    }

    [TestMethod]
    public async Task AReloadThatComesBackEmpty_LeavesTheListUp()
    {
        // An empty schedule cannot be right while a populated one is on screen, and "No events
        // found" over a list the user was just reading is worse than saying nothing.
        var (vm, server, rows) = await Loaded(Event(1), Event(2));
        server.Events = [];

        await vm.RefreshIfChangedAsync();

        CollectionAssert.AreEqual(rows, vm.Events.ToArray());
        Assert.IsFalse(vm.HasMessage);
    }

    [TestMethod]
    public async Task WithNothingOnScreen_AReloadIsAnOrdinaryLoad()
    {
        // The first load, and any load after a failure: there is nothing to compare against and
        // nothing to protect, so this case wants the spinner and the message.
        var (vm, server) = Create();
        server.Events = [Event(1)];

        await vm.RefreshIfChangedAsync();

        Assert.AreEqual(1, vm.Events.Count);
    }

    [TestMethod]
    public async Task AfterAFailedFirstLoad_AReloadStillRecovers()
    {
        var (vm, server) = Create();
        server.Throws = new HttpRequestException("Connection failure");
        await vm.InitializeAsync();
        Assert.AreEqual(0, vm.Events.Count, "Sanity check - the first load failed.");

        server.Throws = null;
        server.Events = [Event(1)];
        await vm.RefreshIfChangedAsync();

        Assert.AreEqual(1, vm.Events.Count, "A list that never loaded has to be able to arrive later.");
        Assert.IsFalse(vm.HasMessage, "The error from the failed load has to be cleared once one succeeds.");
    }

    [TestMethod]
    public void TheComparisonReadsTheWholeSummary()
    {
        // Documents what the encoding covers: the nested schedule too, not just the scalar fields
        // the rows bind directly.
        var withSchedule = Event(1);
        withSchedule.Schedule = new RedMist.TimingCommon.Models.Configuration.EventSchedule { Name = "Saturday" };

        var plain = MessagePackSerializer.Serialize(new List<EventListSummary> { Event(1) });
        var scheduled = MessagePackSerializer.Serialize(new List<EventListSummary> { withSchedule });

        CollectionAssert.AreNotEqual(plain, scheduled);
    }
}
