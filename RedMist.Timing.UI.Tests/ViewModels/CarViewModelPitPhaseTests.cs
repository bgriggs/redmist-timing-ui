using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Mappers;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers the pit phase on a car row: the badge it picks, the fallback to the pit flags when there
/// is none, and the time stopped shown while the car is in its box.
/// </summary>
/// <remarks>
/// Older servers and multiloop events never send a phase, so it stays None there and the flag tests
/// in <see cref="CarViewModelPatchTests"/> are the behavior those must keep.
/// </remarks>
[TestClass]
public sealed class CarViewModelPitPhaseTests
{
    private const string CarNumber = "5";

    private static readonly DateTime StopStart = new(2026, 10, 17, 13, 57, 1, DateTimeKind.Utc);

    private static CarViewModel CreateCar() => TestViewModelFactory.CreateCar();

    #region Phase to badge

    [TestMethod]
    [DataRow(PitPhase.EnteredPit, PitStates.EnteredPit)]
    [DataRow(PitPhase.InPitBox, PitStates.InPitBox)]
    [DataRow(PitPhase.ExitingPit, PitStates.ExitingPit)]
    [DataRow(PitPhase.ExitedPit, PitStates.ExitedPit)]
    [DataRow(PitPhase.InPit, PitStates.InPit)]
    public void ApplyPatch_PitPhase_ShowsItsBadge(PitPhase phase, PitStates expected)
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = phase });

        Assert.AreEqual(expected, car.PitState);
    }

    [TestMethod]
    public void FromPitPhase_None_LeavesTheFlagsToDecide()
    {
        Assert.IsNull(CarViewModel.FromPitPhase(PitPhase.None));
    }

    /// <summary>
    /// The contract asks clients to read a value they do not know as None, so a server that adds a
    /// phase does not put a badge this build cannot draw on the row.
    /// </summary>
    [TestMethod]
    public void FromPitPhase_AValueThisBuildDoesNotKnow_ReadsAsNone()
    {
        Assert.IsNull(CarViewModel.FromPitPhase((PitPhase)99));
    }

    [TestMethod]
    public void ApplyPatch_UnknownPitPhase_FallsBackToTheFlags()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = (PitPhase)99, IsInPit = true });

        Assert.AreEqual(PitStates.InPit, car.PitState);
    }

    /// <summary>
    /// The flags arrive alongside the phase and say less: a car leaving its box is still in the pit
    /// lane, and X2 leaves the entered flag up until the car stops.
    /// </summary>
    [TestMethod]
    public void ApplyPatch_PitPhase_OutranksThePitFlags()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            PitPhase = PitPhase.ExitingPit,
            IsEnteredPit = true,
            IsPitStartFinish = true,
            IsInPit = true,
        });

        Assert.AreEqual(PitStates.ExitingPit, car.PitState);
    }

    [TestMethod]
    public void ApplyPatch_PatchWithoutPitPhase_KeepsThePhaseShowing()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, IsInPit = true });

        // A routine patch from the same server. The phase has not changed, so it is not resent.
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapTime = "00:01:30.000", IsEnteredPit = true });

        Assert.AreEqual(PitStates.InPitBox, car.PitState);
    }

    #endregion

    #region None fallback

    [TestMethod]
    public void ApplyPatch_PitPhaseNoneWithFlags_UsesTheFlags()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.None, IsPitStartFinish = true, IsInPit = true });

        Assert.AreEqual(PitStates.PitSF, car.PitState);
    }

    /// <summary>
    /// A full state from an older server carries the phase as None on every car. That must not
    /// disturb the sticky behavior the flags have always had.
    /// </summary>
    [TestMethod]
    public void ApplyPatch_PitPhaseNoneWithNoFlags_LeavesAFlagBadgeAlone()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsInPit = true });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.None });

        Assert.AreEqual(PitStates.InPit, car.PitState);
    }

    /// <summary>
    /// The phase dropping to None need not come with a flag, and the flags on their own leave the
    /// badge alone without one, so the car's own flags have to be read instead.
    /// </summary>
    [TestMethod]
    public void ApplyPatch_PhaseEndsWithoutAFlag_ReadsTheCarsFlags()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.ExitedPit, IsExitedPit = true, IsInPit = false });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.None, IsExitedPit = false });

        Assert.AreEqual(PitStates.None, car.PitState);
    }

    [TestMethod]
    public void ApplyPatch_PhaseEndsWhileStillFlaggedInPit_ShowsInPit()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, IsInPit = true });

        // The source that owned the phase let go of the car, and the one taking over has only the flag.
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.None });

        Assert.AreEqual(PitStates.InPit, car.PitState);
    }

    [TestMethod]
    public void ApplyPatch_PhaseEndsAlongsideAFlag_UsesTheFlagPrecedence()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, IsInPit = true });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.None, IsExitedPit = true });

        Assert.AreEqual(PitStates.ExitedPit, car.PitState);
    }

    #endregion

    #region Full refresh

    /// <summary>
    /// The periodic refresh builds each car's patch against an empty car, which drops every value
    /// that is the default. A car out of the pit is all defaults - flags false and phase None - so
    /// without filling them in, a badge left by a missed delta would never come down.
    /// </summary>
    [TestMethod]
    public void FullRefreshPatch_CarOutOfThePit_ClearsAStalePhaseBadge()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart, IsInPit = true });
        Assert.AreEqual(PitStates.InPitBox, car.PitState, "Sanity check.");

        var state = new CarPosition { Number = CarNumber, LastLapCompleted = 12, OverallPosition = 3, LastLapTime = "00:01:31.250" };
        var dropped = CarPositionMapper.CreatePatch(new CarPosition(), state);
        Assert.IsNull(dropped.PitPhase, "Sanity check - the gap this covers: a patch against an empty car drops None.");
        Assert.IsNull(dropped.IsInPit, "Sanity check - and drops IsInPit false.");

        car.ApplyPatch(LiveTimingViewModel.ToFullPatch(dropped));

        Assert.AreEqual(PitStates.None, car.PitState);
        Assert.AreEqual(12, car.LastLap, "The rest of the state still applies.");
        Assert.AreEqual(3, car.OverallPosition);
        Assert.AreEqual("00:01:31.250", car.LastTime);
        Assert.IsFalse(car.LastCarPosition!.IsInPit, "The row's own copy of the car is cleared too.");
    }

    [TestMethod]
    public void FullRefreshPatch_CarOutOfThePit_ClearsAStaleFlagBadge()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsEnteredPit = true, IsInPit = true });

        car.ApplyPatch(LiveTimingViewModel.ToFullPatch(
            CarPositionMapper.CreatePatch(new CarPosition(), new CarPosition { Number = CarNumber })));

        Assert.AreEqual(PitStates.None, car.PitState);
    }

    [TestMethod]
    public void FullRefreshPatch_CarInItsBox_KeepsThePhase()
    {
        var car = CreateCar();
        var state = new CarPosition { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart, IsInPit = true };

        car.ApplyPatch(LiveTimingViewModel.ToFullPatch(CarPositionMapper.CreatePatch(new CarPosition(), state)));

        Assert.AreEqual(PitStates.InPitBox, car.PitState);
        Assert.IsTrue(car.HasPitStopClock);
    }

    #endregion

    #region Time stopped

    [TestMethod]
    public void UpdatePitStopClock_InTheBox_ShowsTheTimeStopped()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart });

        car.UpdatePitStopClock(StopStart.AddSeconds(42));

        Assert.IsTrue(car.HasPitStopClock);
        Assert.AreEqual("PIT 0:42", car.PitBoxText);
    }

    [TestMethod]
    public void UpdatePitStopClock_NoStartTime_ShowsPlainPit()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox });

        car.UpdatePitStopClock(StopStart.AddSeconds(42));

        Assert.IsFalse(car.HasPitStopClock, "Nothing to count from, so the grid's clock has nothing to do for this row.");
        Assert.AreEqual("PIT", car.PitBoxText);
    }

    [TestMethod]
    public void UpdatePitStopClock_DeviceClockBehindTheServer_ShowsZero()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart });

        car.UpdatePitStopClock(StopStart.AddSeconds(-5));

        Assert.AreEqual("PIT 0:00", car.PitBoxText);
    }

    [TestMethod]
    public void UpdatePitStopClock_StartReadAsLocalTime_IsCountedFromTheSameInstant()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart.ToLocalTime() });

        car.UpdatePitStopClock(StopStart.AddSeconds(42));

        Assert.AreEqual("PIT 0:42", car.PitBoxText);
    }

    /// <summary>
    /// The start time is left in place after a stop, since a patch cannot clear it, so only the phase
    /// says whether it still applies.
    /// </summary>
    [TestMethod]
    public void ApplyPatch_LeavingTheBox_StopsCounting()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart });
        car.UpdatePitStopClock(StopStart.AddSeconds(42));

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.ExitingPit });

        Assert.IsFalse(car.HasPitStopClock);
        Assert.AreEqual("PIT", car.PitBoxText, "The next stop must not open on this one's time.");
    }

    [TestMethod]
    public void ApplyPatch_NewStopStart_DropsTheLastStopsTime()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart });
        car.UpdatePitStopClock(StopStart.AddMinutes(3));

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitStopStartTime = StopStart.AddHours(1) });

        Assert.AreEqual("PIT", car.PitBoxText);
    }

    [TestMethod]
    public void ApplyPatch_SameStopResent_KeepsTheTimeShowing()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart });
        car.UpdatePitStopClock(StopStart.AddSeconds(42));

        // A full refresh resends every value, the start time included.
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart });

        Assert.AreEqual("PIT 0:42", car.PitBoxText);
    }

    [TestMethod]
    [DataRow(0, "0:00")]
    [DataRow(42_000, "0:42")]
    [DataRow(41_999, "0:41", DisplayName = "Counts whole seconds, like a stopwatch")]
    [DataRow(83_000, "1:23")]
    [DataRow(599_000, "9:59")]
    [DataRow(3_599_000, "59:59")]
    [DataRow(3_600_000, "1:00:00")]
    [DataRow(3_723_000, "1:02:03")]
    [DataRow(90_000_000, "25:00:00", DisplayName = "Hours do not wrap at a day")]
    [DataRow(-1_000, "0:00", DisplayName = "Negative clamps to zero")]
    public void FormatPitStopTime_FormatsAsMinutesAndSeconds(int milliseconds, string expected)
    {
        Assert.AreEqual(expected, CarViewModel.FormatPitStopTime(TimeSpan.FromMilliseconds(milliseconds)));
    }

    #endregion

    #region Last pit stop

    [TestMethod]
    public void ApplyPatch_LastPitStopDuration_IsShownAsMinutesAndSeconds()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastPitStopDurationMs = 83_400 });

        Assert.IsTrue(car.HasLastPitStop);
        Assert.AreEqual("1:23", car.LastPitStopDuration);
    }

    [TestMethod]
    public void NoStopCompleted_ShowsNoLastPitStop()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.InPitBox, PitStopStartTime = StopStart });

        Assert.IsFalse(car.HasLastPitStop);
        Assert.AreEqual(string.Empty, car.LastPitStopDuration);
    }

    [TestMethod]
    public void ApplyPatch_NoLastPitStopSent_KeepsThePreviousOne()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastPitStopDurationMs = 83_400 });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, PitPhase = PitPhase.EnteredPit });

        Assert.AreEqual("1:23", car.LastPitStopDuration);
    }

    #endregion
}
