using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.ViewModels.Design;

class DesignHubClient : HubClient
{
    public DesignHubClient() : base(new DebugLoggerFactory(), new DesignConfiguration(), new EventAccessCodeStore(new MockPreferencesService()))
    {

    }
}
