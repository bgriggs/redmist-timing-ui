using RedMist.Timing.UI.Services;
using Sentry;
using Sentry.Protocol;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace RedMist.Timing.UI.Tests.Services;

/// <summary>
/// Covers what crash reporting decides to send: which faults count as noise, which count as a
/// deliberate cancellation rather than a failure, and how the ones that are kept are grouped.
/// </summary>
/// <remarks>
/// The exception shapes here are not invented. They were taken from a live race weekend's Sentry
/// data and from driving the pinned RestSharp against a local server that refuses, delays and
/// answers with an error, because the shape a canceled request actually arrives in is the thing
/// this policy is easiest to get wrong about.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class CrashReportingPolicyTests
{
    private static AggregateException Aggregate(params Exception[] faults) => new(faults);

    /// <summary>The fingerprint an event was filed under, or null when it kept its own grouping.</summary>
    private static string? FingerprintOf(SentryEvent? sent)
    {
        Assert.IsNotNull(sent, "The event was dropped, so it has no grouping to check.");
        return sent.Fingerprint.Count == 0 ? null : string.Join('|', sent.Fingerprint);
    }

    /// <summary>Sends one fault through the policy in a window of its own.</summary>
    private static SentryEvent? FirstInWindow(Exception fault)
    {
        CrashReporting.ResetConnectivityThrottle();
        return CrashReporting.ApplyNoisePolicy(new SentryEvent(fault));
    }

    /// <summary>How RestSharp reports a request the caller canceled: the cancellation is not outermost.</summary>
    private static Exception AbortedRequest()
        => new HttpRequestException("Request aborted",
            new TaskCanceledException("The operation was canceled.",
                new SocketException(995)));

    /// <summary>How RestSharp reports a request that outlived its own deadline.</summary>
    private static Exception TimedOutRequest()
        => new TimeoutException("Request timed out", new TaskCanceledException("The operation was canceled."));

    /// <summary>How a non-success response arrives: an HttpRequestException that carries the status.</summary>
    private static HttpRequestException ServerAnswered(HttpStatusCode status)
        => new($"Request failed with status code {status}", inner: null, statusCode: status);

    [TestInitialize]
    public void ResetThrottle()
    {
        // The throttle is process-global mutable state. Without this the count assertions below are
        // order-dependent, and would break under a retry or a second throttle test.
        CrashReporting.ResetConnectivityThrottle();
    }

    // --- Cancellation classification ---------------------------------------------------------

    [TestMethod]
    public void Cancellation_IsTreatedAsDeliberate()
    {
        // Navigating away from a live event cancels its background work. Reporting that as an error
        // would bury the faults worth reading.
        Assert.IsTrue(CrashReporting.IsDeliberateCancellation(Aggregate(new OperationCanceledException())));
        Assert.IsTrue(CrashReporting.IsDeliberateCancellation(Aggregate(new TaskCanceledException())));
    }

    [TestMethod]
    public void ASingleCancellation_IsTreatedAsDeliberate()
    {
        // Most cancellations reach reporting one at a time through ILogger rather than wrapped in
        // an AggregateException by the unobserved-task handler.
        Assert.IsTrue(CrashReporting.IsDeliberateCancellation(new OperationCanceledException()));
        Assert.IsFalse(CrashReporting.IsDeliberateCancellation((Exception?)null));
        Assert.IsFalse(CrashReporting.IsDeliberateCancellation(new HttpRequestException("refused")));
    }

    [TestMethod]
    public void AnAggregateReachingTheSingleOverload_StillRequiresEveryFaultToBeCancellation()
    {
        // The production path: ApplyNoisePolicy holds an Exception?, so an AggregateException binds
        // to the single overload and has to be routed back to the all-faults rule rather than being
        // judged on its first inner exception.
        Assert.IsTrue(CrashReporting.IsDeliberateCancellation((Exception?)Aggregate(new TaskCanceledException())));
        Assert.IsFalse(CrashReporting.IsDeliberateCancellation(
            (Exception?)Aggregate(new TaskCanceledException(), new InvalidOperationException("real"))));
    }

    [TestMethod]
    public void ACancellationCarryingThePlatformsReason_IsStillCancellation()
    {
        // How iOS words a request the app itself tore down: OperationCanceledException wrapping
        // NSURLError -999. The inner exception says how it was canceled, not that something broke.
        var iosShaped = new OperationCanceledException("cancelled", new Exception("NSURLErrorDomain Code=-999"));

        Assert.IsTrue(CrashReporting.IsDeliberateCancellation(iosShaped));
    }

    [TestMethod]
    public void ACancellationTheTransportLayerWrapped_IsStillCancellation()
    {
        // The shape the app's own HTTP stack produces, and the one an outermost-frame check misses:
        // RestSharp hands back HttpRequestException("Request aborted") wrapping the cancellation,
        // with a socket error under that from the aborted read. Judged on the outer type alone this
        // is a connectivity failure, and every collapsed car row spends the reporting budget.
        Assert.IsTrue(CrashReporting.IsDeliberateCancellation(AbortedRequest()));
        Assert.IsNull(CrashReporting.ApplyNoisePolicy(new SentryEvent(AbortedRequest())));
    }

    [TestMethod]
    public void HttpClientTimeout_IsNotTreatedAsCancellation()
    {
        // The trap: HttpClient surfaces a timeout as TaskCanceledException, so a naive check on
        // OperationCanceledException silently discards the app's most common genuine failure.
        // .NET distinguishes them by the inner TimeoutException.
        var timeout = new TaskCanceledException("timed out", new TimeoutException());

        Assert.IsFalse(CrashReporting.IsDeliberateCancellation(Aggregate(timeout)));
        Assert.IsFalse(CrashReporting.IsDeliberateCancellation((Exception)timeout));
    }

    [TestMethod]
    public void ATimeoutWrappingItsOwnCancellation_IsNotTreatedAsCancellation()
    {
        // The mirror image, and why the timeout test cannot be a check on the inner exception:
        // RestSharp raises TimeoutException wrapping the cancellation its deadline caused. A
        // TimeoutException anywhere in the chain settles it, whichever end the cancellation is at.
        Assert.IsFalse(CrashReporting.IsDeliberateCancellation(TimedOutRequest()));
        Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(new SentryEvent(TimedOutRequest())));
    }

    [TestMethod]
    public void AMixedAggregate_IsNotTreatedAsCancellation()
    {
        var mixed = Aggregate(new TaskCanceledException(), new InvalidOperationException("real"));

        Assert.IsFalse(CrashReporting.IsDeliberateCancellation(mixed),
            "One real fault among cancellations must still be reported.");
    }

    [TestMethod]
    public void AnEmptyAggregate_IsNotTreatedAsCancellation()
    {
        Assert.IsFalse(CrashReporting.IsDeliberateCancellation(Aggregate()));
    }

    [TestMethod]
    public void NestedCancellation_IsFlattenedBeforeJudging()
    {
        var nested = new AggregateException(new AggregateException(new TaskCanceledException()));

        Assert.IsTrue(CrashReporting.IsDeliberateCancellation(nested));
    }

    // --- Connectivity classification ---------------------------------------------------------

    [TestMethod]
    public void TransportFailures_CountAsConnectivity()
    {
        Assert.IsTrue(CrashReporting.IsConnectivityFailure(new HttpRequestException("no route")));
        Assert.IsTrue(CrashReporting.IsConnectivityFailure(new SocketException(10060)));
        Assert.IsTrue(CrashReporting.IsConnectivityFailure(new WebSocketException("closed")));
        Assert.IsTrue(CrashReporting.IsConnectivityFailure(new TimeoutException()));
    }

    [TestMethod]
    public void AndroidsWordingForLostCoverage_CountsAsConnectivity()
    {
        // WebException is related to none of the others - it derives from InvalidOperationException
        // - so it slipped the check while being how Android reports a phone leaving coverage.
        Assert.IsTrue(CrashReporting.IsConnectivityFailure(
            new WebException("Unable to resolve host \"api.redmist.racing\": No address associated with hostname")));
        Assert.IsTrue(CrashReporting.IsConnectivityFailure(new WebException("Connection reset")));
    }

    [TestMethod]
    public void AResponseTheServerActuallySent_IsNotConnectivity()
    {
        // The distinction the whole policy turns on. RestSharp reports a non-success response as an
        // HttpRequestException - the same type a phone with no signal produces - and .NET separates
        // them by populating StatusCode only when there was a response to take it from. Treating
        // these as the network's fault is how a rate-limit storm goes unnoticed for a whole event.
        foreach (var status in new[] { HttpStatusCode.TooManyRequests, HttpStatusCode.BadGateway,
                                       HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout })
        {
            Assert.IsFalse(CrashReporting.IsConnectivityFailure(ServerAnswered(status)), $"{status} came from the server.");
        }
    }

    [TestMethod]
    public void ConnectivityIsFoundThroughInnerExceptions()
    {
        // SignalR wraps the transport failure, so only the innermost exception names it.
        var wrapped = new InvalidOperationException("connect failed", new HttpRequestException("refused"));

        Assert.IsTrue(CrashReporting.IsConnectivityFailure(wrapped));
    }

    [TestMethod]
    public void AnAggregateLedByASocketFault_IsNotConnectivityIfItCarriesARealBug()
    {
        // Walking InnerException reaches only the first fault of an aggregate, so a real bug
        // travelling beside a socket error would be filed - and rationed - under a title saying the
        // network dropped. Every fault has to qualify, not just the one in front.
        var mixed = Aggregate(new SocketException(10060), new NullReferenceException());

        Assert.IsFalse(CrashReporting.IsConnectivityFailure(mixed));
        Assert.IsNull(FingerprintOf(FirstInWindow(mixed)), "A real bug must keep its own grouping.");
    }

    [TestMethod]
    public void OrdinaryFaults_DoNotCountAsConnectivity()
    {
        Assert.IsFalse(CrashReporting.IsConnectivityFailure(new NullReferenceException()));
        Assert.IsFalse(CrashReporting.IsConnectivityFailure(new InvalidOperationException()));
        Assert.IsFalse(CrashReporting.IsConnectivityFailure(null));
    }

    // --- Grouping -----------------------------------------------------------------------------

    [TestMethod]
    public void EveryShapeOfLostNetwork_GroupsAsOneIssue()
    {
        // The point of the fingerprint. These arrive from different call sites, worded differently
        // by each platform, and Sentry filed them as thirty-odd separate issues - which is what
        // buried a real layout crash at the fortieth row of the list.
        var grouped = new[]
        {
            new HttpRequestException("Connection failure"),
            new HttpRequestException("The network connection was lost."),
            new WebException("Unable to resolve host"),
            new SocketException(10060),
            (Exception)new WebSocketException("closed prematurely"),
        }.Select(fault => FingerprintOf(FirstInWindow(fault))).ToList();

        CollectionAssert.AllItemsAreNotNull(grouped, "A lost connection has to be filed under something.");
        Assert.AreEqual(1, grouped.Distinct().Count(),
            "One condition has to be one issue, or the issue list is unreadable during an event.");
    }

    [TestMethod]
    public void ATimeout_IsGroupedApartFromALostConnection()
    {
        // A timeout says the server did not answer, which is about the server; the rest of the
        // bucket says the phone had nothing to ask over. Filed together, a backend that starts
        // timing out disappears into the ambient signal loss of a race weekend.
        var lostConnection = FingerprintOf(FirstInWindow(new HttpRequestException("Connection failure")));
        var timeout = FingerprintOf(FirstInWindow(new TaskCanceledException("timed out", new TimeoutException())));

        Assert.IsNotNull(timeout);
        Assert.AreNotEqual(lostConnection, timeout);
    }

    [TestMethod]
    public void ARealFault_KeepsItsOwnGrouping()
    {
        // Only the bulk conditions are collapsed. A bug has to stay distinguishable from every
        // other bug, and a response the server sent has to stay distinguishable from the network.
        Assert.IsNull(FingerprintOf(FirstInWindow(new NullReferenceException())));
        Assert.IsNull(FingerprintOf(FirstInWindow(ServerAnswered(HttpStatusCode.TooManyRequests))));
    }

    [TestMethod]
    public void AFatalConnectivityFailure_KeepsItsOwnGrouping()
    {
        // A crash is not one of a kind with a dropped connection, however it was worded.
        var fatal = new SentryEvent(new TimeoutException("died")) { Level = SentryLevel.Fatal };

        Assert.IsNull(FingerprintOf(CrashReporting.ApplyNoisePolicy(fatal)));
    }

    // --- Throttling ---------------------------------------------------------------------------

    [TestMethod]
    public void ConnectivityNoise_IsCappedWithinAWindow()
    {
        // HubClient retries forever and logs an error per attempt, so an afternoon on bad cell
        // service would otherwise spend a month's quota.
        var sent = 0;
        for (var i = 0; i < 50; i++)
        {
            var e = new SentryEvent(new HttpRequestException("refused"));
            if (CrashReporting.ApplyNoisePolicy(e) is not null)
            {
                sent++;
            }
        }

        Assert.IsTrue(sent > 0, "The first failures must still report - a real outage has to be visible.");
        Assert.AreEqual(3, sent);
    }

    [TestMethod]
    public void EachGroupIsRationedSeparately()
    {
        // Shared, the ambient signal loss of a race weekend spends the whole budget, and a backend
        // that starts timing out adds nothing visible - which would make the grouping above a way
        // to hide an outage rather than a way to find one.
        for (var i = 0; i < 50; i++)
        {
            CrashReporting.ApplyNoisePolicy(new SentryEvent(new HttpRequestException("Connection failure")));
        }

        var timeout = new SentryEvent(new TaskCanceledException("timed out", new TimeoutException()));

        Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(timeout),
            "The paddock spending its budget must not silence the server failing to answer.");
    }

    [TestMethod]
    public void AFatalIsNeverThrottled_EvenWhenItWrapsAConnectivityFailure()
    {
        // Spend the window on noise first.
        for (var i = 0; i < 10; i++)
        {
            CrashReporting.ApplyNoisePolicy(new SentryEvent(new HttpRequestException("refused")));
        }

        // A crash is never noise. Dropping it would leave the session marked crashed with no issue
        // explaining it - the unattributable state this whole change exists to end.
        var fatal = new SentryEvent(new TimeoutException("died")) { Level = SentryLevel.Fatal };

        Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(fatal));
    }

    [TestMethod]
    public void AnUnhandledMechanism_IsNeverThrottled()
    {
        // The clause that actually fires in production, for the events CaptureFatal reports with
        // handled: false. It is not reachable through the SentryEvent constructor, because the
        // exception is only converted to SentryExceptions during capture, so it is built by hand.
        for (var i = 0; i < 10; i++)
        {
            CrashReporting.ApplyNoisePolicy(new SentryEvent(new HttpRequestException("refused")));
        }

        var unhandled = new SentryEvent(new HttpRequestException("refused"))
        {
            SentryExceptions = [new SentryException { Mechanism = new Mechanism { Handled = false } }],
        };

        Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(unhandled));
    }

    [TestMethod]
    public void Cancellation_IsDroppedRatherThanRationed()
    {
        // Every one of them, not the first few: a ration keeps what is worth seeing occasionally,
        // and a car row collapsed before its laps arrived never is.
        for (var i = 0; i < 50; i++)
        {
            Assert.IsNull(CrashReporting.ApplyNoisePolicy(new SentryEvent(new OperationCanceledException())));
        }
    }

    [TestMethod]
    public void AFatalCancellation_IsStillSent()
    {
        // The app canceling its own work does not end the process, so a cancellation that arrives
        // marked fatal is something else wearing its clothes - and dropping it would leave a
        // crashed session with no issue behind it.
        var fatal = new SentryEvent(new OperationCanceledException()) { Level = SentryLevel.Fatal };

        Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(fatal));
    }

    [TestMethod]
    public void ACancellationThatReachedAGlobalHandler_IsStillSent()
    {
        // A cancellation caught around an HTTP call is routine; the same cancellation escaping to
        // the top of the UI thread is a bug. Both arrive here stamped handled and at Error, so the
        // global handlers say so themselves. Without this, turning off the crash-on-UI-exception
        // switch to investigate a crash loop at an event would silently take these with it.
        var canceled = new SentryEvent(new OperationCanceledException());

        using (CrashReporting.ReportingUnhandledFault())
        {
            Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(canceled));
        }

        Assert.IsNull(CrashReporting.ApplyNoisePolicy(new SentryEvent(new OperationCanceledException())),
            "The scope has to end with the report, or everything after it on this thread is exempt.");
    }

    [TestMethod]
    public void OrdinaryFaults_AreNeverThrottled()
    {
        // Whatever the network is doing, a real bug must always get through.
        for (var i = 0; i < 50; i++)
        {
            var e = new SentryEvent(new NullReferenceException());
            Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(e));
        }
    }

    [TestMethod]
    public void AServerErrorIsNeverThrottled()
    {
        // 429s arrive in bulk during a rate-limit storm, which is exactly when they must not be
        // rationed away: the count is the signal.
        for (var i = 0; i < 50; i++)
        {
            Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(new SentryEvent(ServerAnswered(HttpStatusCode.TooManyRequests))));
        }
    }

    [TestMethod]
    public void AnEventWithNoException_IsNeverThrottled()
    {
        // Messages captured without an exception, e.g. via CaptureMessage.
        Assert.IsNotNull(CrashReporting.ApplyNoisePolicy(new SentryEvent()));
    }
}
