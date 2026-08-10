using RedMist.Timing.UI.Utilities;
using RedMist.Timing.UI.ViewModels;

namespace RedMist.Timing.UI.Tests.Utilities;

[TestClass]
public sealed class RaceTimeParserTests
{
    [TestMethod]
    public void WithMilliseconds_Parses()
    {
        Assert.AreEqual(new TimeSpan(0, 1, 23, 45, 678), RaceTimeParser.Parse("01:23:45.678"));
    }

    [TestMethod]
    public void WithoutMilliseconds_Parses()
    {
        Assert.AreEqual(new TimeSpan(1, 23, 45), RaceTimeParser.Parse("01:23:45"));
    }

    [TestMethod]
    public void Zero_Parses()
    {
        Assert.AreEqual(TimeSpan.Zero, RaceTimeParser.Parse("00:00:00.000"));
    }

    /// <summary>
    /// The whole reason this parser exists. TimeSpan.TryParseExact's "hh" specifier rejects 24 and
    /// above, which silently zeroed every race clock for the back half of an endurance event.
    /// </summary>
    [TestMethod]
    [DataRow("25:00:00.000", 25, 0, 0)]
    [DataRow("48:00:15.989", 48, 0, 15)]
    [DataRow("100:30:00", 100, 30, 0)]
    public void HoursBeyondADay_Parse(string input, int hours, int minutes, int seconds)
    {
        var parsed = RaceTimeParser.Parse(input);

        Assert.AreEqual(hours, (int)parsed.TotalHours);
        Assert.AreEqual(minutes, parsed.Minutes);
        Assert.AreEqual(seconds, parsed.Seconds);
    }

    [TestMethod]
    public void SingleDigitHours_Parse()
    {
        Assert.AreEqual(new TimeSpan(1, 2, 3), RaceTimeParser.Parse("1:02:03"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("garbage")]
    [DataRow("1:23")]
    [DataRow("::")]
    public void Unparseable_FallsBackToZero(string input)
    {
        Assert.AreEqual(TimeSpan.Zero, RaceTimeParser.Parse(input));
    }

    [TestMethod]
    public void Null_FallsBackToZero()
    {
        Assert.AreEqual(TimeSpan.Zero, RaceTimeParser.Parse(null));
    }

    [TestMethod]
    public void TryParse_ReportsFailureWithoutThrowing()
    {
        Assert.IsFalse(RaceTimeParser.TryParse("nonsense", out var result));
        Assert.AreEqual(default, result);
    }

    [TestMethod]
    public void ExtraFractionalDigits_AreRoundedToMilliseconds()
    {
        Assert.AreEqual(new TimeSpan(0, 1, 23, 45, 679), RaceTimeParser.Parse("01:23:45.6789"));
    }

    [TestMethod]
    public void ParseRMTime_DelegatesToTheSharedParser()
    {
        // CarViewModel.UpdateProjectedLapTimeProgression drives the lap-progress bar from this.
        Assert.AreEqual(RaceTimeParser.Parse("30:00:00.000"), LiveTimingViewModel.ParseRMTime("30:00:00.000"));
        Assert.AreEqual(new TimeSpan(30, 0, 0), LiveTimingViewModel.ParseRMTime("30:00:00.000"));
    }
}
