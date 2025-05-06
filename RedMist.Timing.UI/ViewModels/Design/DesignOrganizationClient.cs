using RedMist.Timing.UI.Clients;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignOrganizationClient : OrganizationClient
{
    public DesignOrganizationClient() : base(new DesignConfiguration())
    {
    }
}
