using RedMist.Timing.UI.ViewModels.CarDetails;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels.CarDetails;

[TestClass]
public sealed class LapsListViewModelTests
{
    private static CarPosition Lap(int lapNumber, string lapTime = "00:01:30.000", int overall = 1, int inClass = 1)
        => new()
        {
            Number = "42",
            LastLapCompleted = lapNumber,
            LastLapTime = lapTime,
            OverallPosition = overall,
            ClassPosition = inClass,
        };

    [TestMethod]
    public void UpdateLaps_AddsALapPerPosition()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1), Lap(2), Lap(3)]);

        Assert.AreEqual(3, vm.Laps.Count);
    }

    [TestMethod]
    public void UpdateLaps_OrdersNewestLapFirst()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1), Lap(3), Lap(2)]);

        CollectionAssert.AreEqual(new[] { 3, 2, 1 }, vm.Laps.Select(l => l.LapNumber).ToArray());
    }

    [TestMethod]
    public void UpdateLaps_IgnoresPositionsWithNoCompletedLap()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(0), Lap(-1), Lap(1)]);

        Assert.AreEqual(1, vm.Laps.Count);
        Assert.AreEqual(1, vm.Laps[0].LapNumber);
    }

    [TestMethod]
    public void UpdateLaps_OnAnEmptyListIsHarmless()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([]);

        Assert.AreEqual(0, vm.Laps.Count);
    }

    [TestMethod]
    public void UpdateLaps_ReplacesALapReportedTwice()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, "00:01:30.000")]);
        vm.UpdateLaps([Lap(1, "00:01:25.000")]);

        Assert.AreEqual(1, vm.Laps.Count);
        Assert.AreEqual("1:25.000", vm.Laps[0].LapTime);
    }

    [TestMethod]
    public void UpdateLaps_MarksTheFastestLap()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, "00:01:30.000"), Lap(2, "00:01:25.000"), Lap(3, "00:01:28.000")]);

        var best = vm.Laps.Single(l => l.IsBestLap);
        Assert.AreEqual(2, best.LapNumber);
    }

    [TestMethod]
    public void UpdateLaps_MovesTheBestLapMarkerWhenAFasterLapArrives()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, "00:01:30.000"), Lap(2, "00:01:25.000")]);
        vm.UpdateLaps([Lap(3, "00:01:20.000")]);

        var best = vm.Laps.Single(l => l.IsBestLap);
        Assert.AreEqual(3, best.LapNumber);
    }

    [TestMethod]
    public void UpdateLaps_IgnoresLapsWithNoRecordedTimeWhenPickingTheBest()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, string.Empty), Lap(2, "00:01:25.000")]);

        var best = vm.Laps.Single(l => l.IsBestLap);
        Assert.AreEqual(2, best.LapNumber);
    }

    [TestMethod]
    public void UpdateLaps_FlagsAPositionGainAgainstThePreviousLap()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, overall: 5), Lap(2, overall: 3)]);

        var lap2 = vm.Laps.Single(l => l.LapNumber == 2);
        Assert.IsTrue(lap2.GainedOverallPosition);
        Assert.IsFalse(lap2.LostOverallPosition);
    }

    [TestMethod]
    public void UpdateLaps_FlagsAPositionLossAgainstThePreviousLap()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, overall: 3), Lap(2, overall: 5)]);

        var lap2 = vm.Laps.Single(l => l.LapNumber == 2);
        Assert.IsTrue(lap2.LostOverallPosition);
        Assert.IsFalse(lap2.GainedOverallPosition);
    }

    [TestMethod]
    public void UpdateLaps_FlagsNeitherWhenThePositionHeld()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, overall: 3), Lap(2, overall: 3)]);

        var lap2 = vm.Laps.Single(l => l.LapNumber == 2);
        Assert.IsFalse(lap2.GainedOverallPosition);
        Assert.IsFalse(lap2.LostOverallPosition);
    }

    [TestMethod]
    public void UpdateLaps_LeavesTheFirstLapWithNoGainOrLoss()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, overall: 5), Lap(2, overall: 3)]);

        var lap1 = vm.Laps.Single(l => l.LapNumber == 1);
        Assert.IsFalse(lap1.GainedOverallPosition);
        Assert.IsFalse(lap1.LostOverallPosition);
        Assert.IsFalse(lap1.GainedClassPosition);
        Assert.IsFalse(lap1.LostClassPosition);
    }

    [TestMethod]
    public void UpdateLaps_TracksClassPositionSeparatelyFromOverall()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, overall: 5, inClass: 2), Lap(2, overall: 5, inClass: 1)]);

        var lap2 = vm.Laps.Single(l => l.LapNumber == 2);
        Assert.IsTrue(lap2.GainedClassPosition);
        Assert.IsFalse(lap2.GainedOverallPosition);
    }

    [TestMethod]
    public void UpdateLaps_ComputesGainAndLossForALapArrivingOnItsOwn()
    {
        // The live path: CarViewModel pushes a single lap as each one completes, which takes the
        // partial-recompute branch rather than the full rebuild.
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, overall: 5), Lap(2, overall: 5)]);

        vm.UpdateLaps([Lap(3, overall: 2)]);

        var lap3 = vm.Laps.Single(l => l.LapNumber == 3);
        Assert.IsTrue(lap3.GainedOverallPosition);
        Assert.IsFalse(lap3.LostOverallPosition);
    }

    [TestMethod]
    public void UpdateLaps_ComputesGainAndLossAcrossAGapInLapNumbers()
    {
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, overall: 5), Lap(2, overall: 5)]);

        vm.UpdateLaps([Lap(5, overall: 9)]);

        var lap5 = vm.Laps.Single(l => l.LapNumber == 5);
        Assert.IsTrue(lap5.LostOverallPosition);
    }

    [TestMethod]
    public void UpdateLaps_RecomputesGainAndLossWhenAnEarlierLapIsCorrected()
    {
        // Laps arrive incrementally during a session; a late correction to lap 1 has to be
        // reflected in lap 2's gain/loss marker.
        var vm = new LapsListViewModel();
        vm.UpdateLaps([Lap(1, overall: 3), Lap(2, overall: 5)]);
        Assert.IsTrue(vm.Laps.Single(l => l.LapNumber == 2).LostOverallPosition);

        vm.UpdateLaps([Lap(1, overall: 8)]);

        Assert.IsTrue(vm.Laps.Single(l => l.LapNumber == 2).GainedOverallPosition);
    }
}
