using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.ViewModels.Design;
using RedMist.TimingCommon.Models;
// Not imported wholesale: RedMist.Timing.UI.ViewModels.InCarDriverMode declares its own CarViewModel,
// which would collide with the timing grid's.
using InCarSettingsViewModel = RedMist.Timing.UI.ViewModels.InCarDriverMode.InCarSettingsViewModel;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Builds the timing view models against real clients pointed at unreachable URLs.
/// </summary>
/// <remarks>
/// Constructing these makes no request, but driving them can. In particular, anything that routes
/// to "EventsList" makes <see cref="MainViewModel"/> start a real background event load against
/// localhost, which the client retries five times with backoff. It is fire-and-forget on a
/// threadpool thread, so it cannot hang a test - it just runs on after the test finishes.
///
/// Two other traps for tests written against these:
/// <list type="bullet">
/// <item>Each factory method builds its own clients, so the ones inside <c>CreateMain</c>'s live
/// timing view model are different instances from the ones handed to the main view model. In the
/// app they are DI singletons. Do not assert that leaving an event unsubscribes a shared hub.</item>
/// <item><c>CreateLiveTiming</c> sets <c>IsRealTime = false</c>, so that view model ignores every
/// notification sent through the messenger. Drive it by calling ApplySessionUpdate directly.</item>
/// </list>
/// </remarks>
internal static class TestViewModelFactory
{
    internal static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Server:EventUrl"] = "http://localhost/event",
                ["Server:OrganizationUrl"] = "http://localhost/organization",
                ["Server:SponsorUrl"] = "http://localhost/sponsor",
                ["Hub:Url"] = "http://localhost/hub",
                ["Keycloak:AuthServerUrl"] = "http://localhost/auth",
                ["Keycloak:Realm"] = "test",
                ["Keycloak:ClientId"] = "test",
                ["Keycloak:ClientSecret"] = "test",
            })
            .Build();
    }

    /// <summary>
    /// Builds a car row.
    /// </summary>
    /// <remarks>
    /// ApplyPatch schedules Rx timers that write back to the row from a threadpool thread after it
    /// returns: the row-flash background at 80ms and 900ms when the lap changes, and ForcePropertyChange
    /// at 500ms on every patch. ForcePropertyChange momentarily blanks <c>Position</c> and
    /// <c>PositionsGainedLost</c>, so do not assert those - or <c>RowBackgroundKey</c> - on a row that
    /// has been patched, unless the assertion runs in the same synchronous block as the patch.
    /// </remarks>
    internal static CarViewModel CreateCar() => CreateCar(new PitTracking());

    /// <summary>
    /// Builds a car row against a caller-supplied <see cref="PitTracking"/>, so a test can observe
    /// the pit stops the patch records.
    /// </summary>
    internal static CarViewModel CreateCar(PitTracking pitTracking)
    {
        var configuration = CreateConfiguration();
        var loggerFactory = new DebugLoggerFactory();
        var accessCodeStore = new EventAccessCodeStore(new MockPreferencesService());

        return new CarViewModel(
            new Event { EventId = 1 },
            new EventClient(configuration, loggerFactory, accessCodeStore),
            new HubClient(loggerFactory, configuration, accessCodeStore),
            pitTracking,
            new ViewSizeService(),
            new DesignHttpClientFactory(),
            configuration,
            loggerFactory);
    }

    internal static LiveTimingViewModel CreateLiveTiming()
    {
        var configuration = CreateConfiguration();
        var loggerFactory = new DebugLoggerFactory();
        var httpClientFactory = new DesignHttpClientFactory();
        var accessCodeStore = new EventAccessCodeStore(new MockPreferencesService());
        var sponsorIconCache = new SponsorIconCacheService(httpClientFactory, loggerFactory);
        var sponsorClient = new SponsorClient(configuration, httpClientFactory);
        var sponsorRotator = new SponsorRotatorViewModel(
            new SponsorsService(sponsorClient, sponsorIconCache, loggerFactory),
            sponsorIconCache,
            sponsorClient,
            loggerFactory);

        return new LiveTimingViewModel(
            new HubClient(loggerFactory, configuration, accessCodeStore),
            new EventClient(configuration, loggerFactory, accessCodeStore),
            loggerFactory,
            new ViewSizeService(),
            new EventContext(),
            httpClientFactory,
            configuration,
            new OrganizationIconCacheService(new OrganizationClient(configuration, httpClientFactory), loggerFactory),
            sponsorRotator)
        {
            EventModel = new Event { EventId = 1 },
            IsRealTime = false,
        };
    }

    internal static InCarSettingsViewModel CreateInCarSettings()
    {
        var configuration = CreateConfiguration();
        var loggerFactory = new DebugLoggerFactory();
        var accessCodeStore = new EventAccessCodeStore(new MockPreferencesService());

        return new InCarSettingsViewModel(
            new EventClient(configuration, loggerFactory, accessCodeStore),
            new HubClient(loggerFactory, configuration, accessCodeStore),
            accessCodeStore,
            new MockPreferencesService(),
            new NoOpScreenWakeService(),
            loggerFactory);
    }

    internal static MainViewModel CreateMain()
    {
        var configuration = CreateConfiguration();
        var loggerFactory = new DebugLoggerFactory();
        var httpClientFactory = new DesignHttpClientFactory();
        var accessCodeStore = new EventAccessCodeStore(new MockPreferencesService());
        var eventClient = new EventClient(configuration, loggerFactory, accessCodeStore);
        var hubClient = new HubClient(loggerFactory, configuration, accessCodeStore);
        var organizationClient = new OrganizationClient(configuration, httpClientFactory);
        var iconCacheService = new OrganizationIconCacheService(organizationClient, loggerFactory);
        var sponsorIconCache = new SponsorIconCacheService(httpClientFactory, loggerFactory);
        var sponsorClient = new SponsorClient(configuration, httpClientFactory);
        var sponsorRotator = new SponsorRotatorViewModel(
            new SponsorsService(sponsorClient, sponsorIconCache, loggerFactory),
            sponsorIconCache,
            sponsorClient,
            loggerFactory);

        return new MainViewModel(
            new EventsListViewModel(eventClient, organizationClient, iconCacheService, loggerFactory),
            CreateLiveTiming(),
            hubClient,
            eventClient,
            loggerFactory,
            new ViewSizeService(),
            new EventContext(),
            new DesignPlatformDetectionService(),
            new DesignVersionCheckService(),
            httpClientFactory,
            configuration,
            iconCacheService,
            sponsorRotator,
            new MockPreferencesService(),
            new NoOpScreenWakeService(),
            accessCodeStore);
    }
}
