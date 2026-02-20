using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.TimingCommon;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Configuration;
using RedMist.TimingCommon.Models.InCarDriverMode;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

public class EventClient : BaseRestClient
{
    private ILogger Logger { get; }


    public EventClient(IConfiguration configuration, ILoggerFactory loggerFactory)
        : base(configuration, "Server:EventUrl")
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
    }


    public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName, int maxRetries = 3)
    {
        var retryDelay = TimeSpan.FromMilliseconds(500);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    Logger.LogError(ex, "Failed to execute {OperationName} after {MaxRetries} attempts", operationName, maxRetries);
                    return default!;
                }

                Logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed for {OperationName}. Retrying in {DelayMs}ms",
                    attempt, maxRetries, operationName, retryDelay.TotalMilliseconds);

                await Task.Delay(retryDelay);
                retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2);
            }
        }

        throw new InvalidOperationException("This should never be reached");
    }

    public virtual async Task<List<EventListSummary>> LoadRecentEventsAsync() 
    {
        var request = new RestRequest("LoadLiveAndRecentEvents", Method.Get);
        return await RestClient.GetAsync<List<EventListSummary>>(request) ?? [];
    }

    public virtual async Task<List<EventListSummary>> LoadArchivedEventsAsync(int offset, int take)
    {
        var request = new RestRequest("LoadArchivedEvents", Method.Get);
        request.AddQueryParameter("offset", offset);
        request.AddQueryParameter("take", take);
        return await RestClient.GetAsync<List<EventListSummary>>(request) ?? [];
    }

    public virtual async Task<TimingCommon.Models.Event?> LoadEventAsync(int eventId)
    {
        if (eventId == 0)
            return null;
        var request = new RestRequest("LoadEvent", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        return await RestClient.GetAsync<TimingCommon.Models.Event?>(request);
    }

    public virtual async Task<SessionState?> LoadEventStatusAsync(int eventId)
    {
        if (eventId == 0)
            return null;
        var request = new RestRequest("GetCurrentSessionState", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        return await RestClient.GetAsync<SessionState?>(request);
    }

    public virtual async Task<List<CarPosition>> LoadCarLapsAsync(int eventId, int sessionId, string carNumber)
    {
        var request = new RestRequest("LoadCarLaps", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        request.AddQueryParameter("carNumber", carNumber);
        return await RestClient.GetAsync<List<CarPosition>>(request) ?? [];
    }

    public virtual async Task<List<Session>> LoadSessionsAsync(int eventId)
    {
        var request = new RestRequest("LoadSessions", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        return await RestClient.GetAsync<List<Session>>(request) ?? [];
    }

    public virtual async Task<SessionState?> LoadSessionResultsAsync(int eventId, int sessionId)
    {
        var request = new RestRequest("LoadSessionResults", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        return await RestClient.GetAsync<SessionState?>(request);
    }

    public virtual async Task<CompetitorMetadata?> LoadCompetitorMetadataAsync(int eventId, string car)
    {
        var request = new RestRequest("LoadCompetitorMetadata", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("car", car);
        return await RestClient.GetAsync<CompetitorMetadata?>(request);
    }

    public virtual async Task<List<ControlLogEntry>> LoadControlLogAsync(int eventId)
    {
        var request = new RestRequest("LoadControlLog", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        return await RestClient.GetAsync<List<ControlLogEntry>>(request) ?? [];
    }

    public virtual async Task<List<ControlLogEntry>> LoadSessionHistoricalControlLogAsync(int eventId, int sessionId)
    {
        var request = new RestRequest("LoadSessionHistoricalControlLog", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        return await RestClient.GetAsync<List<ControlLogEntry>>(request) ?? [];
    }

    public virtual async Task<CarControlLogs?> LoadCarControlLogsAsync(int eventId, string car)
    {
        var request = new RestRequest("LoadCarControlLogs", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("car", car);
        return await RestClient.GetAsync<CarControlLogs?>(request);
    }

    public virtual async Task<InCarPayload?> LoadInCarDriverModePayloadAsync(int eventId, string car)
    {
        var request = new RestRequest("LoadInCarPayload", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("car", car);
        return await RestClient.GetAsync<InCarPayload?>(request);
    }

    public virtual async Task<List<FlagDuration>> LoadFlagsAsync(int eventId, int sessionId)
    {
        var request = new RestRequest("LoadFlags", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        return await RestClient.GetAsync<List<FlagDuration>>(request) ?? [];
    }

    public virtual async Task<UIVersionInfo?> LoadUIVersionInfoAsync()
    {
        var request = new RestRequest("GetUIVersionInfo", Method.Get);
        return await RestClient.GetAsync<UIVersionInfo>(request);
    }
}
