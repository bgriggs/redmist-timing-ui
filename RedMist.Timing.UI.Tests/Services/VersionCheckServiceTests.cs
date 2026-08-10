using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon;

namespace RedMist.Timing.UI.Tests.Services;

/// <summary>
/// A wrong answer here is the most user-visible failure in the app: a false Mandatory blocks every
/// viewer out behind an update prompt they may not be able to satisfy.
/// </summary>
[TestClass]
public sealed class VersionCheckServiceTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Server:EventUrl"] = "https://example.invalid/events",
            ["Keycloak:AuthServerUrl"] = "https://example.invalid/auth",
            ["Keycloak:Realm"] = "test",
            ["Keycloak:ClientId"] = "test-client",
            ["Keycloak:ClientSecret"] = "test-secret",
            ["VersionCheck:iOSAppStoreUrl"] = "https://apps.apple.com/test",
            ["VersionCheck:AndroidPlayStoreUrl"] = "https://play.google.com/test",
        })
        .Build();

    // Shared: each EventClient builds a RestClient with its own HttpMessageHandler that nothing
    // disposes. CheckVersion is pure, so one instance serves every test.
    private static readonly VersionCheckService Service = CreateService();

    private static VersionCheckService CreateService()
    {
        var configuration = Configuration();
        // CheckVersion doesn't touch the client, but the constructor requires one.
        var eventClient = new EventClient(configuration, NullLoggerFactory.Instance,
            new EventAccessCodeStore(new MockPreferencesService()));
        return new VersionCheckService(eventClient, new UpdateMessageService(configuration),
            NullLogger<VersionCheckService>.Instance);
    }

    private static UIVersionInfo AndroidInfo(string minimum, string latest, bool mandatory, bool recommend)
        => new()
        {
            MinimumAndroidVersion = minimum,
            LatestAndroidVersion = latest,
            IsAndroidMinimumMandatory = mandatory,
            RecommendAndroidUpdate = recommend,
        };

    [TestMethod]
    public void UpToDate_RequiresNothing()
    {
        var result = Service.CheckVersion(new Version(2, 0, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreEqual(UpdateRequirement.None, result.Requirement);
    }

    [TestMethod]
    public void BelowMinimum_WithTheMandatoryFlag_IsMandatory()
    {
        var result = Service.CheckVersion(new Version(0, 9, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreEqual(UpdateRequirement.Mandatory, result.Requirement);
    }

    [TestMethod]
    public void BelowMinimum_WithoutTheMandatoryFlag_IsOnlyOptional()
    {
        // The server owns the decision: without the flag, being below the minimum never blocks.
        // It still comes out Optional here, but because it is behind the *latest* version.
        var result = Service.CheckVersion(new Version(0, 9, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: false, recommend: false), AppPlatform.Android);

        Assert.AreEqual(UpdateRequirement.Optional, result.Requirement);
    }

    [TestMethod]
    public void ExactlyAtMinimum_IsNotMandatory()
    {
        var result = Service.CheckVersion(new Version(1, 0, 0),
            AndroidInfo("1.0.0", "1.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreEqual(UpdateRequirement.None, result.Requirement);
    }

    [TestMethod]
    public void BelowLatest_IsOptional()
    {
        var result = Service.CheckVersion(new Version(1, 5, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreEqual(UpdateRequirement.Optional, result.Requirement);
    }

    [TestMethod]
    public void TheRecommendFlagAloneMakesItOptional()
    {
        var result = Service.CheckVersion(new Version(2, 0, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: false, recommend: true), AppPlatform.Android);

        Assert.AreEqual(UpdateRequirement.Optional, result.Requirement);
    }

    [TestMethod]
    public void AheadOfLatest_RequiresNothing()
    {
        // Internal builds run ahead of what the store reports.
        var result = Service.CheckVersion(new Version(3, 0, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreEqual(UpdateRequirement.None, result.Requirement);
    }

    [TestMethod]
    public void UnparseableMinimum_NeverBlocksTheUser()
    {
        var result = Service.CheckVersion(new Version(0, 1, 0),
            AndroidInfo("not-a-version", "2.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreNotEqual(UpdateRequirement.Mandatory, result.Requirement);
    }

    [TestMethod]
    public void MissingVersionStrings_RequireNothing()
    {
        var result = Service.CheckVersion(new Version(1, 0, 0),
            AndroidInfo(string.Empty, string.Empty, mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreEqual(UpdateRequirement.None, result.Requirement);
    }

    [TestMethod]
    public void PlatformsAreReadIndependently()
    {
        // An Android-mandatory release must not block iOS viewers.
        var info = new UIVersionInfo
        {
            MinimumAndroidVersion = "5.0.0",
            IsAndroidMinimumMandatory = true,
            MinimumIOSVersion = "1.0.0",
            IsIOSMinimumMandatory = true,
            LatestIOSVersion = "1.0.0",
        };

        var android = Service.CheckVersion(new Version(1, 0, 0), info, AppPlatform.Android);
        var ios = Service.CheckVersion(new Version(1, 0, 0), info, AppPlatform.iOS);

        Assert.AreEqual(UpdateRequirement.Mandatory, android.Requirement);
        Assert.AreEqual(UpdateRequirement.None, ios.Requirement);
    }

    [TestMethod]
    public void DesktopHasNoVersionRulesAndIsNeverBlocked()
    {
        var info = new UIVersionInfo
        {
            MinimumAndroidVersion = "5.0.0",
            IsAndroidMinimumMandatory = true,
        };

        var result = Service.CheckVersion(new Version(1, 0, 0), info, AppPlatform.Desktop);

        Assert.AreEqual(UpdateRequirement.None, result.Requirement);
    }

    [TestMethod]
    public void AnUpdateResultCarriesAMessageAndAStoreLink()
    {
        var result = Service.CheckVersion(new Version(0, 9, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Message));
        Assert.AreEqual("https://play.google.com/test", result.ActionUrl);
    }

    [TestMethod]
    public void ANoneResultCarriesNoMessageOrLink()
    {
        var result = Service.CheckVersion(new Version(2, 0, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreEqual(string.Empty, result.Message);
        Assert.IsNull(result.ActionUrl);
    }

    [TestMethod]
    public void TheResultEchoesTheVersionsItCompared()
    {
        var result = Service.CheckVersion(new Version(1, 5, 0),
            AndroidInfo("1.0.0", "2.0.0", mandatory: true, recommend: false), AppPlatform.Android);

        Assert.AreEqual(new Version(1, 5, 0), result.CurrentVersion);
        Assert.AreEqual(new Version(1, 0, 0), result.MinimumVersion);
        Assert.AreEqual(new Version(2, 0, 0), result.LatestVersion);
        Assert.AreEqual(AppPlatform.Android, result.Platform);
    }

    [TestMethod]
    public void BrowserUpdatesHaveNoStoreLink()
    {
        var info = new UIVersionInfo
        {
            MinimumWebVersion = "1.0.0",
            LatestWebVersion = "2.0.0",
            IsWebMinimumMandatory = true,
        };

        var result = Service.CheckVersion(new Version(0, 9, 0), info, AppPlatform.Browser);

        Assert.AreEqual(UpdateRequirement.Mandatory, result.Requirement);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Message));
        Assert.IsNull(result.ActionUrl);
    }
}
