using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.ViewModels.Design;
using RedMist.TimingCommon.Models;
using System.Runtime.CompilerServices;

namespace RedMist.Timing.UI.Tests.ViewModels.CarDetails;

/// <summary>
/// Covers a car's details loading when the laps load fails before the control log has been looked at.
/// </summary>
/// <remarks>
/// The control log is fetched alongside the laps, and the laps are awaited first. When they threw,
/// nothing ever awaited the control log, so when that failed too - which on a bad connection it does,
/// seconds later - its exception had no observer. A faulted task nobody awaits is reported from the
/// finalizer as an unobserved task exception, and the app's global handler sends those to crash
/// reporting as unhandled: past the noise policy's grouping and rationing, once for every expanded car.
/// REDMIST-APP-10, -31 and -35 were that, and so was most of REDMIST-APP-T on 1.0.106.
/// </remarks>
[TestClass]
public sealed class DetailsViewModelPartialFailureTests
{
    private sealed class FailingEventClient(RestClientFactory factory, EventAccessCodeStore store, Exception lapsFault, Exception controlLogFault)
        : EventClient(factory, new RecordingLoggerFactory(), store)
    {
        public override Task<List<CarPosition>> LoadCarLapsAsync(int eventId, int sessionId, string carNumber)
            => Task.FromException<List<CarPosition>>(lapsFault);

        public override Task<CompetitorMetadata?> LoadCompetitorMetadataAsync(int eventId, string car)
            => Task.FromResult<CompetitorMetadata?>(null);

        public override Task<CarControlLogs?> LoadCarControlLogsAsync(int eventId, string car)
            => Task.FromException<CarControlLogs?>(controlLogFault);
    }

    private static DetailsViewModel Details(Exception lapsFault, Exception controlLogFault, ILoggerFactory logs)
    {
        var configuration = TestViewModelFactory.CreateConfiguration();
        var store = new EventAccessCodeStore(new MockPreferencesService());
        return new DetailsViewModel(new Event { EventId = 7 }, sessionId: 1, carNumber: "42",
            new FailingEventClient(new RestClientFactory(configuration), store, lapsFault, controlLogFault),
            new HubClient(new DebugLoggerFactory(), configuration, store),
            new PitTracking(), new DesignHttpClientFactory(), configuration, logs);
    }

    [TestMethod]
    public async Task AControlLogFailingBehindTheLaps_IsReportedLikeAnyOtherLoad()
    {
        // Logged by the view model rather than escaping it, so it reaches crash reporting as a handled
        // load failure that the noise policy can group with the rest of a lost connection.
        var logs = new RecordingLoggerFactory();
        var controlLogFault = new HttpRequestException("Connection failure");
        var details = Details(new HttpRequestException("Connection failure"), controlLogFault, logs);
        try
        {
            await details.Initialize();

            Assert.IsTrue(logs.Entries.Any(e => e.Level == LogLevel.Error && ReferenceEquals(e.Exception, controlLogFault)),
                "The control log's failure was never logged, so it was never observed.");
        }
        finally
        {
            details.Dispose();
        }
    }

    [TestMethod]
    public async Task AControlLogFailingBehindTheLaps_IsNeverLeftUnobserved()
    {
        // The direct statement of the bug. The finalizer is what reports an unobserved task, so the
        // collections below are what give it the chance to.
        var controlLogFault = new HttpRequestException("Connection failure");
        AggregateException? unobserved = null;
        EventHandler<UnobservedTaskExceptionEventArgs> listener = (_, e) =>
        {
            // Process-wide, so anything else's unobserved task arrives here too; only this one counts.
            if (e.Exception.Flatten().InnerExceptions.Any(x => ReferenceEquals(x, controlLogFault)))
            {
                unobserved = e.Exception;
                e.SetObserved();
            }
        };

        TaskScheduler.UnobservedTaskException += listener;
        try
        {
            await LoadAndLetGoAsync(new HttpRequestException("Connection failure"), controlLogFault);
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= listener;
        }

        Assert.IsNull(unobserved, "The control log's task faulted with nothing awaiting it.");
    }

    /// <summary>
    /// Loads and disposes a details view model in a frame of its own, so nothing in the test method
    /// keeps its tasks reachable when the collections run.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task LoadAndLetGoAsync(Exception lapsFault, Exception controlLogFault)
    {
        var details = Details(lapsFault, controlLogFault, new RecordingLoggerFactory());
        try
        {
            await details.Initialize();
        }
        finally
        {
            details.Dispose();
        }
    }
}
