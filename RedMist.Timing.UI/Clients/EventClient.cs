using BigMission.Shared.Auth;
using Microsoft.Extensions.Configuration;
using RedMist.TimingCommon.Models;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

public class EventClient
{
    private readonly RestClient restClient;

    public EventClient(IConfiguration configuration)
    {
        var url = configuration["Server:Url"] ?? throw new InvalidOperationException("Server URL is not configured.");
        var authUrl = configuration["Keycloak:AuthServerUrl"] ?? throw new InvalidOperationException("Keycloak URL is not configured.");
        var realm = configuration["Keycloak:Realm"] ?? throw new InvalidOperationException("Keycloak realm is not configured.");
        var clientId = configuration["Keycloak:ClientId"] ?? throw new InvalidOperationException("Keycloak client ID is not configured.");
        var clientSecret = configuration["Keycloak:ClientSecret"] ?? throw new InvalidOperationException("Keycloak client secret is not configured.");

        var options = new RestClientOptions(url)
        {
            Authenticator = new KeycloakServiceAuthenticator(string.Empty, authUrl, realm, clientId, clientSecret)
        };
        restClient = new RestClient(options);
    }

    public virtual async Task<Event[]> LoadRecentEventsAsync() 
    {
        var request = new RestRequest("LoadEvents", Method.Get);
        var startTime = DateTime.UtcNow - TimeSpan.FromDays(7);
        request.AddQueryParameter("startDateUtc", startTime.ToString());
        return await restClient.GetAsync<Event[]>(request) ?? [];
    }

    public virtual async Task<List<CarPosition>> LoadCarLapsAsync(int eventId, string carNumber)
    {
        var request = new RestRequest("LoadCarLaps", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("carNumber", carNumber);
        return await restClient.GetAsync<List<CarPosition>>(request) ?? [];
    }
}
