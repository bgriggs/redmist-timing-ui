using RedMist.Timing.UI.Utilities;

namespace RedMist.Timing.UI.Tests.Utilities;

/// <summary>
/// Covers the file name a shared card travels under.
/// </summary>
/// <remarks>
/// Event names are free text an organizer typed, and this name is what a recipient sees attached to
/// the message and what a save lands under - so anything a filesystem or a share target would refuse
/// has to be gone by the time it gets there.
/// </remarks>
[TestClass]
public sealed class ShareCardNameTests
{
    [TestMethod]
    public void JoinsThePartsAndLowerCasesThem()
    {
        Assert.AreEqual("red-mist-car-99-spring-sprints.png",
            ShareCardName.For("car-99", "Spring Sprints"));
    }

    [TestMethod]
    public void AnEventAlone_NeedsNoCar()
    {
        Assert.AreEqual("red-mist-spring-sprints.png", ShareCardName.For(null, "Spring Sprints"));
    }

    [TestMethod]
    [DataRow("Round 3: Mid-Ohio!", "red-mist-round-3-mid-ohio.png")]
    [DataRow("24 Hours of Lemons (2026)", "red-mist-24-hours-of-lemons-2026.png")]
    [DataRow("  spaced  out  ", "red-mist-spaced-out.png")]
    [DataRow("Grand Prix de Trois-Rivières", "red-mist-grand-prix-de-trois-rivi-res.png")]
    public void ReducesAnythingElseToDashes(string eventName, string expected)
    {
        Assert.AreEqual(expected, ShareCardName.For(null, eventName));
    }

    /// <summary>
    /// The case the dash collapsing is subtlest about: a part that contributes nothing sits between
    /// two that do, and must not leave a doubled dash behind it.
    /// </summary>
    [TestMethod]
    public void APartThatReducesToNothing_LeavesNoGap()
    {
        Assert.AreEqual("red-mist-a-b.png", ShareCardName.For("a", "***", "b"));
    }

    [TestMethod]
    public void NothingUsable_StillNamesAFile()
    {
        Assert.AreEqual("red-mist-timing.png", ShareCardName.For(null, "***"));
    }

    [TestMethod]
    public void NoPartsAtAll_StillNamesAFile()
    {
        Assert.AreEqual("red-mist-timing.png", ShareCardName.For());
    }

    [TestMethod]
    public void ALongEventName_IsCutShortWithoutATrailingDash()
    {
        var name = ShareCardName.For("car-99", new string('a', 200));

        Assert.IsTrue(name.StartsWith("red-mist-car-99-a", System.StringComparison.Ordinal), name);
        Assert.IsTrue(name.EndsWith(".png", System.StringComparison.Ordinal), name);
        // "red-mist-" + at most 60 slug characters + ".png".
        Assert.IsTrue(name.Length <= "red-mist-".Length + 60 + ".png".Length, $"{name} is {name.Length} long");
        Assert.IsFalse(name.Contains("-.png", System.StringComparison.Ordinal), name);
    }

    /// <summary>
    /// The cut lands wherever the length runs out, which can be in the middle of the dashes between
    /// two parts.
    /// </summary>
    [TestMethod]
    public void ACutThatLandsOnADash_DoesNotLeaveItDangling()
    {
        var name = ShareCardName.For(new string('a', 59), "Spring Sprints");

        Assert.AreEqual($"red-mist-{new string('a', 59)}.png", name);
    }
}
