using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers <see cref="CarViewModel.ApplyPatch"/>. Every field on a patch is nullable and null means
/// "the server did not send this", so the standing risk is a patch quietly clearing state it never
/// mentioned - the row would blank out mid-race for no visible reason.
/// </summary>
[TestClass]
public sealed class CarViewModelPatchTests
{
    private const string CarNumber = "5";

    // Mirrors the private row-background resource keys in CarViewModel.
    private const string StaleBackgroundKey = "carRowStaleBackgroundBrush";
    private const string NormalBackgroundKey = "carRowNormalBackgroundBrush";

    private static CarViewModel CreateCar() => TestViewModelFactory.CreateCar();

    private static CarPositionPatch Patch() => new() { Number = CarNumber };

    #region Patch merge semantics

    [TestMethod]
    public void ApplyPatch_OmittedFields_LeaveTheExistingValues()
    {
        var car = CreateCar();
        car.BestTime = "00:01:22.500";
        car.TotalTime = "01:02:03.000";
        car.OverallPosition = 4;
        car.ClassPosition = 2;
        car.PenaltyLaps = 3;

        // Only the lap moved. Nothing else was sent, so nothing else may change.
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 9 });

        Assert.AreEqual("00:01:22.500", car.BestTime);
        Assert.AreEqual("01:02:03.000", car.TotalTime);
        Assert.AreEqual(4, car.OverallPosition);
        Assert.AreEqual(2, car.ClassPosition);
        Assert.AreEqual(3, car.PenaltyLaps);
        Assert.AreEqual(9, car.LastLap);
    }

    [TestMethod]
    public void ApplyPatch_FirstPatch_MaterializesTheCarPosition()
    {
        var car = CreateCar();
        Assert.IsNull(car.LastCarPosition, "Sanity check - nothing has been applied yet.");

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 7, TotalTime = "00:10:00.000" });

        Assert.IsNotNull(car.LastCarPosition);
        Assert.AreEqual(7, car.LastCarPosition.LastLapCompleted);
        Assert.AreEqual("00:10:00.000", car.LastCarPosition.TotalTime);
    }

    [TestMethod]
    public void ApplyPatch_SecondPatch_MergesIntoTheExistingCarPosition()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 7, TotalTime = "00:10:00.000" });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 8 });

        Assert.IsNotNull(car.LastCarPosition);
        Assert.AreEqual(8, car.LastCarPosition.LastLapCompleted);
        Assert.AreEqual("00:10:00.000", car.LastCarPosition.TotalTime, "The untouched field should have survived the merge.");
    }

    #endregion

    #region Pit state precedence

    [TestMethod]
    public void ApplyPatch_IsInPit_ShowsInPit()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsInPit = true });

        Assert.AreEqual(PitStates.InPit, car.PitState);
    }

    [TestMethod]
    public void ApplyPatch_IsInPitFalse_ClearsThePitState()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsInPit = true });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsInPit = false });

        Assert.AreEqual(PitStates.None, car.PitState);
    }

    /// <summary>
    /// The four pit flags arrive together and overlap - a car crossing the pit timing line is both
    /// entering the pit and in it. The order of the checks is what decides which one the row shows.
    /// </summary>
    [TestMethod]
    public void ApplyPatch_EnteredPit_OutranksEveryOtherPitFlag()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            IsEnteredPit = true,
            IsPitStartFinish = true,
            IsExitedPit = true,
            IsInPit = true,
        });

        Assert.AreEqual(PitStates.EnteredPit, car.PitState);
    }

    [TestMethod]
    public void ApplyPatch_PitStartFinish_OutranksExitedAndInPit()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            IsEnteredPit = false,
            IsPitStartFinish = true,
            IsExitedPit = true,
            IsInPit = true,
        });

        Assert.AreEqual(PitStates.PitSF, car.PitState);
    }

    [TestMethod]
    public void ApplyPatch_ExitedPit_OutranksInPit()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            IsExitedPit = true,
            IsInPit = true,
        });

        Assert.AreEqual(PitStates.ExitedPit, car.PitState);
    }

    [TestMethod]
    public void ApplyPatch_NoPitFieldsSent_LeavesThePitStateAlone()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsInPit = true });

        // A routine lap-time patch carries no pit fields. The car is still sitting in the pit box.
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapTime = "00:01:30.000" });

        Assert.AreEqual(PitStates.InPit, car.PitState);
    }

    /// <summary>
    /// The three leading flags are checked with "?? false", so an explicit false has to fall
    /// through to the IsInPit check rather than being treated as a state of its own.
    /// </summary>
    [TestMethod]
    public void ApplyPatch_LeadingPitFlagsFalse_FallsThroughToIsInPit()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            IsEnteredPit = false,
            IsPitStartFinish = false,
            IsExitedPit = false,
            IsInPit = true,
        });

        Assert.AreEqual(PitStates.InPit, car.PitState);
    }

    #endregion

    #region Pit stop recording

    [TestMethod]
    public void ApplyPatch_InPit_RecordsThePitStopAgainstTheLapInTheSamePatch()
    {
        var pitTracking = new PitTracking();
        var car = TestViewModelFactory.CreateCar(pitTracking);
        car.Number = CarNumber;

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 12, IsInPit = true });

        // ApplyPitStop is the read side: it stamps the flag onto positions on a recorded pit lap.
        var onPitLap = new List<CarPosition> { new() { Number = CarNumber, LastLapCompleted = 12 } };
        pitTracking.ApplyPitStop(onPitLap);
        Assert.IsTrue(onPitLap[0].LapIncludedPit, "The lap from the patch should have been recorded.");

        var otherLap = new List<CarPosition> { new() { Number = CarNumber, LastLapCompleted = 11 } };
        pitTracking.ApplyPitStop(otherLap);
        Assert.IsFalse(otherLap[0].LapIncludedPit, "The previous lap should not have been recorded.");
    }

    /// <summary>
    /// The lap number and the pit flag are not guaranteed to arrive in the same patch, so the stop
    /// is recorded against whatever lap the row is on when the flag lands.
    /// </summary>
    [TestMethod]
    public void ApplyPatch_PitFlagInASeparatePatch_RecordsAgainstTheLapAlreadyOnTheRow()
    {
        var pitTracking = new PitTracking();
        var car = TestViewModelFactory.CreateCar(pitTracking);
        car.Number = CarNumber;

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 12 });
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsInPit = true });

        var onPitLap = new List<CarPosition> { new() { Number = CarNumber, LastLapCompleted = 12 } };
        pitTracking.ApplyPitStop(onPitLap);
        Assert.IsTrue(onPitLap[0].LapIncludedPit);
    }

    [TestMethod]
    public void ApplyPatch_NotInPit_RecordsNothing()
    {
        var pitTracking = new PitTracking();
        var car = TestViewModelFactory.CreateCar(pitTracking);
        car.Number = CarNumber;

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 12, IsInPit = false });

        var positions = new List<CarPosition> { new() { Number = CarNumber, LastLapCompleted = 12 } };
        pitTracking.ApplyPitStop(positions);
        Assert.IsFalse(positions[0].LapIncludedPit);
    }

    #endregion

    #region Driver name

    [TestMethod]
    public void ApplyPatch_DriverName_IsShownAndTrimmed()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, DriverName = "  Pat Morgan  " });

        Assert.IsTrue(car.HasDriverName);
        Assert.AreEqual("Pat Morgan", car.DriverName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ApplyPatch_BlankDriverName_ClearsTheRow(string blank)
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, DriverName = "Pat Morgan" });

        // A driver change with no name yet sends blanks rather than dropping the field.
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, DriverName = blank });

        Assert.IsFalse(car.HasDriverName);
        Assert.AreEqual(string.Empty, car.DriverName);
    }

    [TestMethod]
    public void ApplyPatch_NoDriverNameSent_KeepsThePreviousDriver()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, DriverName = "Pat Morgan" });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 3 });

        Assert.IsTrue(car.HasDriverName);
        Assert.AreEqual("Pat Morgan", car.DriverName);
    }

    #endregion

    #region Stale rows

    // These cases deliberately leave the lap alone. A lap change starts the row-flash timers, which
    // rewrite RowBackgroundKey from a background thread 80ms later and would make this racy.

    [TestMethod]
    public void ApplyPatch_Stale_SwitchesToTheStaleRowBackground()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsStale = true });

        Assert.AreEqual(StaleBackgroundKey, car.RowBackgroundKey);
    }

    [TestMethod]
    public void ApplyPatch_NoLongerStale_RestoresTheNormalRowBackground()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsStale = true });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsStale = false });

        Assert.AreEqual(NormalBackgroundKey, car.RowBackgroundKey);
    }

    [TestMethod]
    public void ApplyPatch_StaleFlagNotSent_StaysStale()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, IsStale = true });

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, OverallPosition = 6 });

        Assert.AreEqual(StaleBackgroundKey, car.RowBackgroundKey);
    }

    #endregion

    #region Lap time styling

    [TestMethod]
    public void ApplyPatch_OverallBestLap_HighlightsTheBestTimeColumn()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            LastLapCompleted = 5,
            BestLap = 5,
            IsBestTime = true,
        });

        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_OVERALLBEST_BRUSH, car.BestLapDataBrushKey);
    }

    [TestMethod]
    public void ApplyPatch_PersonalBestLap_HighlightsBothColumnsAsAPersonalBest()
    {
        var car = CreateCar();

        // The car's own best lap is the one it just set, but it is not the best in the field.
        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            LastLapCompleted = 5,
            BestLap = 5,
            IsBestTime = false,
        });

        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_BEST_BRUSH, car.BestLapDataBrushKey);
        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_BEST_BRUSH, car.LapDataBrushKey);
    }

    [TestMethod]
    public void ApplyPatch_OrdinaryLap_UsesTheNormalStyling()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            LastLapCompleted = 5,
            BestLap = 3,
            IsBestTime = false,
        });

        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH, car.BestLapDataBrushKey);
        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH, car.LapDataBrushKey);
    }

    [TestMethod]
    public void ApplyPatch_BeforeTheFirstLap_ClearsAnyCarriedOverHighlight()
    {
        var car = CreateCar();
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 5, BestLap = 5, IsBestTime = true });

        // The row is reused for a new session, where no laps have been completed yet.
        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 0 });

        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH, car.BestLapDataBrushKey);
        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH, car.LapDataBrushKey);
    }

    /// <summary>
    /// A car can hold the class best without holding the overall best, so the highlight depends on
    /// the grouping. Note where the restyle actually happens: the brush keys are only written by
    /// ApplyPatch, so switching grouping on its own leaves the previous grouping's highlight on the
    /// row until the next patch arrives. That is invisible during a live session but persists on
    /// stored results, where no further patches ever come.
    /// </summary>
    [TestMethod]
    public void ApplyPatch_ClassBestLap_HighlightsOnlyOnceAPatchArrivesInClassGrouping()
    {
        var car = CreateCar();
        car.CurrentGroupMode = GroupMode.Overall;

        car.ApplyPatch(new CarPositionPatch
        {
            Number = CarNumber,
            LastLapCompleted = 5,
            BestLap = 3,
            IsBestTime = false,
            IsBestTimeClass = true,
        });
        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH, car.BestLapDataBrushKey);

        car.CurrentGroupMode = GroupMode.Class;
        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH, car.BestLapDataBrushKey,
            "Changing grouping alone does not restyle the row.");

        car.ApplyPatch(new CarPositionPatch { Number = CarNumber, LastLapCompleted = 6 });

        Assert.AreEqual(CarViewModel.CARROWLAPTEXTFOREGROUND_OVERALLBEST_BRUSH, car.BestLapDataBrushKey);
    }

    #endregion
}
