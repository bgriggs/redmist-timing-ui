using RestSharp;

namespace RedMist.Timing.UI.Clients;

public abstract class BaseRestClient
{
    protected RestClient RestClient { get; }

    protected BaseRestClient(RestClientFactory restClientFactory, string serverUrlConfigKey)
    {
        RestClient = restClientFactory.Create(serverUrlConfigKey);
    }
}
