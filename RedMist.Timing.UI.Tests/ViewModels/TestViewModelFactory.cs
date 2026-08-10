using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.Timing.UI.ViewModels.Design;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Builds the timing view models against real clients pointed at unreachable URLs. Nothing here
/// makes a request - the clients only need to construct so the view models under test can run.
/// </summary>
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

    internal static CarViewModel CreateCar()
    {
        var configuration = CreateConfiguration();
        var loggerFactory = new DebugLoggerFactory();
        var accessCodeStore = new EventAccessCodeStore(new MockPreferencesService());

        return new CarViewModel(
            new Event { EventId = 1 },
            new EventClient(configuration, loggerFactory, accessCodeStore),
            new HubClient(loggerFactory, configuration, accessCodeStore),
            new PitTracking(),
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
}
