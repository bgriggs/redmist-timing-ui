using RedMist.Timing.UI.ViewModels;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers which rows end up on a shared card, and in what order.
/// </summary>
/// <remarks>
/// The card promises to show what the viewer is looking at, in the order they are looking at it -
/// honoring whatever search, grouping or sort they have applied. That promise is kept by handing this
/// the rows the view says are on screen and having it change nothing about their order, so these tests
/// are mostly about what it does <em>not</em> do.
/// </remarks>
[TestClass]
public sealed class ShareStandingsTests
{
    private static CarViewModel Car(string number, int position, string name = "", string carClass = "",
        int lastLap = 0, string? bestTime = null)
    {
        var car = TestViewModelFactory.CreateCar();
        car.Number = number;
        car.OverallPosition = position;
        car.OriginalName = name;
        car.Class = carClass;
        car.LastLap = lastLap;
        car.BestTime = bestTime;
        return car;
    }

    private static CarViewModel[] Field(int count) =>
        [.. Enumerable.Range(1, count).Select(i => Car(i.ToString(), i))];

    [TestMethod]
    public void KeepsTheOrderItIsGiven()
    {
        // Sorted by fastest lap, say: the running order on screen is not the position order, and the
        // card is meant to show what is on screen.
        var field = Field(4);
        var onScreen = new[] { field[2], field[0], field[3], field[1] };

        var selection = ShareStandings.Select(onScreen, field, highlightCarNumber: null);

        CollectionAssert.AreEqual(new[] { "3", "1", "4", "2" },
            selection.Rows.Select(r => r.CarNumber).ToArray());
        Assert.AreEqual(0, selection.Omitted);
    }

    [TestMethod]
    public void OnlyWhatIsOnScreen_NotTheWholeField()
    {
        var field = Field(20);
        var onScreen = new[] { field[5], field[6] };

        var selection = ShareStandings.Select(onScreen, field, highlightCarNumber: null);

        CollectionAssert.AreEqual(new[] { "6", "7" }, selection.Rows.Select(r => r.CarNumber).ToArray());
        Assert.AreEqual(0, selection.Omitted, "Nothing was left out - only two cars were on screen.");
    }

    [TestMethod]
    public void MoreThanFitsOnACard_IsCappedAndCounted()
    {
        var field = Field(30);

        var selection = ShareStandings.Select(field, field, highlightCarNumber: null);

        Assert.AreEqual(8, selection.Rows.Count);
        Assert.AreEqual(22, selection.Omitted);
        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5", "6", "7", "8" },
            selection.Rows.Select(r => r.CarNumber).ToArray());
    }

    [TestMethod]
    public void ExactlyWhatFits_LeavesNothingOut()
    {
        var field = Field(8);

        var selection = ShareStandings.Select(field, field, highlightCarNumber: null);

        Assert.AreEqual(8, selection.Rows.Count);
        Assert.AreEqual(0, selection.Omitted);
    }

    [TestMethod]
    public void NothingOnScreen_YieldsNoRows()
    {
        var field = Field(3);

        var selection = ShareStandings.Select([], field, highlightCarNumber: null);

        Assert.AreEqual(0, selection.Rows.Count);
        Assert.AreEqual(0, selection.Omitted);
    }

    #region The shared car

    [TestMethod]
    public void TheSharedCar_AlreadyOnScreen_IsNotMovedOrDuplicated()
    {
        var field = Field(4);

        var selection = ShareStandings.Select(field, field, highlightCarNumber: "3");

        CollectionAssert.AreEqual(new[] { "1", "2", "3", "4" },
            selection.Rows.Select(r => r.CarNumber).ToArray());
    }

    /// <summary>
    /// Its own open details panel can push its row off the bottom of the screen, and the viewer can
    /// scroll away before pressing share - but a card shared from a car has to contain that car.
    /// </summary>
    [TestMethod]
    public void TheSharedCar_NotOnScreen_IsPulledIn()
    {
        var field = Field(20);
        var onScreen = new[] { field[10], field[11] };

        var selection = ShareStandings.Select(onScreen, field, highlightCarNumber: "3");

        CollectionAssert.AreEqual(new[] { "3", "11", "12" },
            selection.Rows.Select(r => r.CarNumber).ToArray());
    }

    [TestMethod]
    public void TheSharedCar_PulledIntoAFullScreen_CostsTheLastRow()
    {
        var field = Field(30);
        var onScreen = field.Skip(10).Take(8).ToArray();

        var selection = ShareStandings.Select(onScreen, field, highlightCarNumber: "3");

        Assert.AreEqual(8, selection.Rows.Count);
        Assert.AreEqual("3", selection.Rows[0].CarNumber);
        Assert.AreEqual(1, selection.Omitted, "One row was pushed off the card to make room.");
    }

    [TestMethod]
    public void TheSharedCar_NotInTheFieldAtAll_IsSimplyAbsent()
    {
        // A car that has left the entry list between the tap and the draw. Better a card without it
        // than no card.
        var field = Field(3);

        var selection = ShareStandings.Select(field, field, highlightCarNumber: "99");

        Assert.AreEqual(3, selection.Rows.Count);
    }

    #endregion

    [TestMethod]
    public void ACarRealizedTwice_AppearsOnce()
    {
        var field = Field(3);
        var onScreen = new[] { field[0], field[0], field[1] };

        var selection = ShareStandings.Select(onScreen, field, highlightCarNumber: null);

        CollectionAssert.AreEqual(new[] { "1", "2" }, selection.Rows.Select(r => r.CarNumber).ToArray());
    }

    [TestMethod]
    public void EachRow_CarriesWhatTheTableShows()
    {
        var car = Car("99", 3, name: "Round 3 Racing", carClass: "GP1", lastLap: 41, bestTime: "00:01:25.123");

        var row = ShareStandings.Select([car], [car], highlightCarNumber: null).Rows.Single();

        Assert.AreEqual(3, row.Position);
        Assert.AreEqual("99", row.CarNumber);
        Assert.AreEqual("ROUND 3 RACING", row.Name, "The grid upper-cases entry names.");
        Assert.AreEqual("GP1", row.ClassName);
        Assert.AreEqual(41, row.Laps);
        Assert.AreEqual("1:25.123", row.BestTime, "Shortened the way the table shows it.");
    }

    /// <summary>
    /// Sorting by fastest lap installs a position override on every row, and that override is the
    /// number on screen - so it is the number the card has to carry.
    /// </summary>
    [TestMethod]
    public void EachRow_TakesThePositionOnScreen_NotTheOverallOne()
    {
        var car = Car("99", 7);
        car.OverridePosition(2);

        var row = ShareStandings.Select([car], [car], highlightCarNumber: null).Rows.Single();

        Assert.AreEqual(2, row.Position);
    }

    [TestMethod]
    public void EachRow_GroupedByClass_TakesTheInClassPosition()
    {
        var car = Car("99", 7);
        car.ClassPosition = 2;
        car.CurrentGroupMode = GroupMode.Class;

        var row = ShareStandings.Select([car], [car], highlightCarNumber: null).Rows.Single();

        Assert.AreEqual(2, row.Position);
    }
}
