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
        vm.Update(Duration(Flags.Green, new DateTime(2026, 8, 9, 14, 5, 0), new DateTime(2026, 8, 9, 15, 30, 0)), default);

        Assert.AreEqual("2:05 PM", vm.StartTime);
        Assert.AreEqual("3:30 PM", vm.EndTime);
    }

    [TestMethod]
    public void Update_LeavesEndTimeBlankWhileTheFlagIsStillRunning()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Yellow, new DateTime(2026, 8, 9, 14, 5, 0)), default);

        Assert.AreEqual(string.Empty, vm.EndTime);
    }

    [TestMethod]
    public void Update_ComputesDurationFromStartAndEnd()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Yellow, new DateTime(2026, 8, 9, 14, 0, 0), new DateTime(2026, 8, 9, 14, 3, 20)), default);

        Assert.AreEqual("3m 20s", vm.Duration);
    }

    [TestMethod]
    public void Update_IncludesHoursInLongDurations()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2026, 8, 9, 9, 0, 0), new DateTime(2026, 8, 9, 11, 34, 56)), default);

        Assert.AreEqual("2h 34m 56s", vm.Duration);
    }

    [TestMethod]
    public void Update_OmitsMinutesForShortDurations()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Red, new DateTime(2026, 8, 9, 9, 0, 0), new DateTime(2026, 8, 9, 9, 0, 42)), default);

        Assert.AreEqual("42s", vm.Duration);
    }

    [TestMethod]
    public void Update_ReportsSubSecondDurationsAsZero()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Red, new DateTime(2026, 8, 9, 9, 0, 0), new DateTime(2026, 8, 9, 9, 0, 0, 400)), default);

        Assert.AreEqual("0s", vm.Duration);
    }

    [TestMethod]
    public void Update_RunsTheDurationAgainstTimeOfDayForTheCurrentFlag()
    {
        // Only the newest flag gets a moving duration, driven by the session's time of day.
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2026, 8, 9, 14, 0, 0)),
            timeOfDay: new DateTime(2026, 8, 9, 14, 12, 30), setMovingDuration: true);

        Assert.AreEqual("12m 30s", vm.Duration);
    }

    [TestMethod]
    public void Update_ClearsDurationForAnOpenFlagThatIsNotTheCurrentOne()
    {
        var vm = PooledRowShowing("5m 0s");
        vm.Update(Duration(Flags.Green, new DateTime(2026, 8, 9, 14, 0, 0)),
            timeOfDay: new DateTime(2026, 8, 9, 14, 12, 30), setMovingDuration: false);

        Assert.AreEqual(string.Empty, vm.Duration);
    }

    [TestMethod]
    public void Update_ClearsDurationWhenTimeOfDayIsUnknown()
    {
        var vm = PooledRowShowing("5m 0s");
        vm.Update(Duration(Flags.Green, new DateTime(2026, 8, 9, 14, 0, 0)), default, setMovingDuration: true);

        Assert.AreEqual(string.Empty, vm.Duration);
    }

    /// <summary>
    /// A row already showing a duration, as it would be after being pooled and reused. A fresh
    /// view model starts blank, so asserting on one proves nothing about Update clearing it.
    /// </summary>
    private static FlagViewModel PooledRowShowing(string expectedDuration)
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Red, new DateTime(2026, 8, 9, 9, 0, 0), new DateTime(2026, 8, 9, 9, 5, 0)), default);
        Assert.AreEqual(expectedDuration, vm.Duration, "Test setup did not produce the expected starting state.");
        return vm;
    }

    [TestMethod]
    public void Update_ReportsDurationsLongerThanADayInHours()
    {
        // A green flag in a 24-hour race can run past a day; TimeSpan.Hours would wrap to 0.
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Green, new DateTime(2026, 8, 9, 9, 0, 0), new DateTime(2026, 8, 10, 9, 5, 30)), default);

        Assert.AreEqual("24h 5m 30s", vm.Duration);
    }

    [TestMethod]
    public void Update_ExposesTheFlagAndItsName()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Checkered, new DateTime(2026, 8, 9, 14, 0, 0)), default);

        Assert.AreEqual(Flags.Checkered, vm.Flag);
        Assert.AreEqual("Checkered", vm.FlagStr);
    }

    [TestMethod]
    public void Update_BlanksTheNameForAnUnknownFlag()
    {
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Unknown, new DateTime(2026, 8, 9, 14, 0, 0)), default);

        Assert.AreEqual(string.Empty, vm.FlagStr);
    }

    [TestMethod]
    public void Update_ReplacesPreviousValuesWhenReused()
    {
        // The view models are pooled and re-pointed at whichever flag now occupies that row.
        var vm = new FlagViewModel();
        vm.Update(Duration(Flags.Red, new DateTime(2026, 8, 9, 9, 0, 0), new DateTime(2026, 8, 9, 9, 5, 0)), default);
        vm.Update(Duration(Flags.Green, new DateTime(2026, 8, 9, 10, 0, 0)), default);

        Assert.AreEqual(Flags.Green, vm.Flag);
        Assert.AreEqual("10:00 AM", vm.StartTime);
        Assert.AreEqual(string.Empty, vm.EndTime);
        Assert.AreEqual(string.Empty, vm.Duration);
    }
}
