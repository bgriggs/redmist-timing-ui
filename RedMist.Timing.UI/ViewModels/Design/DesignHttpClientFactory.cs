using System.Net.Http;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient();
    }
}
