using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Mappers;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers the sort mode reaching the car rows, which is what makes the gap and difference
/// columns switch to the fast-time values.
/// </summary>
[TestClass]
public sealed class LiveTimingViewModelSortModeTests
{
    private const string CarNumber = "1";

    private static SessionStatusNotification CreateSession(bool withFastTimeGaps, bool? isPracticeQualifying = null)
    {
        var state = new SessionState
        {
            EventEntries = [new EventEntry { Number = CarNumber, Name = "Test Car", Class = "GP1" }],
            CarPositions =
            [
                new CarPosition
                {
                    Number = CarNumber,
                    Class = "GP1",
                    OverallPosition = 1,
                    BestTime = "00:01:22.500",
                    OverallGap = "2.000",
                    OverallDifference = "0.800",
                    InClassGap = "1.234",
                    InClassDifference = "0.500",
                    OverallGapByFastTime = withFastTimeGaps ? "5.300" : null,
                    OverallDifferenceByFastTime = withFastTimeGaps ? "6.400" : null,
                    InClassGapByFastTime = withFastTimeGaps ? "3.100" : null,
                    InClassDifferenceByFastTime = withFastTimeGaps ? "4.200" : null,
                }
            ],
        };
        if (isPracticeQualifying != null)
        {
            state.IsPracticeQualifying = isPracticeQualifying.Value;
        }

        return new SessionStatusNotification(SessionStateMapper.ToPatch(state));
    }

    private static CarViewModel SingleCar(LiveTimingViewModel vm)
    {
        Assert.AreEqual(1, vm.Cars.Count, "Expected the session to produce exactly one car row.");
        return vm.Cars[0];
    }

    [TestMethod]
    public void ToggleSortMode_SwitchesExistingCarsToTheFastTimeGaps()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(CreateSession(withFastTimeGaps: true));
        var car = SingleCar(vm);
        Assert.AreEqual("2.000", car.Gap, "Sanity check - should start on the race gap.");

        vm.ToggleSortMode();

        Assert.AreEqual("5.300", car.Gap);
        Assert.AreEqual("6.400", car.Difference);
    }

    [TestMethod]
    public void ToggleSortMode_BackToPosition_RestoresTheRaceGaps()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(CreateSession(withFastTimeGaps: true));
        var car = SingleCar(vm);

        vm.ToggleSortMode();
        vm.ToggleSortMode();

        Assert.AreEqual("2.000", car.Gap);
        Assert.AreEqual("0.800", car.Difference);
    }

    [TestMethod]
    public void CarsAddedWhileSortedByFastest_UseTheFastTimeGaps()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ToggleSortMode();

        // The entries arrive after the toggle, so the row is built with the sort already in effect.
        vm.ApplySessionUpdate(CreateSession(withFastTimeGaps: true));

        var car = SingleCar(vm);
        Assert.AreEqual("5.300", car.Gap);
        Assert.AreEqual("6.400", car.Difference);
    }

    [TestMethod]
    public void PracticeQualifyingSession_SortsByFastestAndUsesTheFastTimeGaps()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(CreateSession(withFastTimeGaps: true, isPracticeQualifying: true));

        Assert.AreEqual(SortMode.Fastest, vm.CurrentSortMode);
        var car = SingleCar(vm);
        Assert.AreEqual("5.300", car.Gap);
        Assert.AreEqual("6.400", car.Difference);
    }

    [TestMethod]
    public void StoredQualifyingSessionWithoutFastTimeGaps_KeepsShowingTheRaceGaps()
    {
        // Results recorded before the server computed the fast-time gaps still auto-switch to the
        // fastest sort. Those columns have to keep their old values rather than go blank.
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(CreateSession(withFastTimeGaps: false, isPracticeQualifying: true));

        Assert.AreEqual(SortMode.Fastest, vm.CurrentSortMode);
        var car = SingleCar(vm);
        Assert.AreEqual("2.000", car.Gap);
        Assert.AreEqual("0.800", car.Difference);
    }

    [TestMethod]
    public void ToggleGroupMode_WhileSortedByFastest_UsesTheInClassFastTimeGaps()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(CreateSession(withFastTimeGaps: true));
        var car = SingleCar(vm);
        vm.ToggleSortMode();

        vm.ToggleGroupMode();

        Assert.AreEqual(GroupMode.Class, vm.CurrentGrouping);
        Assert.AreEqual("3.100", car.Gap);
        Assert.AreEqual("4.200", car.Difference);
    }
}
