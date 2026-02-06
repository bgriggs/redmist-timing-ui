using RedMist.Timing.UI.Clients;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignOrganizationClient : OrganizationClient
{
    public DesignOrganizationClient() : base(new DesignConfiguration(), new DesignHttpClientFactory())
    {
    }

    //public override Task<byte[]> GetOrganizationIconAsync(int organizationId)
    //{
    //    return Task.FromResult(System.Array.Empty<byte>());
    //}

    private class DesignHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }
}
