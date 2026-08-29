using RedMist.Timing.UI.Services;
using Sentry;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace RedMist.Timing.UI.Tests.Services;

/// <summary>
/// Covers what crash reporting decides to send: which faults count as connectivity noise, and which
/// count as a deliberate cancellation rather than a failure.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CrashReportingPolicyTests
{
    private static AggregateException Aggregate(params Exception[] faults) => new(faults);

    [TestInitialize]
    public void ResetThrottle()
    {
        // The throttle is process-global mutable state. Without this the count assertion below is
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
    public void HttpClientTimeout_IsNotTreatedAsCancellation()
    {
        // The trap: HttpClient surfaces a timeout as TaskCanceledException, so a naive check on
        // OperationCanceledException silently discards the app's most common genuine failure.
        // .NET distinguishes them by the inner TimeoutException.
        var timeout = new TaskCanceledException("timed out", new TimeoutException());

        Assert.IsFalse(CrashReporting.IsDeliberateCancellation(Aggregate(timeout)));
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
    public void ConnectivityIsFoundThroughInnerExceptions()
    {
        // SignalR wraps the transport failure, so only the innermost exception names it.
        var wrapped = new InvalidOperationException("connect failed", new HttpRequestException("refused"));

        Assert.IsTrue(CrashReporting.IsConnectivityFailure(wrapped));
    }

    [TestMethod]
    public void OrdinaryFaults_DoNotCountAsConnectivity()
    {
        Assert.IsFalse(CrashReporting.IsConnectivityFailure(new NullReferenceException()));
        Assert.IsFalse(CrashReporting.IsConnectivityFailure(new InvalidOperationException()));
        Assert.IsFalse(CrashReporting.IsConnectivityFailure(null));
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
            if (CrashReporting.ThrottleConnectivityNoise(e) is not null)
            {
                sent++;
            }
        }

        Assert.IsTrue(sent > 0, "The first failures must still report - a real outage has to be visible.");
        Assert.IsTrue(sent < 50, "Repeated failures must not all be sent.");
        Assert.AreEqual(3, sent);
    }

    [TestMethod]
    public void AFatalIsNeverThrottled_EvenWhenItWrapsAConnectivityFailure()
    {
        // Spend the window on noise first.
        for (var i = 0; i < 10; i++)
        {
            CrashReporting.ThrottleConnectivityNoise(new SentryEvent(new HttpRequestException("refused")));
        }

        // A crash is never noise. Dropping it would leave the session marked crashed with no issue
        // explaining it - the unattributable state this whole change exists to end.
        var fatal = new SentryEvent(new TimeoutException("died")) { Level = SentryLevel.Fatal };

        Assert.IsNotNull(CrashReporting.ThrottleConnectivityNoise(fatal));
    }

    [TestMethod]
    public void OrdinaryFaults_AreNeverThrottled()
    {
        // Whatever the network is doing, a real bug must always get through.
        for (var i = 0; i < 50; i++)
        {
            var e = new SentryEvent(new NullReferenceException());
            Assert.IsNotNull(CrashReporting.ThrottleConnectivityNoise(e));
        }
    }

    [TestMethod]
    public void AnEventWithNoException_IsNeverThrottled()
    {
        // Messages captured without an exception, e.g. via CaptureMessage.
        Assert.IsNotNull(CrashReporting.ThrottleConnectivityNoise(new SentryEvent()));
    }
}
