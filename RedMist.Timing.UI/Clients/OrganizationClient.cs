using Microsoft.Extensions.Configuration;
using RestSharp;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

public class OrganizationClient : BaseRestClient
{
    private readonly IHttpClientFactory httpClientFactory;
    private readonly string cdnLogosUrl = "https://assets.redmist.racing/logos";

    public OrganizationClient(IConfiguration configuration, IHttpClientFactory httpClientFactory)
        : base(configuration, "Server:OrganizationUrl")
    {
        if (configuration["Cdn:BaseUrl"] != null && configuration["Cdn:Logos"] != null)
        {
            var baseUrl = configuration["Cdn:BaseUrl"]!.TrimEnd('/');
            var logosPath = configuration["Cdn:Logos"]!.TrimStart('/').TrimEnd('/');
            cdnLogosUrl = $"{baseUrl}/{logosPath}";
        }

        this.httpClientFactory = httpClientFactory;
    }

    public virtual async Task<byte[]> GetOrganizationIconAsync(int organizationId)
    {
        var request = new RestRequest("GetOrganizationIcon", Method.Get);
        request.AddQueryParameter("organizationId", organizationId.ToString());
        var response = await RestClient.ExecuteAsync(request);

        if (!response.IsSuccessful || response.RawBytes == null)
        {
            return [];
        }

        return response.RawBytes;
    }

    public virtual async Task<byte[]> GetOrganizationIconCdnAsync(int organizationId)
    {
        var httpClient = httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{cdnLogosUrl}/org-{organizationId}.img");
        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }
        return await response.Content.ReadAsByteArrayAsync();
    }
}
