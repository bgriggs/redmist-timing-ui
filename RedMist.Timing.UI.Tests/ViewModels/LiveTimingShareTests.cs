using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers what the timing grid will and will not share.
/// </summary>
/// <remarks>
/// The private-event guard is the part of this feature that has to hold absolutely: a private event is
/// off the public list, and the only link that would work for a recipient is one carrying the access
/// code. So it is not enough that the buttons are hidden - the share path is called directly here, the
/// way a stray binding or a future caller would, and asserted to hand nothing over.
///
/// <para>
/// <c>[DoNotParallelize]</c> because these register for <see cref="ShareRequest"/> on the process-wide
/// default messenger, which is where the view model sends. Nothing else in the suite sends one, so the
/// only cross-talk to avoid is between these tests.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class LiveTimingShareTests
{
    /// <summary>Stands in for the head that would put a share in front of the viewer.</summary>
    /// <remarks>
    /// Internal rather than private because implementing two <c>IRecipient</c> interfaces makes the
    /// MVVM toolkit generate a registration extension for it, and a generated file cannot reach a
    /// private nested type.
    /// </remarks>
    internal sealed class ShareRecorder : IRecipient<ShareRequest>, IRecipient<ShareImageRequest>
    {
        public List<SharePayload> Links { get; } = [];
        public List<ShareImageRequest> Images { get; } = [];

        public void Receive(ShareRequest message)
        {
            // Guarded the same way MainView is. A second Reply throws, and a stray recipient left
            // registered by another test would otherwise take this one down with it.
            if (message.HasReceivedResponse)
            {
                return;
            }

            Links.Add(message.Payload);
            // Replied as Shared so the view model has nothing to report, which keeps these tests off
            // the status-banner path and its dispatcher.
            message.Reply(ShareOutcome.Shared);
        }

        public void Receive(ShareImageRequest message)
        {
            if (message.HasReceivedResponse)
            {
                return;
            }

            Images.Add(message);
            message.Reply(ShareOutcome.Shared);
        }
    }

    private ShareRecorder recorder = null!;

    [TestInitialize]
    public void Register()
    {
        recorder = new ShareRecorder();
        // Registered per message rather than through RegisterAll, which would go through reflection.
        WeakReferenceMessenger.Default.Register<ShareRequest>(recorder);
        WeakReferenceMessenger.Default.Register<ShareImageRequest>(recorder);
    }

    [TestCleanup]
    public void Unregister() => WeakReferenceMessenger.Default.UnregisterAll(recorder);

    private static LiveTimingViewModel CreateGrid(Event eventModel)
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.EventModel = eventModel;
        return vm;
    }

    private static Event PublicEvent() => new() { EventId = 42, EventName = "Spring Sprints" };

    private static CarViewModel Car(string number = "99")
    {
        var car = TestViewModelFactory.CreateCar();
        car.Number = number;
        car.ClassPosition = 3;
        car.Class = "GP1";
        return car;
    }

    #region The private-event guard

    [TestMethod]
    public void APrivateEvent_OffersNothing()
    {
        var vm = CreateGrid(new Event { EventId = 42, EventName = "Club Test Day", IsPrivate = true });

        Assert.IsFalse(vm.CanShare, "No share affordance may render for a private event.");
    }

    [TestMethod]
    public void AnEventWithItsNameHidden_OffersNothing()
    {
        var vm = CreateGrid(new Event { EventId = 42, HideName = true });

        Assert.IsFalse(vm.CanShare);
    }

    [TestMethod]
    public void NoEventOpenYet_OffersNothing()
    {
        // The grid is a singleton reused for every event; before one is opened its event is an empty
        // Event, which would otherwise build a link to /timing/0.
        var vm = CreateGrid(new Event());

        Assert.IsFalse(vm.CanShare);
    }

    [TestMethod]
    public async Task APrivateEvent_ShareEventLinkCalledDirectly_SendsNothing()
    {
        var vm = CreateGrid(new Event { EventId = 42, IsPrivate = true });

        await vm.ShareEventLink();

        Assert.AreEqual(0, recorder.Links.Count, "An access code must never reach a shareable URL.");
    }

    [TestMethod]
    public async Task APrivateEvent_ShareEventCardCalledDirectly_SendsNothing()
    {
        var vm = CreateGrid(new Event { EventId = 42, IsPrivate = true });

        await vm.ShareEventCard();

        Assert.AreEqual(0, recorder.Images.Count);
    }

    [TestMethod]
    public async Task APrivateEvent_ShareCarLinkCalledDirectly_SendsNothing()
    {
        var vm = CreateGrid(new Event { EventId = 42, IsPrivate = true });

        await vm.ShareCarLinkAsync(Car());

        Assert.AreEqual(0, recorder.Links.Count);
    }

    [TestMethod]
    public async Task APrivateEvent_ShareCarCardCalledDirectly_SendsNothing()
    {
        var vm = CreateGrid(new Event { EventId = 42, IsPrivate = true });

        await vm.ShareCarCardAsync(Car());

        Assert.AreEqual(0, recorder.Images.Count);
    }

    [TestMethod]
    public async Task AnEventWithItsNameHidden_ShareCalledDirectly_SendsNothing()
    {
        var vm = CreateGrid(new Event { EventId = 42, HideName = true });

        await vm.ShareEventLink();
        await vm.ShareCarLinkAsync(Car());

        Assert.AreEqual(0, recorder.Links.Count);
    }

    /// <summary>
    /// Whatever gates sharing has to be tied to the event on screen, not to the last load that
    /// finished. The web build had a window where a public event's model landed while a private event
    /// was showing and briefly rendered a share button for the private one.
    /// </summary>
    [TestMethod]
    public void TheGuard_FollowsTheEventOnScreen()
    {
        var vm = CreateGrid(PublicEvent());
        Assert.IsTrue(vm.CanShare);

        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.EventModel = new Event { EventId = 43, IsPrivate = true };

        Assert.IsFalse(vm.CanShare, "The guard reads the event being shown, not the one that was.");
        Assert.IsTrue(changed.Contains(nameof(LiveTimingViewModel.CanShare)),
            "The buttons have to be told, or they stay on screen for the private event.");
    }

    #endregion

    #region A public event

    [TestMethod]
    public void APublicEvent_OffersSharing()
    {
        Assert.IsTrue(CreateGrid(PublicEvent()).CanShare);
    }

    [TestMethod]
    public async Task APublicEvent_SharesAnEventLink()
    {
        var vm = CreateGrid(PublicEvent());
        vm.IsRealTime = true;

        await vm.ShareEventLink();

        var payload = recorder.Links.Single();
        Assert.AreEqual("https://redmist.racing/timing/42?src=app-share", payload.Url);
        Assert.AreEqual("Spring Sprints", payload.Title);
        Assert.AreEqual("Spring Sprints - follow live timing on Red Mist", payload.Text);
    }

    [TestMethod]
    public async Task APublicEvent_SharesACarLink()
    {
        var vm = CreateGrid(PublicEvent());
        vm.IsRealTime = true;

        await vm.ShareCarLinkAsync(Car());

        var payload = recorder.Links.Single();
        Assert.AreEqual("https://redmist.racing/timing/42?car=99&src=app-share", payload.Url);
        Assert.AreEqual("Car #99 - P3 in GP1", payload.Title);
    }

    /// <summary>
    /// Session 0 is a real session - the timing feed emits run number 0 - so a results link for it has
    /// to keep the id rather than falling back to the live view.
    /// </summary>
    [TestMethod]
    public async Task AStoredSession_SharesThatSession()
    {
        var vm = CreateGrid(PublicEvent());
        vm.IsRealTime = false;
        vm.PinnedSessionId = 0;

        await vm.ShareEventLink();

        var payload = recorder.Links.Single();
        Assert.AreEqual("https://redmist.racing/timing/42/0?src=app-share", payload.Url);
        Assert.AreEqual("Spring Sprints - results on Red Mist", payload.Text);
    }

    /// <summary>
    /// A build aimed at a test site has to keep its links on that site, which is what the web app
    /// gets for free by reading the address bar. Without this the whole suite would be asserting the
    /// hardcoded fallback, and a mistyped configuration key would pass.
    /// </summary>
    [TestMethod]
    public async Task AConfiguredSite_IsWhereLinksPoint()
    {
        var vm = TestViewModelFactory.CreateLiveTiming("https://test.redmist.racing");
        vm.EventModel = PublicEvent();
        vm.IsRealTime = true;

        await vm.ShareEventLink();

        Assert.AreEqual("https://test.redmist.racing/timing/42?src=app-share",
            recorder.Links.Single().Url);
    }

    [TestMethod]
    public async Task APublicEvent_SharesACard()
    {
        var vm = CreateGrid(PublicEvent());
        vm.IsRealTime = true;

        await vm.ShareEventCard();

        // Drawing needs a render target, which the plain unit-test host has no platform for, so this
        // pins what the share carries rather than what it looks like - the card itself is covered by
        // rendering one and looking at it. A failure to draw is reported, not thrown, so either way
        // this asserts the path ran to the end.
        if (recorder.Images.Count == 0)
        {
            Assert.AreEqual("Could not build that image", vm.ShareStatus,
                "A card that could not be drawn has to say so rather than fail silently.");
            return;
        }

        var request = recorder.Images.Single();
        Assert.AreEqual("red-mist-spring-sprints.png", request.FileName);
        Assert.AreEqual("https://redmist.racing/timing/42?src=app-share", request.Payload.Url);
        Assert.IsTrue(request.Image.Length > 0);
    }

    [TestMethod]
    public async Task ACarCard_IsNamedForTheCar()
    {
        var vm = CreateGrid(PublicEvent());
        vm.IsRealTime = true;

        await vm.ShareCarCardAsync(Car("99"));

        if (recorder.Images.Count == 0)
        {
            Assert.AreEqual("Could not build that image", vm.ShareStatus);
            return;
        }

        var request = recorder.Images.Single();
        Assert.AreEqual("red-mist-car-99-spring-sprints.png", request.FileName);
        Assert.AreEqual("https://redmist.racing/timing/42?car=99&src=app-share", request.Payload.Url);
    }

    /// <summary>
    /// A null car must not quietly turn into an event share - the viewer asked for one card and
    /// would get another.
    /// </summary>
    [TestMethod]
    public async Task ANullCar_SharesNothing()
    {
        var vm = CreateGrid(PublicEvent());

        await vm.ShareCarCardAsync(null!);
        await vm.ShareCarLinkAsync(null!);

        Assert.AreEqual(0, recorder.Images.Count);
        Assert.AreEqual(0, recorder.Links.Count);
    }

    [TestMethod]
    public async Task ALiveGrid_SharesNoSession()
    {
        var vm = CreateGrid(PublicEvent());
        vm.IsRealTime = true;
        // Left over from a grid that showed a session earlier, which a live link must ignore: the
        // point of leaving the session out is that the link keeps following the event.
        vm.PinnedSessionId = 7;

        await vm.ShareEventLink();

        Assert.AreEqual("https://redmist.racing/timing/42?src=app-share", recorder.Links.Single().Url);
    }

    #endregion
}
