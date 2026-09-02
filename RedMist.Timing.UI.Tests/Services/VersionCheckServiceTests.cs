using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon;
using System.Diagnostics;

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

    // Shared: each RestClientFactory built here owns an HttpMessageHandler that nothing disposes.
    // CheckVersion is pure, so one instance serves every test.
    private static readonly VersionCheckService Service = CreateService();

    private static VersionCheckService CreateService(EventClient eventClient)
        => new(eventClient, new UpdateMessageService(Configuration()), NullLogger<VersionCheckService>.Instance);

    private static VersionCheckService CreateService()
    {
        var configuration = Configuration();
        // CheckVersion doesn't touch the client, but the constructor requires one.
        var eventClient = new EventClient(new RestClientFactory(configuration), NullLoggerFactory.Instance,
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

    /// <summary>
    /// A version check that cannot reach the server has to come back as null rather than throw:
    /// the caller treats null as "skip the check and carry on", and an exception escaping here
    /// would take out startup over an optional call.
    /// </summary>
    [TestMethod]
    public async Task AFailedRequestComesBackAsNull()
    {
        var service = CreateService(new StubEventClient(_ => throw new HttpRequestException("gateway timeout")));

        Assert.IsNull(await service.GetVersionInfoAsync(timeoutSeconds: 5));
    }

    /// <summary>
    /// The shape RestSharp actually throws when the request is canceled: not an
    /// OperationCanceledException, but an HttpRequestException wrapping one.
    /// </summary>
    [TestMethod]
    public async Task AWrappedCancellationComesBackAsNull()
    {
        var service = CreateService(new StubEventClient(_
            => throw new HttpRequestException("Request aborted", new TaskCanceledException())));

        Assert.IsNull(await service.GetVersionInfoAsync(timeoutSeconds: 5));
    }

    // Timeout guarded: if the cancellation stops reaching the request, the stub waits forever, and
    // a regression should fail the run rather than hang it.
    [TestMethod]
    [Timeout(15000)]
    public async Task ARequestSlowerThanTheTimeoutComesBackAsNull()
    {
        var stub = new StubEventClient(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        });
        var service = CreateService(stub);

        Assert.IsNull(await service.GetVersionInfoAsync(timeoutSeconds: 1));

        // The cancellation has to reach the request, or it is left running with nobody to await it
        // and a late failure arrives at TaskScheduler.UnobservedTaskException to be reported as a
        // crash. Awaited rather than sampled: the request sees the cancellation on its own turn,
        // which can land just after the outer wait above has already given up.
        await stub.Canceled.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// The token does not reach RestSharp's authenticator, and the Keycloak token request behind
    /// it takes none, so the request can ignore the deadline entirely. This has to be bounded
    /// anyway: it runs before anything else in MainViewModel.Initialize, so an auth endpoint that
    /// stalls would otherwise hold the user on the loading screen for HttpClient's 100 seconds.
    /// </summary>
    [TestMethod]
    [Timeout(30000)]
    public async Task ARequestThatIgnoresTheDeadlineIsStillGivenUpOn()
    {
        var service = CreateService(new StubEventClient(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            return null;
        }));

        var elapsed = Stopwatch.StartNew();
        Assert.IsNull(await service.GetVersionInfoAsync(timeoutSeconds: 1));
        elapsed.Stop();

        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(10),
            $"should have given up near the 1 second deadline, took {elapsed.Elapsed}");
    }

    [TestMethod]
    public async Task ASuccessfulRequestIsReturnedAsIs()
    {
        var info = AndroidInfo("1.0.0", "2.0.0", mandatory: false, recommend: false);
        var service = CreateService(new StubEventClient(_ => Task.FromResult<UIVersionInfo?>(info)));

        Assert.AreSame(info, await service.GetVersionInfoAsync(timeoutSeconds: 5));
    }

    /// <summary>
    /// Stands in for the server so the timeout and failure paths can be exercised without one.
    /// </summary>
    private sealed class StubEventClient(Func<CancellationToken, Task<UIVersionInfo?>> respond)
        : EventClient(new RestClientFactory(Configuration()), NullLoggerFactory.Instance,
            new EventAccessCodeStore(new MockPreferencesService()))
    {
        private readonly TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once the request has seen its cancellation token fire.</summary>
        public Task Canceled => canceled.Task;

        public override async Task<UIVersionInfo?> LoadUIVersionInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await respond(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    canceled.TrySetResult();
                }
                throw;
            }
        }
    }
}
