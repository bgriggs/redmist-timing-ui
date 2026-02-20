using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignMainViewModel : MainViewModel
{
    public DesignMainViewModel() :
        base(new EventsListViewModel(new Clients.EventClient(new DesignConfiguration(), new DebugLoggerFactory()), 
            new DesignOrganizationClient(), new DesignOrganizationIconCacheService(), new DebugLoggerFactory()), 
            new DesignLiveTimingViewModel(), new DesignHubClient(), new DesignEventClient(new DesignConfiguration()), 
            new DebugLoggerFactory(), new Services.ViewSizeService(), new EventContext(),
            new DesignPlatformDetectionService(), new DesignVersionCheckService(), new DesignHttpClientFactory(), 
            new DesignConfiguration(), new DesignOrganizationIconCacheService(), 
            new SponsorRotatorViewModel(new SponsorsService(new DesignSponsorClient(), new SponsorIconCacheService(new DesignHttpClientFactory(), new DebugLoggerFactory()), new DebugLoggerFactory()), new SponsorIconCacheService(new DesignHttpClientFactory(), new DebugLoggerFactory()), new DesignSponsorClient(), new DebugLoggerFactory()),
            new MockPreferencesService(), new NoOpScreenWakeService())
    {
        IsContentVisible = true;
    }
}
