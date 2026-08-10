using RedMist.Timing.UI.ViewModels.CarDetails;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels.CarDetails;

[TestClass]
public sealed class ChartViewModelTests
{
    private const int VisibleLapWindow = 23;

    private static CarPosition Lap(int lapNumber, string lapTime = "00:01:30.000", bool pitted = false)
        => new()
        {
            Number = "42",
            LastLapCompleted = lapNumber,
            LastLapTime = lapTime,
            LapIncludedPit = pitted,
        };

    // A series that has never been given values leaves Values null, so treat that as "no laps".
    private static LapViewModel[] SeriesLaps(ChartViewModel vm, int seriesIndex)
        => ((IEnumerable<LapViewModel>?)vm.Series[seriesIndex].Values)?.ToArray() ?? [];

    private static LapViewModel[] LapSeries(ChartViewModel vm) => SeriesLaps(vm, 0);

    private static LapViewModel[] PitSeries(ChartViewModel vm) => SeriesLaps(vm, 3);

    [TestMethod]
    public void UpdateLaps_PublishesALapPerPosition()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1), Lap(2), Lap(3)]);

        Assert.AreEqual(3, LapSeries(vm).Length);
    }

    [TestMethod]
    public void UpdateLaps_FillsGapsSoTheChartHasNoHoles()
    {
        // Laps can be missed; the column series needs a contiguous 1..max range to plot against.
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1), Lap(5)]);

        var laps = LapSeries(vm);
        Assert.AreEqual(5, laps.Length);
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, laps.Select(l => l.LapNumber).ToArray());
    }

    [TestMethod]
    public void UpdateLaps_IgnoresPositionsWithNoCompletedLap()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(0), Lap(-3), Lap(2)]);

        // Lap 2 is real; lap 1 is filled in behind it.
        Assert.AreEqual(2, LapSeries(vm).Length);
    }

    [TestMethod]
    public void UpdateLaps_OnAnEmptyListIsHarmless()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([]);

        Assert.AreEqual(0, LapSeries(vm).Length);
    }

    [TestMethod]
    public void UpdateLaps_PlotsOnlyPitLapsOnThePitSeries()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1), Lap(2, pitted: true), Lap(3)]);

        CollectionAssert.AreEqual(new[] { 2 }, PitSeries(vm).Select(l => l.LapNumber).ToArray());
    }

    [TestMethod]
    public void UpdateLaps_CountsLapsSinceTheLastPitStop()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1), Lap(2), Lap(3, pitted: true), Lap(4), Lap(5)]);

        var laps = LapSeries(vm);
        Assert.AreEqual("1", laps[0].LapsSinceLastPit);
        Assert.AreEqual("2", laps[1].LapsSinceLastPit);
        Assert.AreEqual("1", laps[3].LapsSinceLastPit, "The count restarts on the lap after the stop.");
        Assert.AreEqual("2", laps[4].LapsSinceLastPit);
    }

    [TestMethod]
    public void UpdateLaps_AccumulatesMinutesSinceTheLastPitStop()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1, "00:02:00.000"), Lap(2, "00:02:00.000"), Lap(3, "00:02:00.000", pitted: true), Lap(4, "00:03:00.000")]);

        var laps = LapSeries(vm);
        Assert.AreEqual("2", laps[0].MinutesSinceLastPit);
        Assert.AreEqual("4", laps[1].MinutesSinceLastPit);
        Assert.AreEqual("3", laps[3].MinutesSinceLastPit, "The running total restarts after the stop.");
    }

    [TestMethod]
    public void UpdateLaps_LeavesTheDefaultWindowForAShortSession()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1), Lap(2), Lap(3)]);

        Assert.AreEqual(0d, vm.XAxes[0].MinLimit);
        Assert.AreEqual((double)VisibleLapWindow, vm.XAxes[0].MaxLimit);
    }

    [TestMethod]
    public void UpdateLaps_ScrollsTheWindowToKeepTheLatestLapInView()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([.. Enumerable.Range(1, 40).Select(i => Lap(i))]);

        Assert.AreEqual(40d - VisibleLapWindow, vm.XAxes[0].MinLimit);
        Assert.AreEqual(41d, vm.XAxes[0].MaxLimit);
    }

    [TestMethod]
    public void UpdateLaps_RefreshesPitMarkersWhenALapIsRestatedAsAPitLap()
    {
        // The lap count is unchanged here, so this only passes if the pit series is republished
        // regardless of the count guard.
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1), Lap(2)]);
        Assert.AreEqual(0, PitSeries(vm).Length);

        vm.UpdateLaps([Lap(2, pitted: true)]);

        CollectionAssert.AreEqual(new[] { 2 }, PitSeries(vm).Select(l => l.LapNumber).ToArray());
    }

    [TestMethod]
    public void UpdateLaps_KeepsTheThreeLapSeriesOnTheSameLapSet()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1), Lap(2)]);

        Assert.AreEqual(2, SeriesLaps(vm, 0).Length);
        Assert.AreEqual(2, SeriesLaps(vm, 1).Length);
        Assert.AreEqual(2, SeriesLaps(vm, 2).Length);
    }

    [TestMethod]
    public void UpdateLaps_AccumulatesAcrossCalls()
    {
        var vm = new ChartViewModel();
        vm.UpdateLaps([Lap(1), Lap(2)]);
        vm.UpdateLaps([Lap(3)]);

        Assert.AreEqual(3, LapSeries(vm).Length);
    }
}
