using RedMist.Timing.UI.Clients;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignOrganizationClient : OrganizationClient
{
    public DesignOrganizationClient() : base(new DesignConfiguration())
    {
    }

    public override Task<byte[]> GetOrganizationIconAsync(int organizationId)
    {
        return Task.FromResult(System.Array.Empty<byte>());
    }
}
