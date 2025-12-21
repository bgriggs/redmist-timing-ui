using BigMission.Shared.Auth;
using BigMission.Shared.RestSharp;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

public class OrganizationClient
{
    private readonly RestClient restClient;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly string cdnLogosUrl = "https://assets.redmist.racing/logos";

    public OrganizationClient(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        var url = configuration["Server:OrganizationUrl"] ?? throw new InvalidOperationException("Server OrganizationUrl is not configured.");
        var authUrl = configuration["Keycloak:AuthServerUrl"] ?? throw new InvalidOperationException("Keycloak URL is not configured.");
        var realm = configuration["Keycloak:Realm"] ?? throw new InvalidOperationException("Keycloak realm is not configured.");
        var clientId = configuration["Keycloak:ClientId"] ?? throw new InvalidOperationException("Keycloak client ID is not configured.");
        var clientSecret = configuration["Keycloak:ClientSecret"] ?? throw new InvalidOperationException("Keycloak client secret is not configured.");

        var options = new RestClientOptions(url)
        {
            Authenticator = new KeycloakServiceAuthenticator(string.Empty, authUrl, realm, clientId, clientSecret)
        };
        restClient = options.CreateWithMessagePack();

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
        var response = await restClient.ExecuteAsync(request);

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
