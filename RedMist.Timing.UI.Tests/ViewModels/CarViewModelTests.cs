using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Mappers;

namespace RedMist.Timing.UI.Tests.ViewModels;

[TestClass]
public sealed class CarViewModelTests
{
    private static CarViewModel CreateCar() => TestViewModelFactory.CreateCar();

    /// <summary>
    /// A car with both the race gaps and the fast-time gaps populated, so a test that reads the
    /// wrong pair gets a distinguishable value rather than an empty string.
    /// </summary>
    private static CarViewModel CreateCarWithGaps()
    {
        var car = CreateCar();
        car.OverallGap = "2.000";
        car.OverallDifference = "0.800";
        car.InClassGap = "1.234";
        car.InClassDifference = "0.500";
        car.OverallGapByFastTime = "5.300";
        car.OverallDifferenceByFastTime = "6.400";
        car.InClassGapByFastTime = "3.100";
        car.InClassDifferenceByFastTime = "4.200";
        return car;
    }

    #region Gap and difference selection

    [TestMethod]
    public void Gap_PositionSort_Overall_UsesRaceGap()
    {
        var car = CreateCarWithGaps();
        car.CurrentSortMode = SortMode.Position;
        car.CurrentGroupMode = GroupMode.Overall;

        Assert.AreEqual("2.000", car.Gap);
        Assert.AreEqual("0.800", car.Difference);
    }

    [TestMethod]
    public void Gap_PositionSort_Class_UsesRaceGap()
    {
        var car = CreateCarWithGaps();
        car.CurrentSortMode = SortMode.Position;
        car.CurrentGroupMode = GroupMode.Class;

        Assert.AreEqual("1.234", car.Gap);
        Assert.AreEqual("0.500", car.Difference);
    }

    [TestMethod]
    public void Gap_FastestSort_Overall_UsesFastTimeGap()
    {
        var car = CreateCarWithGaps();
        car.CurrentSortMode = SortMode.Fastest;
        car.CurrentGroupMode = GroupMode.Overall;

        Assert.AreEqual("5.300", car.Gap);
        Assert.AreEqual("6.400", car.Difference);
    }

    [TestMethod]
    public void Gap_FastestSort_Class_UsesFastTimeGap()
    {
        var car = CreateCarWithGaps();
        car.CurrentSortMode = SortMode.Fastest;
        car.CurrentGroupMode = GroupMode.Class;

        Assert.AreEqual("3.100", car.Gap);
        Assert.AreEqual("4.200", car.Difference);
    }

    [TestMethod]
    public void Gap_FastestSort_NoLapTime_IsBlankRatherThanRaceGap()
    {
        var car = CreateCarWithGaps();
        car.OverallGapByFastTime = string.Empty;
        car.OverallDifferenceByFastTime = string.Empty;
        car.CurrentSortMode = SortMode.Fastest;
        car.CurrentGroupMode = GroupMode.Overall;

        Assert.AreEqual(string.Empty, car.Gap);
        Assert.AreEqual(string.Empty, car.Difference);
    }

    [TestMethod]
    public void Gap_FastestSort_ServerNeverSentFastTimeGaps_FallsBackToRaceGap()
    {
        // Sessions recorded before the server computed the fast-time gaps replay with those
        // fields absent. Falling back keeps the columns populated for stored results instead of
        // blanking the whole field, which an empty-string default would do.
        var car = CreateCarWithGaps();
        car.OverallGapByFastTime = null;
        car.OverallDifferenceByFastTime = null;
        car.InClassGapByFastTime = null;
        car.InClassDifferenceByFastTime = null;
        car.CurrentSortMode = SortMode.Fastest;

        car.CurrentGroupMode = GroupMode.Overall;
        Assert.AreEqual("2.000", car.Gap);
        Assert.AreEqual("0.800", car.Difference);

        car.CurrentGroupMode = GroupMode.Class;
        Assert.AreEqual("1.234", car.Gap);
        Assert.AreEqual("0.500", car.Difference);
    }

    #endregion

    #region Change notification

    [TestMethod]
    public void CurrentSortMode_Changed_NotifiesGapAndDifference()
    {
        var car = CreateCarWithGaps();
        var changed = new List<string?>();
        car.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        car.CurrentSortMode = SortMode.Fastest;

        CollectionAssert.Contains(changed, nameof(CarViewModel.Gap));
        CollectionAssert.Contains(changed, nameof(CarViewModel.Difference));
    }

    [TestMethod]
    public void CurrentSortMode_SetToSameValue_DoesNotNotify()
    {
        var car = CreateCarWithGaps();
        car.CurrentSortMode = SortMode.Fastest;
        var changed = new List<string?>();
        car.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        car.CurrentSortMode = SortMode.Fastest;

        CollectionAssert.DoesNotContain(changed, nameof(CarViewModel.Gap));
        CollectionAssert.DoesNotContain(changed, nameof(CarViewModel.Difference));
    }

    [TestMethod]
    public void FastTimeGapProperties_Changed_NotifyGapAndDifference()
    {
        var car = CreateCar();
        var changed = new List<string?>();
        car.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        car.OverallGapByFastTime = "1.000";
        car.InClassGapByFastTime = "2.000";
        car.OverallDifferenceByFastTime = "3.000";
        car.InClassDifferenceByFastTime = "4.000";

        Assert.AreEqual(2, changed.Count(p => p == nameof(CarViewModel.Gap)));
        Assert.AreEqual(2, changed.Count(p => p == nameof(CarViewModel.Difference)));
    }

    #endregion

    #region Patch plumbing

    [TestMethod]
    public void ApplyPatch_AppliesFastTimeGaps()
    {
        var car = CreateCar();

        car.ApplyPatch(CarPositionMapper.CreatePatch(new CarPosition(), new CarPosition
        {
            Number = "1",
            OverallGapByFastTime = "0.300",
            OverallDifferenceByFastTime = "0.400",
            InClassGapByFastTime = "0.100",
            InClassDifferenceByFastTime = "0.200",
        }));

        Assert.AreEqual("0.300", car.OverallGapByFastTime);
        Assert.AreEqual("0.400", car.OverallDifferenceByFastTime);
        Assert.AreEqual("0.100", car.InClassGapByFastTime);
        Assert.AreEqual("0.200", car.InClassDifferenceByFastTime);
    }

    [TestMethod]
    public void ApplyPatch_ClearsFastTimeGaps_WhenCarTakesTheBestLap()
    {
        var car = CreateCar();
        car.OverallGapByFastTime = "0.300";
        car.OverallDifferenceByFastTime = "0.400";

        // The backend sends empty - not null - for the car holding the best lap time, so the
        // previous gap has to be cleared rather than left showing a stale value.
        car.ApplyPatch(new CarPositionPatch
        {
            Number = "1",
            OverallGapByFastTime = string.Empty,
            OverallDifferenceByFastTime = string.Empty,
        });

        Assert.AreEqual(string.Empty, car.OverallGapByFastTime);
        Assert.AreEqual(string.Empty, car.OverallDifferenceByFastTime);
    }

    [TestMethod]
    public void ApplyPatch_LeavesFastTimeGaps_WhenPatchOmitsThem()
    {
        var car = CreateCar();
        car.OverallGapByFastTime = "0.300";
        car.InClassGapByFastTime = "0.100";

        // Null means "no change" on a patch - only the fields the backend sent get applied.
        car.ApplyPatch(new CarPositionPatch { Number = "1", LastLapCompleted = 15 });

        Assert.AreEqual("0.300", car.OverallGapByFastTime);
        Assert.AreEqual("0.100", car.InClassGapByFastTime);
    }

    [TestMethod]
    public void ApplyPatch_NewBestTime_UpdatesBestTimeMs()
    {
        var car = CreateCar();

        car.ApplyPatch(new CarPositionPatch { Number = "1", BestTime = "00:01:22.500" });

        Assert.AreEqual(82500, car.BestTimeMs);
    }

    #endregion
}
