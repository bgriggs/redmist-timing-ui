using System;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Decides whether the live timing screen still needs to fetch a whole session state from the
/// server, or whether the hub is already telling it everything.
/// </summary>
/// <remarks>
/// The screen has two sources for the same data: a SignalR subscription delivering patches, and a
/// REST call returning the entire session state. It used to run the REST call every five seconds
/// regardless, which during one race weekend was most of a request per second per viewer across the
/// user base - enough to spend the server's rate limit and draw several hundred 429s in an
/// afternoon, while the hub beside it was delivering the same data for free.
///
/// What makes gating safe on the feed this app is watched over is that silence really does mean
/// something is wrong. RMonitor emits an $F heartbeat once a second, and the processor's
/// HeartbeatStateUpdate compares its time-of-day against the session state before publishing, so
/// the wall clock alone produces a non-empty patch every second for as long as the feed is alive.
/// There is no application-level keep-alive to mistake for data, and no legitimate quiet period to
/// misread: a red flag, a caution, the gap between sessions - the clock advances through all of them.
///
/// That guarantee is RMonitor's, not the pipeline's. The other lanes - multiloop, x2 passings,
/// flags, Flagtronics, lap-completed - publish only when something changes, and the external feed
/// ticks without ever touching time-of-day. An event carried by those alone falls back to polling
/// every five seconds, which is what this replaced, so the gate costs nothing it did not already
/// spend. It is the reason the threshold below cannot simply be raised to save more: doing that
/// trades away how quickly a stalled RMonitor feed is noticed, without helping the quiet lanes.
/// </remarks>
internal static class LivePollingPolicy
{
    /// <summary>How long the hub may go quiet before the screen stops believing it.</summary>
    /// <remarks>
    /// Five missed heartbeats. Evaluated on a five-second tick against a one-second cadence, the
    /// newest patch is at most about a second old when the question is asked, so there are four
    /// seconds of slack before this fires - a late tick or a slow patch cannot trip it on its own.
    /// </remarks>
    public static readonly TimeSpan HubSilenceBeforeRefreshing = TimeSpan.FromSeconds(5);

    /// <summary>How long a screen fed only by patches may run before taking a whole state again.</summary>
    /// <remarks>
    /// The hub sends deltas, so a patch that never arrives leaves the grid quietly wrong for as long
    /// as the session lasts - a car's position or pit state frozen at whatever it was. Reconnects
    /// are the obvious way to miss one and are handled separately, by refreshing on the transition
    /// back to Connected; this floor covers the rest. It is insurance rather than correctness, so it
    /// is set long: twelve requests an hour against the seven hundred and twenty this replaces.
    /// </remarks>
    public static readonly TimeSpan FullRefreshFloor = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether the periodic tick should fetch a full session state.
    /// </summary>
    /// <param name="hubConnected">Whether the hub subscription is currently connected.</param>
    /// <param name="sinceHubMessage">Time since the hub last delivered anything for this event.</param>
    /// <param name="sinceFullRefresh">Time since the last whole session state was applied.</param>
    public static bool ShouldRefresh(bool hubConnected, TimeSpan sinceHubMessage, TimeSpan sinceFullRefresh)
    {
        // A disconnected hub is the case the poll exists for, and it is asked first because the
        // other two questions are meaningless while nothing can arrive: a connection that dropped a
        // moment ago has a recent message behind it and would otherwise look healthy.
        if (!hubConnected)
        {
            return true;
        }

        return sinceHubMessage >= HubSilenceBeforeRefreshing || sinceFullRefresh >= FullRefreshFloor;
    }
}
