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

    /// <summary>
    /// How long the server has to keep saying an event has nothing live before the screen stops
    /// asking on every tick.
    /// </summary>
    /// <remarks>
    /// Long enough to sit out an event starting. Production shows twelve to fifteen seconds of these
    /// answers between orchestration launching an event's processor and the processor serving state,
    /// and a viewer who opened the event in that window should have the first whole state as soon as
    /// there is one. An event still saying so after a minute has finished or has not begun.
    /// </remarks>
    public static readonly TimeSpan NotLiveBeforeHoldingOff = TimeSpan.FromMinutes(1);

    /// <summary>How often a screen on an event with nothing live still asks.</summary>
    /// <remarks>
    /// Roughly every seventh tick instead of every one: the answer is stamped when it arrives, so the
    /// first tick a full interval later is usually the seventh. It is the backstop rather than the way
    /// an event coming back gets noticed - a running event sends patches, and any patch lifts the hold
    /// at once.
    /// </remarks>
    public static readonly TimeSpan NotLiveRecheckInterval = TimeSpan.FromSeconds(30);

    /// <summary>How far apart two not-live answers can be and still count as one run.</summary>
    /// <remarks>
    /// Well past a recheck, so a screen being held off stays in one run. What it separates is a screen
    /// that stopped asking - the app in the background, or left open overnight on a multi-day event -
    /// whose next answer should get the same grace as a first one.
    /// </remarks>
    public static readonly TimeSpan NotLiveRunBreak = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether the periodic tick should skip an event the server keeps saying has nothing live.
    /// </summary>
    /// <remarks>
    /// Without this a screen left open on a finished event asked every five seconds for as long as it
    /// stayed open - one viewer's phone for most of a day - because a finished event's hub is silent,
    /// and silence is exactly what <see cref="ShouldRefresh"/> treats as needing a poll.
    /// </remarks>
    /// <param name="notLiveFor">How long the server has been answering that way without a break.</param>
    /// <param name="sinceNotLiveAnswer">How long since it last did.</param>
    /// <param name="hubHeardFromSince">Whether the hub has delivered anything for the event since then.</param>
    public static bool IsHoldingOff(TimeSpan notLiveFor, TimeSpan sinceNotLiveAnswer, bool hubHeardFromSince)
    {
        // The feed outranks the last answer: patches arriving mean the event is running again, and
        // the screen needs a whole state now rather than at the next recheck.
        if (hubHeardFromSince)
        {
            return false;
        }

        return notLiveFor >= NotLiveBeforeHoldingOff && sinceNotLiveAnswer < NotLiveRecheckInterval;
    }
}
