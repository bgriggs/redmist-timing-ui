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
                // FlagsViewModel throws from its constructor without this one, which takes down the
                // whole of MainViewModel.SetupForEventAsync partway through - leaving a view model
                // that looks half-built rather than one that failed.
                ["Cdn:ArchiveUrl"] = "http://localhost/archive",
                ["Cdn:BaseUrl"] = "http://localhost/cdn",
                ["Cdn:Logos"] = "http://localhost/cdn/logos",
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
        var restClientFactory = new RestClientFactory(configuration);
        var loggerFactory = new DebugLoggerFactory();
        var accessCodeStore = new EventAccessCodeStore(new MockPreferencesService());

        return new CarViewModel(
            new Event { EventId = 1 },
            new EventClient(restClientFactory, loggerFactory, accessCodeStore),
            new HubClient(loggerFactory, configuration, accessCodeStore),
            pitTracking,
            new ViewSizeService(),
            new DesignHttpClientFactory(),
            configuration,
            loggerFactory);
    }

    internal static LiveTimingViewModel CreateLiveTiming() => CreateLiveTiming(hubClient: null, serverClient: null);

    /// <summary>
    /// Builds the live timing view model, optionally against a caller-supplied hub and event client
    /// so a test can stand in for the server and for the state of the hub subscription.
    /// </summary>
    internal static LiveTimingViewModel CreateLiveTiming(HubClient? hubClient, EventClient? serverClient)
    {
        var configuration = CreateConfiguration();
        var restClientFactory = new RestClientFactory(configuration);
        var loggerFactory = new DebugLoggerFactory();
        var httpClientFactory = new DesignHttpClientFactory();
        var accessCodeStore = new EventAccessCodeStore(new MockPreferencesService());
        var sponsorIconCache = new SponsorIconCacheService(httpClientFactory, loggerFactory);
        var sponsorClient = new SponsorClient(restClientFactory, httpClientFactory);
        var sponsorRotator = new SponsorRotatorViewModel(
            new SponsorsService(sponsorClient, sponsorIconCache, loggerFactory),
            sponsorIconCache,
            sponsorClient,
            loggerFactory);

        return new LiveTimingViewModel(
            hubClient ?? new HubClient(loggerFactory, configuration, accessCodeStore),
            serverClient ?? new EventClient(restClientFactory, loggerFactory, accessCodeStore),
            loggerFactory,
            new ViewSizeService(),
            new EventContext(),
            httpClientFactory,
            configuration,
            new OrganizationIconCacheService(new OrganizationClient(configuration, httpClientFactory, restClientFactory), loggerFactory),
            sponsorRotator)
        {
            EventModel = new Event { EventId = 1 },
            IsRealTime = false,
        };
    }

    internal static InCarSettingsViewModel CreateInCarSettings()
    {
        var configuration = CreateConfiguration();
        var restClientFactory = new RestClientFactory(configuration);
        var loggerFactory = new DebugLoggerFactory();
        var accessCodeStore = new EventAccessCodeStore(new MockPreferencesService());

        return new InCarSettingsViewModel(
            new EventClient(restClientFactory, loggerFactory, accessCodeStore),
            new HubClient(loggerFactory, configuration, accessCodeStore),
            accessCodeStore,
            new MockPreferencesService(),
            new NoOpScreenWakeService(),
            loggerFactory);
    }

    /// <summary>
    /// Builds the main view model, optionally against a caller-supplied event client and access code
    /// store so a test can stand in for the server.
    /// </summary>
    /// <remarks>
    /// The store has to be passed in alongside a substitute client, not just to the view model:
    /// the two have to agree on what code is held, because that is what the client keys its answers
    /// off and what the view model writes when a code is accepted.
    /// </remarks>
    internal static MainViewModel CreateMain(EventClient? eventClient = null, EventAccessCodeStore? accessCodeStore = null)
    {
        var configuration = CreateConfiguration();
        var restClientFactory = new RestClientFactory(configuration);
        var loggerFactory = new DebugLoggerFactory();
        var httpClientFactory = new DesignHttpClientFactory();
        accessCodeStore ??= new EventAccessCodeStore(new MockPreferencesService());
        eventClient ??= new EventClient(restClientFactory, loggerFactory, accessCodeStore);
        var hubClient = new HubClient(loggerFactory, configuration, accessCodeStore);
        var organizationClient = new OrganizationClient(configuration, httpClientFactory, restClientFactory);
        var iconCacheService = new OrganizationIconCacheService(organizationClient, loggerFactory);
        var sponsorIconCache = new SponsorIconCacheService(httpClientFactory, loggerFactory);
        var sponsorClient = new SponsorClient(restClientFactory, httpClientFactory);
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
