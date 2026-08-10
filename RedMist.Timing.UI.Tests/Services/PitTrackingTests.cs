using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using System.Collections.Concurrent;

namespace RedMist.Timing.UI.Tests.Services;

[TestClass]
public sealed class PitTrackingTests
{
    [TestMethod]
    public void ApplyPitStop_FlagsTheRecordedLap()
    {
        var tracking = new PitTracking();
        tracking.AddPitStop("5", 3);

        var positions = new List<CarPosition> { new() { Number = "5", LastLapCompleted = 3 } };
        tracking.ApplyPitStop(positions);

        Assert.IsTrue(positions[0].LapIncludedPit);
    }

    [TestMethod]
    public void ApplyPitStop_LeavesOtherLapsOfTheSameCarAlone()
    {
        var tracking = new PitTracking();
        tracking.AddPitStop("5", 3);

        var positions = new List<CarPosition>
        {
            new() { Number = "5", LastLapCompleted = 2 },
            new() { Number = "5", LastLapCompleted = 4 },
        };
        tracking.ApplyPitStop(positions);

        Assert.IsFalse(positions[0].LapIncludedPit);
        Assert.IsFalse(positions[1].LapIncludedPit);
    }

    [TestMethod]
    public void ApplyPitStop_DoesNotLeakAcrossCarNumbers()
    {
        var tracking = new PitTracking();
        tracking.AddPitStop("5", 3);

        var positions = new List<CarPosition> { new() { Number = "6", LastLapCompleted = 3 } };
        tracking.ApplyPitStop(positions);

        Assert.IsFalse(positions[0].LapIncludedPit);
    }

    [TestMethod]
    public void ApplyPitStop_TracksMultipleLapsPerCar()
    {
        var tracking = new PitTracking();
        tracking.AddPitStop("5", 3);
        tracking.AddPitStop("5", 9);

        var positions = new List<CarPosition>
        {
            new() { Number = "5", LastLapCompleted = 3 },
            new() { Number = "5", LastLapCompleted = 9 },
        };
        tracking.ApplyPitStop(positions);

        Assert.IsTrue(positions[0].LapIncludedPit);
        Assert.IsTrue(positions[1].LapIncludedPit);
    }

    [TestMethod]
    public void ApplyPitStop_IgnoresPositionsWithNoCarNumber()
    {
        // Seed the blank key too, so the assertion depends on the guard rather than on a
        // lookup that would have missed anyway.
        var tracking = new PitTracking();
        tracking.AddPitStop(string.Empty, 3);

        var positions = new List<CarPosition> { new() { Number = string.Empty, LastLapCompleted = 3 } };
        tracking.ApplyPitStop(positions);

        Assert.IsFalse(positions[0].LapIncludedPit);
    }

    [TestMethod]
    public void ApplyPitStop_ToleratesAPositionWithANullCarNumber()
    {
        var tracking = new PitTracking();
        tracking.AddPitStop("5", 3);

        var positions = new List<CarPosition> { new() { Number = null!, LastLapCompleted = 3 } };
        tracking.ApplyPitStop(positions);

        Assert.IsFalse(positions[0].LapIncludedPit);
    }

    [TestMethod]
    public void Clear_DropsRecordedStops()
    {
        var tracking = new PitTracking();
        tracking.AddPitStop("5", 3);
        tracking.Clear();

        var positions = new List<CarPosition> { new() { Number = "5", LastLapCompleted = 3 } };
        tracking.ApplyPitStop(positions);

        Assert.IsFalse(positions[0].LapIncludedPit);
    }

    /// <summary>
    /// The instance is shared between the UI thread (which records and reads stops) and the
    /// background task that clears it when a live event starts. Before it was synchronized, the
    /// unguarded Dictionary could be torn by concurrent writes - which surfaces as a corrupted
    /// lookup or a fault inside the runtime rather than a clean exception.
    /// </summary>
    [TestMethod]
    public void ConcurrentAccess_DoesNotCorruptTheStore()
    {
        var tracking = new PitTracking();
        var failures = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim();
        const int Iterations = 40_000;

        void Hammer(Action body)
        {
            try
            {
                start.Wait();
                for (int i = 0; i < Iterations; i++)
                {
                    body();
                }
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        }

        static Task Run(Action body) =>
            Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        // Adding a wide spread of distinct keys forces repeated internal resizes, which is what
        // actually tears an unsynchronized Dictionary out from under a concurrent reader.
        var growers = Enumerable.Range(0, 3)
            .Select(t => Run(() =>
            {
                int i = 0;
                Hammer(() => tracking.AddPitStop($"car-{t}-{i++}", 1));
            }))
            .ToArray();

        var readers = Enumerable.Range(0, 3)
            .Select(t => Run(() => Hammer(() => tracking.ApplyPitStop(
                [new CarPosition { Number = $"car-{t}-7", LastLapCompleted = 1 }]))))
            .ToArray();

        var clearer = Run(() => Hammer(tracking.Clear));

        var all = growers.Concat(readers).Append(clearer).ToArray();
        start.Set();

        Assert.IsTrue(Task.WaitAll(all, TimeSpan.FromSeconds(60)),
            "Concurrent access did not complete - a torn dictionary can spin forever inside a lookup.");
        Assert.IsTrue(failures.IsEmpty,
            $"Concurrent access threw: {string.Join("; ", failures.Select(e => $"{e.GetType().Name}: {e.Message}"))}");
    }
}
