using RedMist.Timing.UI.Clients;

namespace RedMist.Timing.UI.ViewModels.Design;

class DesignHubClient : HubClient
{
    public DesignHubClient() : base(new DebugLoggerFactory(), new DesignConfiguration())
    {
        
    }
}
