using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
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
/// Covers getting into a private event, which turns on the very first request the app makes for it.
/// </summary>
/// <remarks>
/// LoadEvent is gated by the access code on the server, so opening a private event with no code
/// stored is denied before <c>MainViewModel</c> has an event to prompt about, and the events list
/// has already been hidden by then. When the denial finds nothing to match, the viewer is left on a
/// blank screen with no way forward, which is what these tests exist to prevent.
///
/// Headless because the prompt is raised through <c>Dispatcher.PostSafe</c>: with no Avalonia
/// platform initialized the post never runs, and every one of these would pass without the fix.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class MainViewModelAccessCodeTests
{
    private const int PrivateEventId = 279;

    private readonly List<MainViewModel> created = [];

    /// <summary>
    /// Navigation runs through the shared <c>WeakReferenceMessenger.Default</c> and nothing
    /// unregisters a view model, so a leftover one would answer the next test's router events.
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

    /// <summary>
    /// Stands in for the server's treatment of a private event: everything is denied until a code is
    /// stored, and the denial carries the same notification the real client sends on a 401.
    /// </summary>
    private sealed class PrivateEventClient : EventClient
    {
        private readonly EventAccessCodeStore store;

        public PrivateEventClient(IConfiguration configuration, ILoggerFactory loggerFactory, EventAccessCodeStore store)
            : base(configuration, loggerFactory, store)
        {
            this.store = store;
        }

        public int LoadEventCalls { get; private set; }

        public Event Event { get; init; } = new() { EventId = PrivateEventId, EventName = "Test Event", IsPrivate = true };

        /// <summary>
        /// Answers null rather than the event, as the real client does for any unsuccessful response
        /// that carries no transport exception - a 404 or a 500 on the load.
        /// </summary>
        public bool LoadFails { get; set; }

        /// <summary>The only code this event accepts; anything else is denied, as a wrong one is.</summary>
        public const string CorrectCode = "1234";

        private bool HasCode(int eventId) => store.Get(eventId) == CorrectCode;

        private static void Deny(int eventId)
        {
            WeakReferenceMessenger.Default.Send(new EventAccessDeniedNotification(eventId));
            throw new EventAccessDeniedException(eventId);
        }

        public override Task<Event?> LoadEventAsync(int eventId)
        {
            LoadEventCalls++;
            if (LoadFails)
            {
                return Task.FromResult<Event?>(null);
            }
            if (!HasCode(eventId))
            {
                Deny(eventId);
            }
            return Task.FromResult<Event?>(Event);
        }

        /// <summary>The endpoint the prompt probes to check the code it was given.</summary>
        public override Task<SessionState?> LoadEventStatusAsync(int eventId)
        {
            if (!HasCode(eventId))
            {
                Deny(eventId);
            }
            return Task.FromResult<SessionState?>(null);
        }
    }

    private (MainViewModel Vm, PrivateEventClient Client, EventAccessCodeStore Store) Create()
    {
        var store = new EventAccessCodeStore(new MockPreferencesService());
        var client = new PrivateEventClient(TestViewModelFactory.CreateConfiguration(), new DebugLoggerFactory(), store);
        var vm = TestViewModelFactory.CreateMain(client, store);
        created.Add(vm);
        return (vm, client, store);
    }

    private static EventListSummary Summary(int eventId = PrivateEventId) => new()
    {
        Id = eventId,
        EventName = "Test Event",
        OrganizationName = "Test Org",
        IsPrivate = true,
    };

    /// <summary>
    /// Opens the event and runs the prompt that the denial posts back to the dispatcher.
    /// </summary>
    private static void NavigateToEvent(MainViewModel vm, object data)
    {
        vm.Receive(new ValueChangedMessage<RouterEvent>(new RouterEvent { Path = "EventStatus", Data = data }));
        Dispatcher.UIThread.RunJobs();
    }

    [TestMethod]
    public Task FirstVisitToAPrivateEvent_RaisesThePrompt() => HeadlessTest.OnDispatcher(() =>
    {
        var (vm, _, _) = Create();

        NavigateToEvent(vm, Summary());

        Assert.IsTrue(vm.IsAccessCodePromptVisible,
            "The load was denied before currentEvent could be set, and the events list is already hidden - " +
            "without a prompt here the viewer is stranded on a blank screen.");
        Assert.IsNotNull(vm.AccessCodePromptViewModel);
    });

    /// <summary>
    /// The prompt is titled from the events list entry, because the event itself never loaded.
    /// </summary>
    [TestMethod]
    public Task ThePrompt_IsTitledFromTheEventsListEntry() => HeadlessTest.OnDispatcher(() =>
    {
        var (vm, _, _) = Create();

        NavigateToEvent(vm, Summary());

        Assert.AreEqual("Test Event", vm.AccessCodePromptViewModel!.EventName);
        Assert.AreEqual("Test Org", vm.AccessCodePromptViewModel.OrganizationName);
    });

    /// <summary>
    /// Navigating by bare id - no events list entry to draw a name from - still has to prompt.
    /// </summary>
    [TestMethod]
    public Task NavigatingByEventId_StillRaisesThePrompt() => HeadlessTest.OnDispatcher(() =>
    {
        var (vm, _, _) = Create();

        NavigateToEvent(vm, PrivateEventId);

        Assert.IsTrue(vm.IsAccessCodePromptVisible);
        Assert.AreEqual("Private Event", vm.AccessCodePromptViewModel!.EventName,
            "With no name to show, the prompt falls back to its placeholder.");
    });

    [TestMethod]
    public Task AcceptedCode_ReplaysTheNavigationAndEntersTheEvent() => HeadlessTest.OnDispatcher(async () =>
    {
        var (vm, client, store) = Create();
        NavigateToEvent(vm, Summary());

        vm.AccessCodePromptViewModel!.Code = PrivateEventClient.CorrectCode;
        await vm.AccessCodePromptViewModel.ContinueCommand.ExecuteAsync(null);

        Assert.AreEqual(PrivateEventClient.CorrectCode, store.Get(PrivateEventId));
        Assert.IsFalse(vm.IsAccessCodePromptVisible, "The prompt should be gone once the code is accepted.");
        Assert.AreEqual(2, client.LoadEventCalls,
            "The route has to be replayed so the load runs again with the code attached - the first " +
            "attempt was denied and returned no event to show.");
        Assert.IsTrue(vm.IsTimingTabStripVisible, "The viewer should now be inside the event.");
    });

    [TestMethod]
    public Task RejectedCode_KeepsThePromptUpAndDiscardsTheCode() => HeadlessTest.OnDispatcher(async () =>
    {
        var (vm, _, store) = Create();
        NavigateToEvent(vm, Summary());
        var prompt = vm.AccessCodePromptViewModel!;

        // The client denies every code, so this stands in for a wrong one.
        prompt.Code = "9999";
        await prompt.ContinueCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsTrue(vm.IsAccessCodePromptVisible, "A wrong code leaves the viewer on the prompt to try again.");
        Assert.AreSame(prompt, vm.AccessCodePromptViewModel,
            "The denial from the validation probe must not replace the prompt that raised it.");
        Assert.IsTrue(prompt.HasError);
        Assert.IsNull(store.Get(PrivateEventId), "A rejected code should not be left behind.");
    });

    [TestMethod]
    public Task CancelingThePrompt_ReturnsToTheEventsList() => HeadlessTest.OnDispatcher(() =>
    {
        var (vm, _, _) = Create();
        NavigateToEvent(vm, Summary());

        vm.AccessCodePromptViewModel!.CancelCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsFalse(vm.IsAccessCodePromptVisible);
        Assert.IsTrue(vm.IsEventsListVisible, "Backing out of the prompt has to go somewhere the viewer can use.");
    });

    /// <summary>
    /// A denial for the event already on screen still re-prompts, which is what a code that has been
    /// revoked since it was stored looks like. This path predates the fix; it goes through the
    /// overload that has a loaded event to prompt from.
    /// </summary>
    [TestMethod]
    public Task DenialForTheEventOnScreen_RePromptsAndDiscardsTheStoredCode() => HeadlessTest.OnDispatcher(() =>
    {
        var (vm, _, store) = Create();
        store.Set(PrivateEventId, PrivateEventClient.CorrectCode);
        NavigateToEvent(vm, Summary());
        Assert.IsTrue(vm.IsTimingTabStripVisible, "Sanity check - the stored code got the viewer in.");

        WeakReferenceMessenger.Default.Send(new EventAccessDeniedNotification(PrivateEventId));
        Dispatcher.UIThread.RunJobs();

        Assert.IsTrue(vm.IsAccessCodePromptVisible);
        Assert.AreEqual("Test Event", vm.AccessCodePromptViewModel!.EventName,
            "This prompt is built from the loaded event, not the events list entry.");
        Assert.IsNull(store.Get(PrivateEventId), "The code the server just rejected should not be kept.");
    });

    /// <summary>
    /// The event being navigated into is remembered so the denial can find it, and that memory has to
    /// be given up once the navigation is over - otherwise a later denial for that event prompts over
    /// whatever the viewer has moved on to, and then replays a route they had already left.
    /// </summary>
    [TestMethod]
    public Task DenialAfterTheNavigationEnded_IsIgnored() => HeadlessTest.OnDispatcher(() =>
    {
        var (vm, client, _) = Create();
        client.LoadFails = true;

        // The load answers null rather than denying, so nothing is coming for this event.
        NavigateToEvent(vm, Summary());
        Assert.IsFalse(vm.IsAccessCodePromptVisible, "Sanity check - a failed load is not a denial.");

        WeakReferenceMessenger.Default.Send(new EventAccessDeniedNotification(PrivateEventId));
        Dispatcher.UIThread.RunJobs();

        Assert.IsFalse(vm.IsAccessCodePromptVisible,
            "The navigation is over, so this denial belongs to something else and must not be answered here.");
    });

    [TestMethod]
    public Task DenialForAnotherEvent_IsIgnored() => HeadlessTest.OnDispatcher(() =>
    {
        var (vm, _, store) = Create();
        store.Set(PrivateEventId, PrivateEventClient.CorrectCode);
        NavigateToEvent(vm, Summary());
        Assert.IsTrue(vm.IsTimingTabStripVisible, "Sanity check - the stored code got the viewer in.");

        WeakReferenceMessenger.Default.Send(new EventAccessDeniedNotification(PrivateEventId + 1));
        Dispatcher.UIThread.RunJobs();

        Assert.IsFalse(vm.IsAccessCodePromptVisible,
            "A denial for an event the viewer is not looking at should not take over the screen.");
    });
}
