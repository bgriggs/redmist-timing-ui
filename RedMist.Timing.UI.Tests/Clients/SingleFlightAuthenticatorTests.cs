using RedMist.Timing.UI.Clients;
using RestSharp;
using RestSharp.Authenticators;

namespace RedMist.Timing.UI.Tests.Clients;

/// <summary>
/// Covers the wrapper that keeps a cold start from fetching one token per request.
/// </summary>
/// <remarks>
/// Driven against a stub rather than a real Keycloak, because what is pinned here is the wrapper's
/// own behavior: that a second caller waits rather than starting its own fetch, and that it stops
/// waiting rather than being stuck behind one that never finishes.
/// </remarks>
[TestClass]
public sealed class SingleFlightAuthenticatorTests
{
    private static readonly TimeSpan NeverArrives = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Stands in for an authenticator that fetches a token: it records how many callers are inside
    /// it at once, marks each request it authenticated, and holds callers until the test lets go.
    /// </summary>
    private sealed class CountingAuthenticator : IAuthenticator
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int inFlight;

        public int Calls;
        public int MaxConcurrent;
        public Exception? Throw;
        public CancellationToken LastToken;

        /// <summary>Signalled once a caller is inside, so the test can pile more up behind it.</summary>
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => release.TrySetResult();

        public async ValueTask Authenticate(IRestClient client, RestRequest request, CancellationToken cancellationToken = default)
        {
            LastToken = cancellationToken;
            Interlocked.Increment(ref Calls);
            var now = Interlocked.Increment(ref inFlight);

            // Not Math.Max on a plain field: two callers getting through at once is the failure this
            // test exists to catch, so the recording of it cannot itself be racy.
            int seen;
            while (now > (seen = Volatile.Read(ref MaxConcurrent)) &&
                   Interlocked.CompareExchange(ref MaxConcurrent, now, seen) != seen)
            {
            }

            Entered.TrySetResult();
            try
            {
                await release.Task.WaitAsync(cancellationToken);
                if (Throw is not null)
                {
                    throw Throw;
                }

                // What a real authenticator is for, so a queued caller can be shown to still get it.
                request.AddOrUpdateParameter(Parameter.CreateParameter("Authorization", "Bearer test", ParameterType.HttpHeader));
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }
    }

    private static bool IsAuthenticated(RestRequest request) =>
        request.Parameters.Any(p => p.Name == "Authorization");

    /// <summary>
    /// Runs <paramref name="callers"/> calls at once and does not return until every one of them has
    /// been picked up by the thread pool.
    /// </summary>
    /// <remarks>
    /// This raises the floor rather than removing the race: the count goes up on the way into the
    /// call, so it says the work item is running, not that it has reached the gate. That is enough
    /// to keep an assertion made straight afterwards from passing purely because nothing had been
    /// scheduled yet, but it is not proof, and no assertion should rest on it alone - the ones that
    /// have to hold are made on <c>MaxConcurrent</c> after every caller has finished, which does not
    /// depend on timing at all.
    /// </remarks>
    private static async Task<Task[]> StartAll(int callers, Func<int, Task> call)
    {
        var started = 0;
        var tasks = Enumerable.Range(0, callers)
            .Select(i => Task.Run(() =>
            {
                Interlocked.Increment(ref started);
                return call(i);
            }))
            .ToArray();

        await WaitUntil(() => Volatile.Read(ref started) == callers, "The callers never all started; the thread pool is wedged.");

        return tasks;
    }

    /// <summary>
    /// Spins until <paramref name="condition"/> holds, and fails rather than hanging the run if it
    /// never does.
    /// </summary>
    private static async Task WaitUntil(Func<bool> condition, string message)
    {
        var giveUp = DateTime.UtcNow + NeverArrives;
        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < giveUp, message);
            await Task.Delay(10);
        }
    }

    [TestMethod]
    public async Task CallersThatArriveTogether_FetchOneTokenBetweenThem()
    {
        var inner = new CountingAuthenticator();
        var authenticator = new SingleFlightAuthenticator(inner);
        using var client = new RestClient("http://localhost/");

        // The first caller is held inside the stub, so the rest genuinely overlap it rather than
        // arriving after it has finished and reading a cached token.
        var first = Task.Run(async () => await authenticator.Authenticate(client, new RestRequest("resource")));
        await inner.Entered.Task.WaitAsync(NeverArrives);

        var rest = await StartAll(5, async _ => await authenticator.Authenticate(client, new RestRequest("resource")));

        // An early read, worth making for the sharper failure it gives when the gate is missing
        // altogether. The assertion that actually has to hold is the one after everything finishes.
        Assert.AreEqual(1, Volatile.Read(ref inner.Calls),
            "Every caller has started and only one is inside the token fetch, which is the whole point.");

        inner.Release();
        await Task.WhenAll(rest.Append(first)).WaitAsync(NeverArrives);

        Assert.AreEqual(1, inner.MaxConcurrent, "Token fetches overlapped, which is the whole of what this prevents.");
    }

    [TestMethod]
    public async Task EveryCaller_StillGetsAuthenticated()
    {
        // Queueing must not turn into dropping: each request has to come back carrying what its own
        // Authenticate call was supposed to put on it.
        var inner = new CountingAuthenticator();
        var authenticator = new SingleFlightAuthenticator(inner);
        using var client = new RestClient("http://localhost/");
        var requests = Enumerable.Range(0, 4).Select(_ => new RestRequest("resource")).ToArray();

        var calls = await StartAll(requests.Length, async i => await authenticator.Authenticate(client, requests[i]));
        inner.Release();
        await Task.WhenAll(calls).WaitAsync(NeverArrives);

        Assert.AreEqual(4, inner.Calls, "Every request has to be authenticated, not just the one that took the gate.");
        Assert.IsTrue(requests.All(IsAuthenticated), "A queued caller came back without its authentication.");
    }

    /// <summary>
    /// The gate is an optimization, and it has to be one a caller can walk away from.
    /// </summary>
    /// <remarks>
    /// The token fetch in the library is uncancellable and runs on HttpClient's 100 second default,
    /// and the caller holding the gate may already have abandoned its own request - the version
    /// check gives up after five seconds and leaves its fetch running. Every other request in the
    /// app authenticates with no cancellation token, so without the ceiling they would all queue
    /// behind that dead fetch for a minute and a half, having previously just fetched their own
    /// token in parallel. Falling through is what makes the worst case no worse than not having
    /// this class at all.
    /// </remarks>
    [TestMethod]
    public async Task ACallerHoldingTheGate_DoesNotStrandTheRest()
    {
        var stuck = new CountingAuthenticator();
        var authenticator = new SingleFlightAuthenticator(stuck, shareWindow: TimeSpan.FromMilliseconds(200));
        using var client = new RestClient("http://localhost/");

        // Never released, standing in for a fetch nobody is waiting on any more.
        var holder = Task.Run(async () => await authenticator.Authenticate(client, new RestRequest("resource")));
        await stuck.Entered.Task.WaitAsync(NeverArrives);

        var request = new RestRequest("resource");
        var queued = Task.Run(async () => await authenticator.Authenticate(client, request));

        // Being inside the fetch alongside the holder is the assertion: the stub still has the first
        // caller, so a second one is only in there by having given up on the gate. Bounded, so
        // removing the fall-through fails this with its message rather than hanging the run.
        //
        // MaxConcurrent rather than Calls, which the stub raises one line earlier: waiting on the
        // count would let the release below race the second caller's arrival, and the two of them
        // would then never be recorded as overlapping - failing the assertion after this with a
        // message about the fall-through that is not what went wrong.
        await WaitUntil(() => Volatile.Read(ref stuck.MaxConcurrent) == 2,
            "The caller that gave up waiting never got to fetch a token of its own.");

        stuck.Release();
        await Task.WhenAll(queued, holder).WaitAsync(NeverArrives);

        Assert.IsTrue(IsAuthenticated(request), "The caller that gave up waiting still has to come back authenticated.");
    }

    [TestMethod]
    public async Task AFailedFetch_LetsTheNextCallerTry()
    {
        // A token fetch that throws must release the gate. Holding it would turn one failed request
        // into an app that authenticates only once every share window.
        var failing = new CountingAuthenticator { Throw = new HttpRequestException("auth server down") };
        var authenticator = new SingleFlightAuthenticator(failing);
        using var client = new RestClient("http://localhost/");

        failing.Release();
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            async () => await authenticator.Authenticate(client, new RestRequest("resource")).AsTask().WaitAsync(NeverArrives));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            async () => await authenticator.Authenticate(client, new RestRequest("resource")).AsTask().WaitAsync(NeverArrives));

        Assert.AreEqual(2, failing.Calls, "The second caller never reached the authenticator, so the gate was not released.");
    }

    [TestMethod]
    public async Task ACanceledCaller_LeavesTheGateAlone()
    {
        // Cancellation leaves without a permit, so releasing one would let a second caller in and
        // eventually push the count past its maximum. A gate that has not been corrupted still
        // admits exactly one caller after this.
        var inner = new CountingAuthenticator();
        var authenticator = new SingleFlightAuthenticator(inner);
        using var client = new RestClient("http://localhost/");
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            async () => await authenticator.Authenticate(client, new RestRequest("resource"), canceled.Token));

        // The same shape as CallersThatArriveTogether, run on a gate the canceled call has already
        // been through. A permit released that was never held shows up as a second caller getting
        // in beside the first; one taken and never given back shows up as the wait below never
        // ending, which the timeout turns into a failure rather than a hung run.
        var first = Task.Run(async () => await authenticator.Authenticate(client, new RestRequest("resource")));
        await inner.Entered.Task.WaitAsync(NeverArrives);
        var rest = await StartAll(2, async _ => await authenticator.Authenticate(client, new RestRequest("resource")));

        Assert.AreEqual(1, Volatile.Read(ref inner.Calls), "The gate let another caller through, so a permit was leaked.");

        inner.Release();
        await Task.WhenAll(rest.Append(first)).WaitAsync(NeverArrives);

        Assert.AreEqual(1, inner.MaxConcurrent, "The gate let another caller through, so a permit was leaked.");
    }

    /// <summary>
    /// A window that is not positive is refused where it is written, not where it goes wrong.
    /// </summary>
    /// <remarks>
    /// Neither degenerate value announces itself otherwise: zero is accepted by SemaphoreSlim and
    /// silently turns every caller into a fall-through, leaving a class that shares nothing, and
    /// Timeout.InfiniteTimeSpan - what someone reaching for "just wait" would reasonably write -
    /// puts back the unbounded stall the ceiling exists to remove.
    /// </remarks>
    [TestMethod]
    [DataRow(0, DisplayName = "Zero, which shares nothing")]
    [DataRow(-1, DisplayName = "Timeout.InfiniteTimeSpan, which never gives up")]
    [DataRow(-5000, DisplayName = "Negative, which throws on every request")]
    public void AWindowThatIsNotPositive_IsRefused(int milliseconds)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new SingleFlightAuthenticator(new CountingAuthenticator(), TimeSpan.FromMilliseconds(milliseconds)));
    }

    [TestMethod]
    public async Task TheCallersCancellation_ReachesTheAuthenticator()
    {
        // The token is forwarded, not swallowed. Nothing depends on it today - the Keycloak
        // authenticator ignores the one it is given - but a wrapper that quietly dropped it would
        // be the kind of thing only noticed once something downstream started honoring it.
        var inner = new CountingAuthenticator();
        var authenticator = new SingleFlightAuthenticator(inner);
        using var client = new RestClient("http://localhost/");
        using var cts = new CancellationTokenSource();

        inner.Release();
        await authenticator.Authenticate(client, new RestRequest("resource"), cts.Token).AsTask().WaitAsync(NeverArrives);

        Assert.AreEqual(cts.Token, inner.LastToken, "The caller's cancellation did not reach the authenticator.");
    }
}
