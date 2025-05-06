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
        var url = configuration["Server:EventUrl"] ?? throw new InvalidOperationException("Server EventUrl is not configured.");
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

    public virtual async Task<List<EventListSummary>> LoadRecentEventsAsync() 
    {
        var request = new RestRequest("LoadLiveAndRecentEvents", Method.Get);
        //var startTime = DateTime.UtcNow - TimeSpan.FromDays(7);
        //request.AddQueryParameter("startDateUtc", startTime.ToString());
        return await restClient.GetAsync<List<EventListSummary>>(request) ?? [];
    }

    public virtual async Task<Event?> LoadEventAsync(int eventId)
    {
        var request = new RestRequest("LoadEvent", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        return await restClient.GetAsync<Event?>(request);
    }

    public virtual async Task<List<CarPosition>> LoadCarLapsAsync(int eventId, int sessionId, string carNumber)
    {
        var request = new RestRequest("LoadCarLaps", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        request.AddQueryParameter("carNumber", carNumber);
        return await restClient.GetAsync<List<CarPosition>>(request) ?? [];
    }

    public virtual async Task<List<Session>> LoadSessionsAsync(int eventId)
    {
        var request = new RestRequest("LoadSessions", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        return await restClient.GetAsync<List<Session>>(request) ?? [];
    }

    public virtual async Task<Payload?> LoadSessionResultsAsync(int eventId, int sessionId)
    {
        var request = new RestRequest("LoadSessionResults", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        return await restClient.GetAsync<Payload?>(request);
    }

    public virtual async Task<CompetitorMetadata?> LoadCompetitorMetadataAsync(int eventId, string car)
    {
        var request = new RestRequest("LoadCompetitorMetadata", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("car", car);
        return await restClient.GetAsync<CompetitorMetadata?>(request);
    }

    public virtual async Task<List<ControlLogEntry>> LoadControlLogAsync(int eventId)
    {
        var request = new RestRequest("LoadControlLog", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        return await restClient.GetAsync<List<ControlLogEntry>>(request) ?? [];
    }

    public virtual async Task<CarControlLogs?> LoadCarControlLogsAsync(int eventId, string car)
    {
        var request = new RestRequest("LoadCarControlLogs", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("car", car);
        return await restClient.GetAsync<CarControlLogs?>(request);
    }
}
