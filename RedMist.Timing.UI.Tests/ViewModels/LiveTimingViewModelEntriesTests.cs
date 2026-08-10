using Avalonia.Media;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers the entry list building the car rows, and the session fields that sit above the grid.
/// </summary>
[TestClass]
public sealed class LiveTimingViewModelEntriesTests
{
    private static SessionStatusNotification Update(SessionStatePatch patch) => new(patch);

    private static EventEntry Entry(string number, string @class = "GP1", string name = "Car", string team = "Team")
        => new() { Number = number, Name = name, Team = team, Class = @class };

    private static string[] CarNumbers(LiveTimingViewModel vm) => [.. vm.Cars.Select(c => c.Number)];

    #region Building the car list

    [TestMethod]
    public void Entries_BuildOneRowPerCar()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2"), Entry("3")],
        }));

        Assert.AreEqual(3, vm.Cars.Count);
        CollectionAssert.AreEquivalent(new[] { "1", "2", "3" }, CarNumbers(vm));
    }

    [TestMethod]
    public void Entries_CarryTheEntryDetailsOntoTheRow()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("7", @class: "GP2", name: "Morgan Racing", team: "Morgan")],
        }));

        var car = vm.Cars.Single();
        Assert.AreEqual("7", car.Number);
        Assert.AreEqual("Morgan Racing", car.OriginalName);
        Assert.AreEqual("Morgan", car.Team);
        Assert.AreEqual("GP2", car.Class);
    }

    [TestMethod]
    public void Entries_RepeatedForTheSameCar_UpdateTheRowInsteadOfDuplicatingIt()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1", name: "Old Name")] }));
        var original = vm.Cars.Single();

        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1", name: "New Name")] }));

        Assert.AreEqual(1, vm.Cars.Count);
        Assert.AreSame(original, vm.Cars.Single(), "The row should have been reused, not rebuilt.");
        Assert.AreEqual("New Name", original.OriginalName);
    }

    /// <summary>
    /// Cars that withdraw drop out of the entry list, and their rows have to go with them.
    /// </summary>
    [TestMethod]
    public void Entries_DropCarsThatAreNoLongerEntered()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2"), Entry("3")],
        }));

        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("2")] }));

        CollectionAssert.AreEqual(new[] { "2" }, CarNumbers(vm));
    }

    [TestMethod]
    public void Entries_RemovingEveryCar_EmptiesTheGrid()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2")],
            CarPositions = [new CarPosition { Number = "1", LastLapCompleted = 8 }],
        }));
        Assert.AreEqual("8", vm.TotalLaps, "Sanity check - the lap counter picked up the field.");

        // The empty positions list is what drives the lap counter back to blank. Without it the
        // grid empties but the header keeps showing the departed field's lap count.
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [], CarPositions = [] }));

        Assert.AreEqual(0, vm.Cars.Count);
        Assert.AreEqual(string.Empty, vm.TotalLaps);
    }

    [TestMethod]
    public void Entries_NotSent_LeaveTheExistingRowsAlone()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1"), Entry("2")] }));

        // A routine flag-only update carries no entry list and must not clear the grid.
        vm.ApplySessionUpdate(Update(new SessionStatePatch { CurrentFlag = Flags.Green }));

        Assert.AreEqual(2, vm.Cars.Count);
    }

    /// <summary>
    /// A car reclassified mid-event keeps its row, so the class has to follow it - the grouped view
    /// buckets rows by that property and would otherwise leave the car under its old class.
    /// </summary>
    [TestMethod]
    public void Entries_CarReclassified_MovesTheRowToTheNewClass()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            ClassColors = new Dictionary<string, string> { ["GP1"] = "#FF0000", ["GP2"] = "#0000FF" },
            EventEntries = [Entry("1", @class: "GP1")],
        }));
        var row = vm.Cars.Single();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            ClassColors = new Dictionary<string, string> { ["GP1"] = "#FF0000", ["GP2"] = "#0000FF" },
            EventEntries = [Entry("1", @class: "GP2")],
        }));

        Assert.AreSame(row, vm.Cars.Single(), "The row should have been reused.");
        Assert.AreEqual("GP2", row.Class);
        Assert.AreEqual(Colors.Blue, ColorOf(row));
    }

    [TestMethod]
    public void Entries_AddedLater_JoinTheExistingRows()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1")] }));

        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1"), Entry("2")] }));

        CollectionAssert.AreEquivalent(new[] { "1", "2" }, CarNumbers(vm));
    }

    #endregion

    #region Ordering

    [TestMethod]
    public void Cars_AreOrderedByPosition()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2"), Entry("3")],
            CarPositions =
            [
                new CarPosition { Number = "1", OverallPosition = 3 },
                new CarPosition { Number = "2", OverallPosition = 1 },
                new CarPosition { Number = "3", OverallPosition = 2 },
            ],
        }));

        CollectionAssert.AreEqual(new[] { "2", "3", "1" }, CarNumbers(vm));
    }

    [TestMethod]
    public void Cars_WithNoPositionYet_SortToTheBackOfTheGrid()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2")],
            CarPositions =
            [
                new CarPosition { Number = "1", OverallPosition = 0 },
                new CarPosition { Number = "2", OverallPosition = 4 },
            ],
        }));

        CollectionAssert.AreEqual(new[] { "2", "1" }, CarNumbers(vm));
    }

    #endregion

    #region Class colors

    private static Color ColorOf(CarViewModel car) => ((SolidColorBrush)car.ClassColor).Color;

    [TestMethod]
    public void ClassColor_ComesFromTheSessionPalette()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            ClassColors = new Dictionary<string, string> { ["GP1"] = "#FF0000" },
            EventEntries = [Entry("1", @class: "GP1")],
        }));

        Assert.AreEqual(Colors.Red, ColorOf(vm.Cars.Single()));
    }

    [TestMethod]
    public void ClassColor_UnknownClass_FallsBackToGray()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            ClassColors = new Dictionary<string, string> { ["GP1"] = "#FF0000" },
            EventEntries = [Entry("1", @class: "GP9")],
        }));

        Assert.AreEqual(Colors.Gray, ColorOf(vm.Cars.Single()));
    }

    [TestMethod]
    public void ClassColor_UnparseableColor_FallsBackToGray()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            ClassColors = new Dictionary<string, string> { ["GP1"] = "not a color" },
            EventEntries = [Entry("1", @class: "GP1")],
        }));

        Assert.AreEqual(Colors.Gray, ColorOf(vm.Cars.Single()));
    }

    [TestMethod]
    public void ClassColor_NoClass_IsTransparent()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1", @class: string.Empty)],
        }));

        Assert.AreEqual(Colors.Transparent, ColorOf(vm.Cars.Single()));
    }

    [TestMethod]
    public void ClassColor_IsSharedByEveryCarInTheClass()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            ClassColors = new Dictionary<string, string> { ["GP1"] = "#FF0000" },
            EventEntries = [Entry("1", @class: "GP1"), Entry("2", @class: "GP1")],
        }));

        // The point of the cache: one brush per class rather than one per row.
        Assert.AreSame(vm.Cars[0].ClassColor, vm.Cars[1].ClassColor);
    }

    /// <summary>
    /// The brushes are cached per class so every row does not allocate its own. A new palette has
    /// to invalidate that cache, or the grid keeps the previous event's colors.
    /// </summary>
    [TestMethod]
    public void ClassColor_NewPalette_ReplacesTheCachedBrush()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            ClassColors = new Dictionary<string, string> { ["GP1"] = "#FF0000" },
            EventEntries = [Entry("1", @class: "GP1")],
        }));
        Assert.AreEqual(Colors.Red, ColorOf(vm.Cars.Single()), "Sanity check.");

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            ClassColors = new Dictionary<string, string> { ["GP1"] = "#0000FF" },
            EventEntries = [Entry("1", @class: "GP1")],
        }));

        Assert.AreEqual(Colors.Blue, ColorOf(vm.Cars.Single()));
    }

    #endregion

    #region Session fields

    [TestMethod]
    public void SessionFields_AreTakenFromTheUpdate()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            SessionName = "Race 1",
            CurrentFlag = Flags.Green,
            TimeToGo = "01:30:00",
            RunningRaceTime = "00:30:00",
        }));

        Assert.AreEqual("Race 1", vm.SessionName);
        Assert.AreEqual(Flags.Green.ToString(), vm.Flag);
        Assert.AreEqual("01:30:00", vm.TimeToGo);
        Assert.AreEqual("00:30:00", vm.RaceTime);
    }

    [TestMethod]
    public void SessionFields_NotSent_KeepTheirPreviousValues()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            SessionName = "Race 1",
            CurrentFlag = Flags.Green,
            TimeToGo = "01:30:00",
        }));

        vm.ApplySessionUpdate(Update(new SessionStatePatch { RunningRaceTime = "00:31:00" }));

        Assert.AreEqual("Race 1", vm.SessionName);
        Assert.AreEqual(Flags.Green.ToString(), vm.Flag);
        Assert.AreEqual("01:30:00", vm.TimeToGo);
    }

    [TestMethod]
    public void LocalTimeOfDay_IsReformattedForDisplay()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch { LocalTimeOfDay = "14:05:09" }));

        Assert.AreEqual("2:05:09 PM", vm.LocalTime);
    }

    [TestMethod]
    public void LocalTimeOfDay_Unparseable_LeavesTheLastKnownTime()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { LocalTimeOfDay = "14:05:09" }));

        vm.ApplySessionUpdate(Update(new SessionStatePatch { LocalTimeOfDay = "garbage" }));

        Assert.AreEqual("2:05:09 PM", vm.LocalTime);
    }

    [TestMethod]
    public void TotalLaps_IsTheHighestLapCompletedInTheField()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2")],
            CarPositions =
            [
                new CarPosition { Number = "1", LastLapCompleted = 12 },
                new CarPosition { Number = "2", LastLapCompleted = 14 },
            ],
        }));

        Assert.AreEqual("14", vm.TotalLaps);
    }

    #endregion

    #region Car positions

    [TestMethod]
    public void CarPositions_ForAnUnknownCar_AreIgnored()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        // A position can arrive for a car that is not in the entry list. It has no row to land on.
        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1")],
            CarPositions =
            [
                new CarPosition { Number = "1", OverallPosition = 1 },
                new CarPosition { Number = "99", OverallPosition = 2 },
            ],
        }));

        Assert.AreEqual(1, vm.Cars.Count);
        Assert.AreEqual("1", vm.Cars.Single().Number);
    }

    [TestMethod]
    public void CarPositions_ReachTheRow()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();

        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1")],
            CarPositions =
            [
                new CarPosition
                {
                    Number = "1",
                    OverallPosition = 2,
                    ClassPosition = 1,
                    BestTime = "00:01:22.500",
                    LastLapCompleted = 11,
                    PenalityLaps = 1,
                },
            ],
        }));

        var car = vm.Cars.Single();
        Assert.AreEqual(2, car.OverallPosition);
        Assert.AreEqual(1, car.ClassPosition);
        Assert.AreEqual("00:01:22.500", car.BestTime);
        Assert.AreEqual(11, car.LastLap);
        Assert.AreEqual(1, car.PenaltyLaps);
    }

    #endregion
}
