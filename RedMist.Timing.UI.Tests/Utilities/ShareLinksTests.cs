using RedMist.Timing.UI.Utilities;

namespace RedMist.Timing.UI.Tests.Utilities;

/// <summary>
/// Covers the links the share buttons hand out.
/// </summary>
/// <remarks>
/// These are the one part of the feature a recipient sees, and the one part that has to agree with an
/// implementation in another repository - so the formats are pinned here rather than left to
/// inspection.
/// </remarks>
[TestClass]
public sealed class ShareLinksTests
{
    private const string Origin = "https://redmist.racing";

    #region Event links

    [TestMethod]
    public void EventUrl_Live_CarriesNoSession()
    {
        // No session id, deliberately: the link keeps following the event as sessions change.
        Assert.AreEqual("https://redmist.racing/timing/42?src=app-share",
            ShareLinks.EventUrl(Origin, 42, null));
    }

    [TestMethod]
    public void EventUrl_AStoredSession_KeepsIt()
    {
        Assert.AreEqual("https://redmist.racing/timing/42/7?src=app-share",
            ShareLinks.EventUrl(Origin, 42, 7));
    }

    /// <summary>
    /// The timing feed emits run number 0, so session 0 is a real session. A link built by testing the
    /// id for truthiness silently becomes a live link, which is what happened in the web build.
    /// </summary>
    [TestMethod]
    public void EventUrl_SessionZero_IsASession()
    {
        Assert.AreEqual("https://redmist.racing/timing/42/0?src=app-share",
            ShareLinks.EventUrl(Origin, 42, 0));
    }

    [TestMethod]
    public void EventUrl_ATrailingSlashOnTheOrigin_DoesNotDoubleUp()
    {
        Assert.AreEqual("https://redmist.racing/timing/42?src=app-share",
            ShareLinks.EventUrl("https://redmist.racing/", 42, null));
    }

    [TestMethod]
    public void EventUrl_ATestOrigin_StaysOnIt()
    {
        Assert.AreEqual("https://test.redmist.racing/timing/42?src=app-share",
            ShareLinks.EventUrl("https://test.redmist.racing", 42, null));
    }

    [TestMethod]
    public void EventUrl_NoOrigin_FallsBackToProduction()
    {
        Assert.AreEqual("https://redmist.racing/timing/42?src=app-share",
            ShareLinks.EventUrl("  ", 42, null));
    }

    #endregion

    #region Car links

    [TestMethod]
    public void CarUrl_NamesTheCarAndTheSource()
    {
        Assert.AreEqual("https://redmist.racing/timing/42?car=99&src=app-share",
            ShareLinks.CarUrl(Origin, 42, null, "99"));
    }

    [TestMethod]
    public void CarUrl_AStoredSession_KeepsIt()
    {
        Assert.AreEqual("https://redmist.racing/timing/42/0?car=99&src=app-share",
            ShareLinks.CarUrl(Origin, 42, 0, "99"));
    }

    /// <summary>
    /// Car numbers are free text from the timing feed and do turn up with letters and punctuation in
    /// them, so the value has to survive the trip as itself.
    /// </summary>
    [TestMethod]
    [DataRow("99X", "99X")]
    [DataRow("7 ", "7%20")]
    [DataRow("A+B", "A%2BB")]
    [DataRow("1/2", "1%2F2")]
    [DataRow("#4", "%234")]
    public void CarUrl_EncodesTheCarNumber(string carNumber, string expected)
    {
        Assert.AreEqual($"https://redmist.racing/timing/42?car={expected}&src=app-share",
            ShareLinks.CarUrl(Origin, 42, null, carNumber));
    }

    #endregion

    #region The host the card prints

    /// <summary>
    /// The card names a site in its footer, and it has to be the one the link points at - a build
    /// aimed at a test site handing out a card advertising production is worse than either alone.
    /// </summary>
    [TestMethod]
    [DataRow("https://redmist.racing", "redmist.racing")]
    [DataRow("https://test.redmist.racing/", "test.redmist.racing")]
    [DataRow("http://localhost:4200", "localhost")]
    public void HostOf_NamesTheSite(string origin, string expected)
    {
        Assert.AreEqual(expected, ShareLinks.HostOf(origin));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not a url")]
    public void HostOf_SomethingUnusable_FallsBackToProduction(string origin)
    {
        Assert.AreEqual("redmist.racing", ShareLinks.HostOf(origin));
    }

    #endregion

    #region Share text

    [TestMethod]
    public void CarShareTitle_NamesThePositionInClass()
    {
        Assert.AreEqual("Car #99 - P3 in GP1", ShareLinks.CarShareTitle("99", 3, "GP1"));
    }

    [TestMethod]
    [DataRow(0, "GP1", DisplayName = "No position yet")]
    [DataRow(3, "", DisplayName = "No class")]
    public void CarShareTitle_WithNothingToSayAboutPosition_SaysNothing(int classPosition, string className)
    {
        Assert.AreEqual("Car #99", ShareLinks.CarShareTitle("99", classPosition, className));
    }

    [TestMethod]
    public void CarShareText_Live_ReadsAsAStandingFactAboutTheCar()
    {
        // Phrased this way on purpose: the position is a snapshot taken when the button was pressed,
        // while the link it travels with stays live.
        Assert.AreEqual("Car #99 - P3 in GP1\nSpring Sprints - live timing on Red Mist",
            ShareLinks.CarShareText("99", 3, "GP1", "Spring Sprints", isLive: true));
    }

    [TestMethod]
    public void CarShareText_Results_SaysSo()
    {
        Assert.AreEqual("Car #99 - P3 in GP1\nSpring Sprints - results on Red Mist",
            ShareLinks.CarShareText("99", 3, "GP1", "Spring Sprints", isLive: false));
    }

    [TestMethod]
    public void CarShareText_NoEventName_LeavesItOut()
    {
        Assert.AreEqual("Car #99\nlive timing on Red Mist",
            ShareLinks.CarShareText("99", 0, "", "", isLive: true));
    }

    [TestMethod]
    public void EventShareText_Live_InvitesFollowing()
    {
        Assert.AreEqual("Spring Sprints - follow live timing on Red Mist",
            ShareLinks.EventShareText("Spring Sprints", isLive: true));
    }

    [TestMethod]
    public void EventShareText_Results_DoesNot()
    {
        Assert.AreEqual("Spring Sprints - results on Red Mist",
            ShareLinks.EventShareText("Spring Sprints", isLive: false));
    }

    [TestMethod]
    public void EventShareText_NoEventName_StillReads()
    {
        Assert.AreEqual("This event - follow live timing on Red Mist",
            ShareLinks.EventShareText("", isLive: true));
    }

    #endregion
}
