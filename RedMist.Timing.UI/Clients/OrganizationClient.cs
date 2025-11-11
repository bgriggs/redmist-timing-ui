using BigMission.Shared.Auth;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

public class OrganizationClient
{
    private readonly RestClient restClient;

    public OrganizationClient(IConfiguration configuration)
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
        restClient = new RestClient(options);

        // Add default Accept header for all requests (MessagePack preferred, JSON fallback)
        restClient.AddDefaultHeader("Accept", "application/msgpack, application/json");
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
}
