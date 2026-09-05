using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.Tests.Services;

/// <summary>
/// Covers when the live timing screen still asks the server for a whole session state.
/// </summary>
/// <remarks>
/// The screen has two sources for the same data, and it used to run both flat out: a hub
/// subscription delivering patches, and a REST call every five seconds regardless. Across the user
/// base that was enough to spend the server's per-caller rate limit and draw several hundred 429s
/// in one afternoon, while the hub beside it was delivering the same data for free.
///
/// Two things have to hold for standing the poll down to be safe, and both are pinned here: the
/// screen keeps polling whenever the hub is not actually delivering, and it takes a whole state
/// again after a reconnect - because the server sends none on subscribe, so the delta stream
/// resumes with a gap behind it that nothing else would ever repair.
/// </remarks>
[TestClass]
public sealed class LivePollingPolicyTests
{
    private static readonly TimeSpan JustArrived = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RecentlyResynced = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void AHealthyHub_StandsThePollDown()
    {
        // The case that matters for load: patches arriving, nothing to ask for.
        Assert.IsFalse(LivePollingPolicy.ShouldRefresh(hubConnected: true, JustArrived, RecentlyResynced));
    }

    [TestMethod]
    public void ADisconnectedHub_IsPolled()
    {
        Assert.IsTrue(LivePollingPolicy.ShouldRefresh(hubConnected: false, JustArrived, RecentlyResynced),
            "Nothing can arrive over a hub that is down, however recently something last did.");
    }

    [TestMethod]
    public void ADisconnectedHubIsAskedAboutFirst()
    {
        // A connection that dropped a moment ago still has a recent message behind it, so reading
        // the data before the connection would call it healthy for the first five seconds of an
        // outage - which is the first tick, the one that would have noticed.
        Assert.IsTrue(LivePollingPolicy.ShouldRefresh(hubConnected: false,
            sinceHubMessage: TimeSpan.Zero, sinceFullRefresh: TimeSpan.Zero));
    }

    [TestMethod]
    public void AConnectedButSilentHub_IsPolled()
    {
        // RMonitor sends an $F heartbeat every second and its time-of-day always advances, so the
        // processor publishes a patch every second for as long as the feed is alive. Five seconds of
        // silence is five missed heartbeats, not a quiet moment in the race.
        Assert.IsTrue(LivePollingPolicy.ShouldRefresh(hubConnected: true,
            LivePollingPolicy.HubSilenceBeforeRefreshing, RecentlyResynced));
    }

    [TestMethod]
    public void AHubThatHasNeverDelivered_IsPolled()
    {
        // How the screen starts, and how it stays if the subscription never takes.
        Assert.IsTrue(LivePollingPolicy.ShouldRefresh(hubConnected: true, TimeSpan.MaxValue, TimeSpan.MaxValue));
    }

    [TestMethod]
    public void TheSilenceThresholdLeavesRoomForALateTick()
    {
        // Evaluated on a five-second tick against a one-second cadence, the newest patch is about a
        // second old when the question is asked. If the threshold did not leave slack above that,
        // ordinary jitter would poll - and polling hardest under load is the wrong response.
        Assert.IsTrue(LivePollingPolicy.HubSilenceBeforeRefreshing > TimeSpan.FromSeconds(2),
            "A threshold this tight would fire on a single late patch.");
        Assert.IsFalse(LivePollingPolicy.ShouldRefresh(hubConnected: true,
            TimeSpan.FromSeconds(2), RecentlyResynced));
    }

    [TestMethod]
    public void TheSilenceThresholdFiresWellBeforeSignalRGivesUp()
    {
        // A wedged-but-connected hub has to be noticed well before SignalR gives up on it, or the
        // screen spends half a minute showing a frozen grid it believes is live.
        // This is SignalR's client-side ServerTimeout default, which nothing in this app overrides.
        var signalRGivesUpAfter = TimeSpan.FromSeconds(30);

        Assert.IsTrue(LivePollingPolicy.HubSilenceBeforeRefreshing < signalRGivesUpAfter / 2);
    }

    [TestMethod]
    public void AScreenRunningOnDeltasAlone_TakesAWholeStateEventually()
    {
        // The hub sends deltas, so a patch that never arrives leaves the grid quietly wrong for the
        // rest of the session - a car's position or pit state frozen at whatever it last was.
        Assert.IsTrue(LivePollingPolicy.ShouldRefresh(hubConnected: true, JustArrived,
            LivePollingPolicy.FullRefreshFloor));
    }

    [TestMethod]
    public void TheResyncFloorIsFarCheaperThanPolling()
    {
        // The whole point of the change. Anything under a minute here gives most of the traffic
        // back, and the floor is insurance against a dropped patch rather than the primary path.
        Assert.IsTrue(LivePollingPolicy.FullRefreshFloor >= TimeSpan.FromMinutes(1),
            "At this rate the gate would save little of what it was written to save.");

        // Bounded from above as well, and for the more important reason: this floor is the only
        // backstop for everything the gate stands down, so it is also the longest a missed patch can
        // leave a row quietly wrong.
        Assert.IsTrue(LivePollingPolicy.FullRefreshFloor <= TimeSpan.FromMinutes(10),
            "A session can end before the screen next checks its whole state against the server.");
    }
}
