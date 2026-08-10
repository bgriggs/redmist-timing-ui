using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers the values <see cref="CarViewModel"/> derives for the timing grid: positions, penalties,
/// the lap progress bar, and the shortened lap times.
/// </summary>
[TestClass]
public sealed class CarViewModelDisplayTests
{
    private const string CarNumber = "5";

    private static CarViewModel CreateCar() => TestViewModelFactory.CreateCar();

    #region Position

    [TestMethod]
    public void SortablePosition_NoPositionYet_SortsToTheEnd()
    {
        var car = CreateCar();
        car.OverallPosition = 0;

        // Cars that have not taken a position yet would otherwise sort to the front of the field.
        Assert.AreEqual(9999, car.SortablePosition);
    }

    [TestMethod]
    public void SortablePosition_UsesTheOverallPosition()
    {
        var car = CreateCar();
        car.OverallPosition = 7;

        Assert.AreEqual(7, car.SortablePosition);
    }

    [TestMethod]
    public void OverridePosition_TakesPrecedenceOverTheOverallPosition()
    {
        var car = CreateCar();
        car.OverallPosition = 7;

        car.OverridePosition(2);

        Assert.AreEqual(2, car.SortablePosition);
        Assert.AreEqual(2, car.Position);
    }

    [TestMethod]
    public void OverridePosition_Cleared_RevertsToTheOverallPosition()
    {
        var car = CreateCar();
        car.OverallPosition = 7;
        car.OverridePosition(2);

        car.OverridePosition(null);

        Assert.AreEqual(7, car.SortablePosition);
        Assert.AreEqual(7, car.Position);
    }

    [TestMethod]
    public void OverridePosition_ZeroIsAnOverrideNotAnAbsentPosition()
    {
        var car = CreateCar();
        car.OverallPosition = 7;

        car.OverridePosition(0);

        Assert.AreEqual(0, car.SortablePosition, "Zero is a real override and must not fall through to the 9999 sentinel.");
    }

    [TestMethod]
    public void Position_FollowsTheGrouping()
    {
        var car = CreateCar();
        car.OverallPosition = 7;
        car.ClassPosition = 2;

        car.CurrentGroupMode = GroupMode.Overall;
        Assert.AreEqual(7, car.Position);

        car.CurrentGroupMode = GroupMode.Class;
        Assert.AreEqual(2, car.Position);
    }

    [TestMethod]
    public void OverridePosition_Changed_NotifiesPositionAndSortablePosition()
    {
        var car = CreateCar();
        var changed = new List<string?>();
        car.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        car.OverridePosition(3);

        CollectionAssert.Contains(changed, nameof(CarViewModel.Position));
        CollectionAssert.Contains(changed, nameof(CarViewModel.SortablePosition));
    }

    [TestMethod]
    public void OverridePosition_SetToSameValue_DoesNotNotify()
    {
        var car = CreateCar();
        car.OverridePosition(3);
        var changed = new List<string?>();
        car.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        car.OverridePosition(3);

        CollectionAssert.DoesNotContain(changed, nameof(CarViewModel.Position));
        CollectionAssert.DoesNotContain(changed, nameof(CarViewModel.SortablePosition));
    }

    #endregion

    #region Positions gained and lost

    [TestMethod]
    public void PositionsGainedLost_FollowsTheGrouping()
    {
        var car = CreateCar();
        car.OverallPositionsGained = 4;
        car.InClassPositionsGained = -1;

        car.CurrentGroupMode = GroupMode.Overall;
        Assert.AreEqual(4, car.PositionsGainedLost.RawPositionChange);

        car.CurrentGroupMode = GroupMode.Class;
        Assert.AreEqual(-1, car.PositionsGainedLost.RawPositionChange);
    }

    [TestMethod]
    public void IsMostPositionsGained_FollowsTheGrouping()
    {
        var car = CreateCar();
        car.IsOverallMostPositionsGained = true;
        car.IsClassMostPositionsGained = false;

        car.CurrentGroupMode = GroupMode.Overall;
        Assert.IsTrue(car.IsMostPositionsGained);

        car.CurrentGroupMode = GroupMode.Class;
        Assert.IsFalse(car.IsMostPositionsGained);
    }

    [TestMethod]
    public void IsBestTime_FollowsTheGrouping()
    {
        var car = CreateCar();
        car.IsBestTimeOverall = false;
        car.IsBestTimeClass = true;

        car.CurrentGroupMode = GroupMode.Overall;
        Assert.IsFalse(car.IsBestTime);

        car.CurrentGroupMode = GroupMode.Class;
        Assert.IsTrue(car.IsBestTime);
    }

    [TestMethod]
    [DataRow(3, true, false, false, "3")]
    [DataRow(-2, false, true, false, "2")]
    [DataRow(0, false, false, true, "0")]
    public void PositionChange_ClassifiesTheChange(int raw, bool isGain, bool isLoss, bool isNoChange, string text)
    {
        var change = new PositionChange { RawPositionChange = raw };

        Assert.AreEqual(isGain, change.IsGain);
        Assert.AreEqual(isLoss, change.IsLoss);
        Assert.AreEqual(isNoChange, change.IsNoChange);
        Assert.AreEqual(text, change.PositionChangeText);
    }

    /// <summary>
    /// The sentinel means the starting position was never known, so the row shows no arrow at all.
    /// Without the guard it reads as a 999-position loss.
    /// </summary>
    [TestMethod]
    public void PositionChange_InvalidPosition_ShowsNoGainOrLoss()
    {
        var change = new PositionChange { RawPositionChange = CarPosition.InvalidPosition };

        Assert.IsFalse(change.IsGain);
        Assert.IsFalse(change.IsLoss);
        Assert.IsFalse(change.IsNoChange);
    }

    #endregion

    #region Penalties

    [TestMethod]
    [DataRow(0, false, "Laps")]
    [DataRow(1, true, "Lap")]
    [DataRow(2, true, "Laps")]
    public void PenaltyLaps_PluralizesTheTerm(int laps, bool hasPenalty, string term)
    {
        var car = CreateCar();
        car.PenaltyLaps = laps;

        Assert.AreEqual(hasPenalty, car.HasPenaltyLaps);
        Assert.AreEqual(term, car.PenaltyLapTerm);
    }

    [TestMethod]
    public void PenaltyFlags_TrackTheirCounts()
    {
        var car = CreateCar();
        Assert.IsFalse(car.HasPenaltyWarnings);
        Assert.IsFalse(car.HasPenaltyBlackFlags);

        car.PenaltyWarnings = 1;
        car.PenaltyBlackFlags = 2;

        Assert.IsTrue(car.HasPenaltyWarnings);
        Assert.IsTrue(car.HasPenaltyBlackFlags);
    }

    #endregion

    #region Name

    [TestMethod]
    public void Name_IsUpperCased()
    {
        var car = CreateCar();

        car.OriginalName = "Morgan Racing";

        Assert.AreEqual("MORGAN RACING", car.Name);
    }

    #endregion

    #region Shortened lap times

    [TestMethod]
    public void LastTimeShort_DropsTheLeadingHour()
    {
        var car = CreateCar();

        car.LastTime = "00:01:22.500";

        Assert.AreEqual("1:22.500", car.LastTimeShort);
    }

    [TestMethod]
    public void BestTimeShort_DropsTheLeadingHour()
    {
        var car = CreateCar();

        car.BestTime = "00:01:22.500";

        Assert.AreEqual("1:22.500", car.BestTimeShort);
    }

    [TestMethod]
    public void BestTimeMs_IsTheTotalMilliseconds()
    {
        var car = CreateCar();

        car.BestTime = "00:01:22.500";

        Assert.AreEqual(82500, car.BestTimeMs);
    }

    /// <summary>
    /// A car with no lap time has to sort behind every car that has one, so the sentinel is
    /// int.MaxValue rather than zero.
    /// </summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not a time")]
    public void BestTimeMs_Unparseable_SortsToTheBackOfTheField(string? value)
    {
        var car = CreateCar();
        // Set a real time first: the generated setter short-circuits on an unchanged value, so
        // assigning null to an already-null property would never run the parse being tested.
        car.BestTime = "00:01:22.500";

        car.BestTime = value;

        Assert.AreEqual(int.MaxValue, car.BestTimeMs);
        Assert.AreEqual(string.Empty, car.BestTimeShort);
    }

    [TestMethod]
    public void BestTimeMs_ZeroTime_SortsToTheBackOfTheField()
    {
        var car = CreateCar();

        car.BestTime = "00:00:00.000";

        Assert.AreEqual(int.MaxValue, car.BestTimeMs);
    }

    #endregion

    #region Lap progress bar

    // The bar is 1.3 wide: 1.0 of normal progress plus 0.3 of overrun past the projected lap time.

    [TestMethod]
    public void LapProgress_PartWayThroughTheLap_FillsOnlyTheNormalPortion()
    {
        var car = CreateCar();

        car.ProjectedLapTimePercent = 0.5;

        Assert.AreEqual(0.5 / 1.3, car.LapProgressNormalFraction, 1e-9);
        Assert.AreEqual(0, car.LapProgressOverrunFraction, 1e-9);
        Assert.IsFalse(car.IsLapProgressOverrun);
    }

    [TestMethod]
    public void LapProgress_ExactlyOnTheProjection_IsNotYetAnOverrun()
    {
        var car = CreateCar();

        car.ProjectedLapTimePercent = 1.0;

        Assert.AreEqual(1.0 / 1.3, car.LapProgressNormalFraction, 1e-9);
        Assert.AreEqual(0, car.LapProgressOverrunFraction, 1e-9);
        Assert.IsFalse(car.IsLapProgressOverrun);
    }

    [TestMethod]
    public void LapProgress_PastTheProjection_FillsTheOverrunPortion()
    {
        var car = CreateCar();

        car.ProjectedLapTimePercent = 1.15;

        Assert.AreEqual(1.0 / 1.3, car.LapProgressNormalFraction, 1e-9, "The normal portion stays full.");
        Assert.AreEqual(0.15 / 1.3, car.LapProgressOverrunFraction, 1e-9);
        Assert.IsTrue(car.IsLapProgressOverrun);
    }

    [TestMethod]
    public void LapProgress_FarPastTheProjection_StopsAtTheEndOfTheBar()
    {
        var car = CreateCar();

        car.ProjectedLapTimePercent = 5.0;

        Assert.AreEqual(1.0 / 1.3, car.LapProgressNormalFraction, 1e-9);
        Assert.AreEqual(0.3 / 1.3, car.LapProgressOverrunFraction, 1e-9);
    }

    [TestMethod]
    public void LapProgress_Negative_IsEmpty()
    {
        var car = CreateCar();

        car.ProjectedLapTimePercent = -0.4;

        Assert.AreEqual(0, car.LapProgressNormalFraction, 1e-9);
        Assert.AreEqual(0, car.LapProgressOverrunFraction, 1e-9);
    }

    #endregion

    #region Projected lap time progression

    private static CarViewModel CarOnLap(string totalTime, int projectedLapTimeMs = 0,
        string? lastLapTime = null, string? bestTime = null, int lastLapCompleted = 3)
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            LastLapCompleted = lastLapCompleted,
            TotalTime = totalTime,
            ProjectedLapTimeMs = projectedLapTimeMs,
            LastLapTime = lastLapTime,
            BestTime = bestTime,
        });
        return car;
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_HalfWayThroughTheProjectedLap_IsHalf()
    {
        // Crossed start/finish at 10 minutes, projected a 2 minute lap, and it is now 11 minutes.
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 120_000);

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(11));

        Assert.AreEqual(0.5, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_JustCrossedTheLine_IsZero()
    {
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 120_000);

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(10));

        Assert.AreEqual(0, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_RunningLongerThanProjected_ClampsToTheBarWidth()
    {
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 120_000);

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(20));

        Assert.AreEqual(1.3, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_RaceTimeBehindTheLapStart_ClampsToZero()
    {
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 120_000);

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(9));

        Assert.AreEqual(0, car.ProjectedLapTimePercent, 1e-9);
    }

    /// <summary>
    /// This is the reason the race clock parser has to accept more than 24 hours. A rejected total
    /// time reads as a lap that started at zero, which pins every bar at the end of a 24 hour race.
    /// </summary>
    [TestMethod]
    public void UpdateProjectedLapTimeProgression_PastTwentyFourHours_StillTracksTheLap()
    {
        var car = CarOnLap("25:10:00.000", projectedLapTimeMs: 120_000);

        car.UpdateProjectedLapTimeProgression(new TimeSpan(25, 11, 0));

        Assert.AreEqual(0.5, car.ProjectedLapTimePercent, 1e-9);
    }

    /// <summary>
    /// Characterizes the failure the unbounded parser exists to prevent, rather than endorsing it:
    /// an unreadable total time reads as a lap that started at zero, so the bar sits pinned at the
    /// end of its travel. This is why the race clock parser must not reject times past 24 hours -
    /// it used to, and this is what the grid looked like afterwards. If the unreadable case is ever
    /// given its own empty-bar handling, change this test to match; it is not a requirement.
    /// </summary>
    [TestMethod]
    public void UpdateProjectedLapTimeProgression_UnreadableTotalTime_PinsTheBarAtTheEnd()
    {
        var car = CarOnLap("garbage", projectedLapTimeMs: 120_000);

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(11));

        Assert.AreEqual(1.3, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_NoProjection_FallsBackToTheLastLapTime()
    {
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 0, lastLapTime: "00:02:00.000");

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(11));

        Assert.AreEqual(0.5, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_NoLastLapTime_FallsBackToTheBestTimePlusMargin()
    {
        // The car has never completed a timed lap this stint, so the best time stands in with a 5%
        // margin: 120s becomes 126s, and 63s elapsed is half of that.
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 0, lastLapTime: null, bestTime: "00:02:00.000");

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(63));

        Assert.AreEqual(0.5, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_NothingToProjectFrom_IsEmpty()
    {
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 0);

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(11));

        Assert.AreEqual(0, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_BeforeTheFirstLap_IsEmpty()
    {
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 120_000, lastLapCompleted: 0);

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(11));

        Assert.AreEqual(0, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_NoCarPositionYet_IsEmpty()
    {
        var car = CreateCar();

        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(11));

        Assert.AreEqual(0, car.ProjectedLapTimePercent, 1e-9);
    }

    [TestMethod]
    public void UpdateProjectedLapTimeProgression_ResetsAStalePercentWhenTheDataGoesAway()
    {
        var car = CarOnLap("00:10:00.000", projectedLapTimeMs: 120_000);
        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(11));
        Assert.AreEqual(0.5, car.ProjectedLapTimePercent, 1e-9, "Sanity check.");

        // The session resets and the lap count goes back to zero. The half-full bar has to clear.
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 0 });
        car.UpdateProjectedLapTimeProgression(TimeSpan.FromMinutes(11));

        Assert.AreEqual(0, car.ProjectedLapTimePercent, 1e-9);
    }

    #endregion
}
