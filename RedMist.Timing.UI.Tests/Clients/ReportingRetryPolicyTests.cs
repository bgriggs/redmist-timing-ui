using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Tests.ViewModels;
using System.Reflection;
using RetryContext = Microsoft.AspNetCore.SignalR.Client.RetryContext;

namespace RedMist.Timing.UI.Tests.Clients;

/// <summary>
/// Covers the hub reporting a reconnect that has gone on long enough to be an outage.
/// </summary>
/// <remarks>
/// Crash reporting keeps SignalR's own reports of a lost connection and of each failed attempt to
/// connect again as breadcrumbs, because nearly all of them are dropouts over in seconds. Without
/// this, an outage that began after the hub had connected would send nothing at all.
/// </remarks>
[TestClass]
public sealed class ReportingRetryPolicyTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(2);

    private sealed class FixedPolicy(TimeSpan? delay) : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => delay;
    }

    private static RetryContext Attempt(long previous, TimeSpan elapsed)
        => new() { PreviousRetryCount = previous, ElapsedTime = elapsed, RetryReason = new TimeoutException("Server timeout") };

    [TestMethod]
    public void AReconnectThatOutlastsTheThreshold_IsReportedOnce()
    {
        var reports = 0;
        var policy = new ReportingRetryPolicy(new FixedPolicy(TimeSpan.FromSeconds(10)), Threshold, _ => reports++);

        policy.NextRetryDelay(Attempt(0, TimeSpan.Zero));
        policy.NextRetryDelay(Attempt(1, TimeSpan.FromSeconds(30)));
        policy.NextRetryDelay(Attempt(9, Threshold));
        policy.NextRetryDelay(Attempt(10, Threshold + TimeSpan.FromMinutes(5)));

        Assert.AreEqual(1, reports, "Once when the hub has stayed gone, not once per attempt after that.");
    }

    [TestMethod]
    public void ADropoutOverBeforeTheThreshold_IsNotReported()
    {
        var reports = 0;
        var policy = new ReportingRetryPolicy(new FixedPolicy(TimeSpan.FromSeconds(10)), Threshold, _ => reports++);

        policy.NextRetryDelay(Attempt(0, TimeSpan.Zero));
        policy.NextRetryDelay(Attempt(1, TimeSpan.FromSeconds(30)));
        policy.NextRetryDelay(Attempt(5, Threshold - TimeSpan.FromSeconds(1)));

        Assert.AreEqual(0, reports);
    }

    [TestMethod]
    public void EachReconnect_IsReportedOnItsOwn()
    {
        // The hub came back in between, so a second outage is a second report.
        var reports = 0;
        var policy = new ReportingRetryPolicy(new FixedPolicy(TimeSpan.FromSeconds(10)), Threshold, _ => reports++);

        policy.NextRetryDelay(Attempt(0, TimeSpan.Zero));
        policy.NextRetryDelay(Attempt(9, Threshold));
        policy.NextRetryDelay(Attempt(0, TimeSpan.Zero));
        policy.NextRetryDelay(Attempt(9, Threshold));

        Assert.AreEqual(2, reports);
    }

    [TestMethod]
    [DataRow(10, DisplayName = "A delay")]
    [DataRow(-1, DisplayName = "Giving up")]
    public void TheWrappedPolicy_StillDecidesTheRetries(int seconds)
    {
        TimeSpan? delay = seconds < 0 ? null : TimeSpan.FromSeconds(seconds);
        var policy = new ReportingRetryPolicy(new FixedPolicy(delay), Threshold, _ => { });

        Assert.AreEqual(delay, policy.NextRetryDelay(Attempt(0, TimeSpan.Zero)));
        Assert.AreEqual(delay, policy.NextRetryDelay(Attempt(9, Threshold)), "Reporting does not change the answer.");
    }

    [TestMethod]
    public void AReportThatFails_DoesNotEndTheRetries()
    {
        var policy = new ReportingRetryPolicy(new FixedPolicy(TimeSpan.FromSeconds(10)), Threshold,
            _ => throw new InvalidOperationException("logging failed"));

        policy.NextRetryDelay(Attempt(0, TimeSpan.Zero));

        Assert.AreEqual(TimeSpan.FromSeconds(10), policy.NextRetryDelay(Attempt(9, Threshold)));
    }

    /// <summary>A hub client that hands out the connection it builds. Building one does not connect.</summary>
    private sealed class ConnectionBuildingHubClient(ILoggerFactory loggerFactory)
        : HubClient(loggerFactory, TestViewModelFactory.CreateConfiguration(), new EventAccessCodeStore(new MockPreferencesService()))
    {
        public HubConnection Build() => GetConnection();
    }

    [TestMethod]
    public async Task TheHub_ReconnectsThroughThePolicy_AndReportsAnOutageAsItsOwnIssue()
    {
        // Nothing else says HubClient hands the policy to SignalR, and without it a hub that went down
        // in the middle of a race would report nothing. SignalR keeps the policy in a private field, so
        // if that changes this fails rather than passing without looking.
        var logs = new RecordingLoggerFactory();
        var connection = new ConnectionBuildingHubClient(logs).Build();
        try
        {
            var field = typeof(HubConnection).GetField("_reconnectPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "SignalR no longer keeps the reconnect policy where this test looks for it.");
            var policy = field.GetValue(connection) as ReportingRetryPolicy;
            Assert.IsNotNull(policy, "The hub reconnects without reporting an outage.");

            policy.NextRetryDelay(Attempt(0, TimeSpan.Zero));
            policy.NextRetryDelay(Attempt(1, TimeSpan.FromSeconds(30)));
            Assert.IsFalse(logs.Entries.Any(e => e.Level >= LogLevel.Warning), "An ordinary dropout was reported as an outage.");

            Assert.IsNotNull(policy.NextRetryDelay(Attempt(24, HubClient.ReconnectReportThreshold)), "And it still retries.");

            // Sent as an error with no exception attached, so crash reporting neither files it under the
            // lost-connection fingerprint with every dropout from every phone, nor rations it away.
            var report = logs.Entries.Single(e => e.Level >= LogLevel.Warning);
            Assert.AreEqual(LogLevel.Error, report.Level);
            Assert.IsNull(report.Exception);
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
