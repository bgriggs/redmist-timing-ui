using BigMission.Shared.Auth;
using BigMission.Shared.RestSharp;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;

namespace RedMist.Timing.UI.Clients;

public abstract class BaseRestClient
{
    protected RestClient RestClient { get; }

    protected BaseRestClient(IConfiguration configuration, string serverUrlConfigKey)
    {
        var url = configuration[serverUrlConfigKey] ?? throw new InvalidOperationException($"{serverUrlConfigKey} is not configured.");
        var authUrl = configuration["Keycloak:AuthServerUrl"] ?? throw new InvalidOperationException("Keycloak URL is not configured.");
        var realm = configuration["Keycloak:Realm"] ?? throw new InvalidOperationException("Keycloak realm is not configured.");
        var clientId = configuration["Keycloak:ClientId"] ?? throw new InvalidOperationException("Keycloak client ID is not configured.");
        var clientSecret = configuration["Keycloak:ClientSecret"] ?? throw new InvalidOperationException("Keycloak client secret is not configured.");

        var options = new RestClientOptions(url)
        {
            Authenticator = new KeycloakServiceAuthenticator(string.Empty, authUrl, realm, clientId, clientSecret)
        };
        RestClient = options.CreateWithMessagePack();
    }
}
