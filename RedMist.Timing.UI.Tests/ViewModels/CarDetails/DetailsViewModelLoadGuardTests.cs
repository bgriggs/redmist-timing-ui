using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.ViewModels.Design;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels.CarDetails;

/// <summary>
/// Covers a car's details panel keeping one load out at a time.
/// </summary>
/// <remarks>
/// A load is the laps, the competitor metadata and the control log together, and an app resume starts
/// one. Nothing used to stop one starting while the last was still out, so on a stalled connection
/// each resume sent a whole set of requests alongside the ones still waiting. REDMIST-APP-33, -34 and
/// -35 are one viewer's phone with car 722 open: about five laps loads and four metadata loads refused
/// by the server's rate limit in the same ten milliseconds, twice. What produced that many is not
/// established, and this guard is per panel - a car collapsed and expanded again gets a new one - so
/// these pin what the panel does, not that the incident cannot recur.
/// </remarks>
[TestClass]
public sealed class DetailsViewModelLoadGuardTests
{
    /// <summary>An event client whose laps and metadata calls can each be held open.</summary>
    private sealed class GatedEventClient(RestClientFactory factory, EventAccessCodeStore store)
        : EventClient(factory, new RecordingLoggerFactory(), store)
    {
        private readonly TaskCompletionSource lapsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource metadataGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource lapsEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource metadataEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int lapLoads;
        private int metadataLoads;

        public int LapLoads => Volatile.Read(ref lapLoads);
        public int MetadataLoads => Volatile.Read(ref metadataLoads);
        public Task LapsEntered => lapsEntered.Task;
        public Task MetadataEntered => metadataEntered.Task;
        public bool Fails { get; set; }

        public override async Task<List<CarPosition>> LoadCarLapsAsync(int eventId, int sessionId, string carNumber)
        {
            Interlocked.Increment(ref lapLoads);
            lapsEntered.TrySetResult();
            await lapsGate.Task;
            return Fails ? throw new HttpRequestException("Connection failure") : [];
        }

        public override async Task<CompetitorMetadata?> LoadCompetitorMetadataAsync(int eventId, string car)
        {
            Interlocked.Increment(ref metadataLoads);
            metadataEntered.TrySetResult();
            await metadataGate.Task;
            return null;
        }

        public override Task<CarControlLogs?> LoadCarControlLogsAsync(int eventId, string car)
            => Task.FromResult<CarControlLogs?>(null);

        public void ReleaseLaps() => lapsGate.TrySetResult();
        public void ReleaseMetadata() => metadataGate.TrySetResult();

        public void ReleaseAll()
        {
            ReleaseLaps();
            ReleaseMetadata();
        }
    }

    private static (DetailsViewModel Details, GatedEventClient Server) Details()
    {
        var configuration = TestViewModelFactory.CreateConfiguration();
        var store = new EventAccessCodeStore(new MockPreferencesService());
        var server = new GatedEventClient(new RestClientFactory(configuration), store);
        var details = new DetailsViewModel(new Event { EventId = 7 }, sessionId: 1, carNumber: "722", server,
            new HubClient(new DebugLoggerFactory(), configuration, store),
            new PitTracking(), new DesignHttpClientFactory(), configuration, new RecordingLoggerFactory());
        return (details, server);
    }

    /// <summary>
    /// Awaits a step against a timeout, so a guard that never lets go - or never lets a load start -
    /// fails the test instead of hanging it.
    /// </summary>
    private static async Task WithinAsync(Task task, string failure = "The load never finished.")
    {
        Assert.AreSame(task, await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))), failure);
        await task;
    }

    [TestMethod]
    public async Task LoadsAskedForDuringALoad_ComeDownToOneMore()
    {
        var (details, server) = Details();
        try
        {
            var first = details.Initialize();
            await WithinAsync(server.LapsEntered, "No load reached the server.");
            server.ReleaseMetadata();

            // Against a timeout rather than awaited outright: without the guard these would each send a
            // load of their own and wait on the same held gate, and the test would hang instead of
            // saying what is wrong.
            var askedDuring = Task.WhenAll(details.Initialize(), details.Initialize(), details.Initialize());
            var settled = await Task.WhenAny(askedDuring, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.AreSame(askedDuring, settled, "A load asked for mid-load must hand back at once.");
            Assert.AreEqual(1, server.LapLoads, "Nothing may go out alongside the load still waiting.");

            server.ReleaseLaps();
            await WithinAsync(first);

            Assert.AreEqual(2, server.LapLoads,
                "The three that asked are served by one more load afterwards - not one each, and not none, " +
                "because the load they arrived during might have been the one about to fail.");
        }
        finally
        {
            server.ReleaseAll();
            details.Dispose();
        }
    }

    [TestMethod]
    public async Task AnAppResumeDuringALoad_IsServedOnceItFinishes()
    {
        // The path the incident is thought to have taken: a resume arriving while a load was still out.
        var (details, server) = Details();
        try
        {
            var first = details.Initialize();
            await WithinAsync(server.LapsEntered, "No load reached the server.");
            server.ReleaseMetadata();

            // The resume records its request and hands back before its first await, so there is
            // nothing to wait for before looking.
            details.Receive(new AppResumeNotification());
            Assert.AreEqual(1, server.LapLoads, "Not alongside the load still out.");

            server.ReleaseLaps();
            await WithinAsync(first);

            Assert.AreEqual(2, server.LapLoads, "But after it.");
        }
        finally
        {
            server.ReleaseAll();
            details.Dispose();
        }
    }

    [TestMethod]
    public async Task MetadataStillOut_KeepsTheLoadOpen()
    {
        // The panel stops showing as loading once the laps are in, but the metadata request is part of
        // the same load. Letting go of the guard before it answered let the next load send another.
        var (details, server) = Details();
        try
        {
            // Laps released first, so the whole load apart from the metadata has finished by the time
            // the first call hands back.
            server.ReleaseLaps();
            var first = details.Initialize();
            await WithinAsync(server.MetadataEntered, "No load reached the server.");
            Assert.IsFalse(first.IsCompleted, "The load handed back with its metadata request still out.");

            var second = details.Initialize();
            var settled = await Task.WhenAny(second, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.AreSame(second, settled, "Asked for while the metadata was out, so it waits its turn.");
            Assert.AreEqual(1, server.LapLoads, "A second load went out beside the first.");
            Assert.AreEqual(1, server.MetadataLoads, "A second metadata request went out beside the first.");

            server.ReleaseMetadata();
            await WithinAsync(first);

            Assert.AreEqual(2, server.MetadataLoads, "And it was served once the first answered.");
        }
        finally
        {
            server.ReleaseAll();
            details.Dispose();
        }
    }

    [TestMethod]
    public async Task AReloadOwedWhenThePanelCloses_IsNeverRun()
    {
        // A closed panel sending a load would also subscribe to its car's control log again, taking
        // HubClient's single slot back from whichever car was opened since.
        var (details, server) = Details();
        try
        {
            var first = details.Initialize();
            await WithinAsync(server.LapsEntered, "No load reached the server.");
            server.ReleaseMetadata();
            details.Receive(new AppResumeNotification());

            details.Dispose();
            server.ReleaseLaps();
            await WithinAsync(first);

            Assert.AreEqual(1, server.LapLoads, "The reload that was owed ran on a closed panel.");
        }
        finally
        {
            server.ReleaseAll();
            details.Dispose();
        }
    }

    [TestMethod]
    [DataRow(false, DisplayName = "After a load that succeeded")]
    [DataRow(true, DisplayName = "After a load that failed")]
    public async Task OnceALoadFinishes_TheNextIsLetThrough(bool fails)
    {
        // The guard has to come off once a load is over, whether it worked or failed, or the panel
        // never reloads again - not on a resume, and not after the connection that failed the first
        // load comes back.
        var (details, server) = Details();
        try
        {
            server.Fails = fails;
            server.ReleaseAll();
            await WithinAsync(details.Initialize());

            await WithinAsync(details.Initialize());

            Assert.AreEqual(2, server.LapLoads);
        }
        finally
        {
            details.Dispose();
        }
    }
}
