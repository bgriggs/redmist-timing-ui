using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.Timing.UI.ViewModels.Design;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.Clients;

/// <summary>
/// Covers the clock the live timing screen reads to decide whether the hub is still delivering.
/// </summary>
/// <remarks>
/// Without it the screen has no way to tell a hub that is quiet from one that is broken, and the
/// gate on the periodic refresh degrades to polling every five seconds - silently, since nothing
/// else in the app reads this. That is a regression that costs the server rather than the user, so
/// it would not be noticed until the next race weekend.
/// </remarks>
[TestClass]
public sealed class HubClientLivenessTests
{
    private static HubClient Create() => new(
        new DebugLoggerFactory(),
        TestViewModelFactory.CreateConfiguration(),
        new EventAccessCodeStore(new MockPreferencesService()));

    [TestMethod]
    public void BeforeAnythingArrives_ThereIsNoTimestamp()
    {
        // Null rather than "now": a screen whose subscription never took has to keep polling.
        Assert.IsNull(Create().LastEventMessageUtc);
    }

    [TestMethod]
    public void ASessionPatch_AdvancesTheClock()
    {
        var client = Create();

        client.ProcessSessionMessage(new SessionStatePatch());

        Assert.IsNotNull(client.LastEventMessageUtc);
    }

    [TestMethod]
    public void CarPatches_AdvanceTheClock()
    {
        var client = Create();

        client.ProcessCarPatches([]);

        Assert.IsNotNull(client.LastEventMessageUtc);
    }

    [TestMethod]
    public void AReset_AdvancesTheClock()
    {
        var client = Create();

        client.ProcessReset();

        Assert.IsNotNull(client.LastEventMessageUtc);
    }

    [TestMethod]
    public void EachMessageMovesTheClockForward()
    {
        // An age is what the gate reads, so a clock that stopped at the first message would report
        // the feed dead five seconds into a perfectly healthy session.
        var client = Create();
        client.ProcessSessionMessage(new SessionStatePatch());
        var first = client.LastEventMessageUtc;

        Thread.Sleep(20);
        client.ProcessSessionMessage(new SessionStatePatch());

        Assert.IsTrue(client.LastEventMessageUtc > first);
    }

    [TestMethod]
    public void WithNoConnection_TheClientIsNotConnected()
    {
        Assert.IsFalse(Create().IsConnected);
    }
}
