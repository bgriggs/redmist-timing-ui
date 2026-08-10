using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

[TestClass]
public sealed class FlagViewModelTests
{
    private static FlagDuration Duration(Flags flag, DateTime start, DateTime? end = null)
        => new() { Flag = flag, StartTime = start, EndTime = end };

    [TestMethod]
    public void Update_FormatsStartAndEndAsLocalClockTimes()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 14, 5, 0), new DateTime(2024, 3, 15, 15, 30, 0)), null);

        Assert.AreEqual("2:05 PM", vm.StartTime);
        Assert.AreEqual("3:30 PM", vm.EndTime);
    }

    [TestMethod]
    public void Update_LeavesEndTimeBlankWhileTheFlagIsStillRunning()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Yellow, new DateTime(2024, 3, 15, 14, 5, 0)), null);

        Assert.AreEqual(string.Empty, vm.EndTime);
    }

    [TestMethod]
    public void Update_ComputesDurationFromStartAndEnd()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Yellow, new DateTime(2024, 3, 15, 14, 0, 0), new DateTime(2024, 3, 15, 14, 3, 20)), null);

        Assert.AreEqual("3m 20s", vm.Duration);
    }

    [TestMethod]
    public void Update_IncludesHoursInLongDurations()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 9, 0, 0), new DateTime(2024, 3, 15, 11, 34, 56)), null);

        Assert.AreEqual("2h 34m 56s", vm.Duration);
    }

    [TestMethod]
    public void Update_OmitsMinutesForShortDurations()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Red, new DateTime(2024, 3, 15, 9, 0, 0), new DateTime(2024, 3, 15, 9, 0, 42)), null);

        Assert.AreEqual("42s", vm.Duration);
    }

    [TestMethod]
    public void Update_ReportsSubSecondDurationsAsZero()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Red, new DateTime(2024, 3, 15, 9, 0, 0), new DateTime(2024, 3, 15, 9, 0, 0, 400)), null);

        Assert.AreEqual("0s", vm.Duration);
    }

    [TestMethod]
    public void Update_RunsTheDurationAgainstTimeOfDayForTheCurrentFlag()
    {
        // Only the newest flag gets a moving duration, driven by the session's time of day.
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 14, 0, 0)),
            trackTimeOfDay: new TimeSpan(14, 12, 30), setMovingDuration: true);

        Assert.AreEqual("12m 30s", vm.Duration);
    }

    [TestMethod]
    public void Update_ClearsDurationForAnOpenFlagThatIsNotTheCurrentOne()
    {
        var vm = PooledRowShowing("5m 0s");
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 14, 0, 0)),
            trackTimeOfDay: new TimeSpan(14, 12, 30), setMovingDuration: false);

        Assert.AreEqual(string.Empty, vm.Duration);
    }

    [TestMethod]
    public void Update_ClearsDurationWhenTimeOfDayIsUnknown()
    {
        var vm = PooledRowShowing("5m 0s");
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 14, 0, 0)), null, setMovingDuration: true);

        Assert.AreEqual(string.Empty, vm.Duration);
    }

    /// <summary>
    /// A row already showing a duration, as it would be after being pooled and reused. A fresh
    /// view model starts blank, so asserting on one proves nothing about Update clearing it.
    /// </summary>
    private static FlagViewModel PooledRowShowing(string expectedDuration)
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Red, new DateTime(2024, 3, 15, 9, 0, 0), new DateTime(2024, 3, 15, 9, 5, 0)), null);
        Assert.AreEqual(expectedDuration, vm.Duration, "Test setup did not produce the expected starting state.");
        return vm;
    }

    [TestMethod]
    public void Update_ReportsDurationsLongerThanADayInHours()
    {
        // A green flag in a 24-hour race can run past a day; TimeSpan.Hours would wrap to 0.
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 9, 0, 0), new DateTime(2024, 3, 16, 9, 5, 30)), null);

        Assert.AreEqual("24h 5m 30s", vm.Duration);
    }

    [TestMethod]
    public void Update_MeasuresTheRunningFlagAgainstTheTrackDayNotTheViewerDay()
    {
        // The flag started on 9 August at the track. The clock reads 14:12:30 there. A viewer
        // whose own calendar date is anything else must still see 12m 30s.
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 14, 0, 0)),
            trackTimeOfDay: new TimeSpan(14, 12, 30), setMovingDuration: true);

        Assert.AreEqual("12m 30s", vm.Duration);
    }

    [TestMethod]
    public void Update_RollsForwardWhenTheSessionHasPassedMidnightAtTheTrack()
    {
        // Green at 22:00, clock now reads 01:30 - the next day at the track, so 3h 30m.
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 22, 0, 0)),
            trackTimeOfDay: new TimeSpan(1, 30, 0), setMovingDuration: true);

        Assert.AreEqual("3h 30m 0s", vm.Duration);
    }

    [TestMethod]
    public void Update_IsUnaffectedByTheFlagsCalendarDate()
    {
        // Same clock reading and same elapsed time, a year apart - the viewer's "today" plays no
        // part in the arithmetic.
        var early = new FlagViewModel();
        early.Update(Duration(Flags.Yellow, new DateTime(2025, 1, 1, 9, 0, 0)),
            trackTimeOfDay: new TimeSpan(9, 4, 0), setMovingDuration: true);

        var late = new FlagViewModel();
        late.Update(Duration(Flags.Yellow, new DateTime(2026, 12, 31, 9, 0, 0)),
            trackTimeOfDay: new TimeSpan(9, 4, 0), setMovingDuration: true);

        Assert.AreEqual("4m 0s", early.Duration);
        Assert.AreEqual(early.Duration, late.Duration);
    }

    [TestMethod]
    public void Update_ExposesTheFlagAndItsName()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Checkered, new DateTime(2024, 3, 15, 14, 0, 0)), null);

        Assert.AreEqual(Flags.Checkered, vm.Flag);
        Assert.AreEqual("Checkered", vm.FlagStr);
    }

    [TestMethod]
    public void Update_BlanksTheNameForAnUnknownFlag()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Unknown, new DateTime(2024, 3, 15, 14, 0, 0)), null);

        Assert.AreEqual(string.Empty, vm.FlagStr);
    }

    [TestMethod]
    public void Update_ReplacesPreviousValuesWhenReused()
    {
        // The view models are pooled and re-pointed at whichever flag now occupies that row.
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Red, new DateTime(2024, 3, 15, 9, 0, 0), new DateTime(2024, 3, 15, 9, 5, 0)), null);
        vm.Update(Duration(Flags.Green, new DateTime(2024, 3, 15, 10, 0, 0)), null);

        Assert.AreEqual(Flags.Green, vm.Flag);
        Assert.AreEqual("10:00 AM", vm.StartTime);
        Assert.AreEqual(string.Empty, vm.EndTime);
        Assert.AreEqual(string.Empty, vm.Duration);
    }
}
