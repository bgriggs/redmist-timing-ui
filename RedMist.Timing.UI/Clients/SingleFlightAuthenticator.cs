using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

/// <summary>
/// Lets one authentication at a time through to the authenticator it wraps, so requests that start
/// together share a token rather than each fetching its own - but only for as long as sharing is
/// still cheaper than not.
/// </summary>
/// <remarks>
/// <see cref="BigMission.Shared.Auth.KeycloakServiceAuthenticator"/> caches its token on the
/// instance, which is why <see cref="RestClientFactory"/> shares one. But the cache is only read at
/// the start of its Authenticate and written at the end, with nothing holding the gap: requests
/// that arrive together all miss, and all fetch. That is measured rather than assumed - six
/// concurrent calls produce six token requests, and each one allocates its own HttpClient inside
/// the library, so it is six connections and six TLS handshakes as well. Startup is exactly that
/// case, now that the version check runs alongside the events list rather than in front of it.
///
/// In the ordinary case queueing costs nothing and saves a round trip: the first caller's fetch is
/// one every later caller was going to wait for anyway - none of them can send a request without a
/// token - so they arrive at a cached token instead of starting their own.
///
/// <see cref="shareWindow"/> is what keeps that from becoming a liability, and it is not a
/// nicety. The token fetch inside the library is uncancellable and runs on HttpClient's 100 second
/// default, and the caller holding the gate may already have given up: the version check abandons
/// its request after five seconds, but the fetch behind it carries on regardless. Every other
/// request in the app authenticates with no cancellation token at all, so a plain gate would make
/// them wait on that dead fetch for a minute and a half - where before this class existed they
/// would have fetched their own token in parallel and been done. Waiting is therefore given a
/// ceiling, and a caller that reaches it goes ahead unsynchronized.
///
/// That ceiling is not free, and the cost is worth stating rather than rounding to "at worst an
/// extra token". A caller can only ever fetch once, so the count is bounded by what the app did
/// before this class existed - but a fetch lasting between one and two share windows is the case
/// this handles worst: the waiters sit for the whole window and then start their own fetch, which
/// is a duplicate token and up to a window of added latency, where either waiting indefinitely or
/// never waiting at all would have been quicker. It is the price of not being able to tell a slow
/// fetch from an abandoned one, and it is bounded by <see cref="shareWindow"/> in exchange for
/// removing an unbounded stall.
///
/// It also means a self-deadlock is a stall rather than a hang. Nothing re-enters this today -
/// RestSharp authenticates once per request and does not re-authenticate on a redirect, and the
/// library's token fetch uses a bare HttpClient rather than a RestClient - but that is a property
/// of code in another repo, and the ceiling is what stops a change over there turning into an app
/// that never authenticates again.
///
/// Not IDisposable: <see cref="SemaphoreSlim"/> only takes an unmanaged handle once
/// AvailableWaitHandle is read, and nothing here reads it.
/// </remarks>
public sealed class SingleFlightAuthenticator : IAuthenticator
{
    /// <summary>
    /// How long a caller waits for another's token fetch before going and getting its own.
    /// </summary>
    /// <remarks>
    /// Comfortably longer than a healthy fetch, which is a few hundred milliseconds, so the sharing
    /// this exists for happens every time it can, and far enough from the hundred second ceiling on
    /// the fetch itself that a wedged one cannot serialize startup behind it. Five seconds because
    /// that is already the app's idea of how long this call is worth waiting on, in the version
    /// check's own bound - not because the two have to agree.
    /// </remarks>
    public static readonly TimeSpan DefaultShareWindow = TimeSpan.FromSeconds(5);

    private readonly IAuthenticator inner;
    private readonly TimeSpan shareWindow;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <param name="shareWindow">Defaults to <see cref="DefaultShareWindow"/>. Tests shorten it.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The window is not positive. Rejected here rather than passed on, because neither degenerate
    /// value announces itself: zero is accepted by SemaphoreSlim and silently turns every caller
    /// into a fall-through, so the class would go on existing while sharing nothing, and a negative
    /// one - Timeout.InfiniteTimeSpan included, which is what someone reaching for "just wait"
    /// would write - either throws on every single request the app makes, from inside RestSharp
    /// where nothing wraps it, or restores the unbounded wait this ceiling exists to remove.
    /// </exception>
    public SingleFlightAuthenticator(IAuthenticator inner, TimeSpan? shareWindow = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        if (shareWindow is { } window && window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(shareWindow), window, "The share window has to be positive.");
        }

        this.inner = inner;
        this.shareWindow = shareWindow ?? DefaultShareWindow;
    }

    /// <remarks>
    /// ConfigureAwait(false) because none of this touches the UI. Startup calls it from the UI
    /// thread, and resuming there to hand a token to RestSharp would put the wait for every queued
    /// caller through the dispatcher queue for no reason.
    /// </remarks>
    public async ValueTask Authenticate(IRestClient client, RestRequest request, CancellationToken cancellationToken = default)
    {
        // False means the wait ran out rather than the gate being taken, and the release below is
        // keyed off it: releasing a permit this call never held would hand a second caller through
        // and, once the count ran past its maximum, throw SemaphoreFullException.
        //
        // Cancellation usually leaves by throwing, without entering the try - but not always, and
        // the release is keyed off the return value rather than that assumption. A release landing
        // in the same instant as the cancellation completes the wait, so this returns true with the
        // permit genuinely held and an already-canceled token; the inner call then throws and the
        // finally hands the permit back, which is the same path as any other failure. Worth knowing
        // rather than relying on: the exception type also differs by which way it went - canceled
        // before the wait gives TaskCanceledException, canceled during it gives a bare
        // OperationCanceledException - so anything catching this upstream has to catch the base.
        var holdsGate = await gate.WaitAsync(shareWindow, cancellationToken).ConfigureAwait(false);
        try
        {
            await inner.Authenticate(client, request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (holdsGate)
            {
                gate.Release();
            }
        }
    }
}
