using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Configuration;
using RedMist.TimingCommon.Models.InCarDriverMode;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

public class EventClient : BaseRestClient
{
    private readonly EventAccessCodeStore accessCodeStore;
    private ILogger Logger { get; }


    public EventClient(IConfiguration configuration, ILoggerFactory loggerFactory, EventAccessCodeStore accessCodeStore)
        : base(configuration, "Server:EventUrl")
    {
        this.accessCodeStore = accessCodeStore;
        Logger = loggerFactory.CreateLogger(GetType().Name);
    }


    /// <summary>
    /// Runs an operation, retrying with a growing delay, and returns null once it has given up.
    /// </summary>
    /// <remarks>
    /// Null means the operation failed every attempt, and is not the same answer as an empty result.
    /// The distinction is the whole point of the nullable return: this used to hand back
    /// <c>default!</c>, which callers were free to treat as "nothing came back", and the events list
    /// duly reported a total failure to reach the server as "No events found". A real outage looked
    /// like a quiet weekend.
    ///
    /// Constrained to reference types so that stays true. On a value type <c>default</c> is 0 or
    /// false, which is indistinguishable from a real answer, and a failure would be silently
    /// believed rather than merely misreported. The constraint is <c>class?</c> rather than
    /// <c>class</c> only so operations that already return a nullable reference, such as
    /// <see cref="LoadEventStatusAsync"/>, still fit; value types remain excluded either way.
    /// </remarks>
    public async Task<T?> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName, int maxRetries = 3)
        where T : class?
    {
        // Without this a caller passing zero would skip the loop entirely and fall through to the
        // "should never be reached" throw below, which is neither the documented null nor anything
        // the caller could act on.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRetries);

        var retryDelay = TimeSpan.FromMilliseconds(500);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (EventAccessDeniedException)
            {
                // Don't retry on access denied - caller needs to prompt for a new code.
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    Logger.LogError(ex, "Failed to execute {OperationName} after {MaxRetries} attempts", operationName, maxRetries);
                    return null;
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
        // LoadEvent itself is not gated by the access code, but attach it anyway
        // so the same path works once the user has provided one.
        AttachAccessCode(request, eventId);
        return await GetAsync<TimingCommon.Models.Event?>(request, eventId);
    }

    public virtual async Task<SessionState?> LoadEventStatusAsync(int eventId)
    {
        if (eventId == 0)
            return null;
        var request = new RestRequest("GetCurrentSessionState", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        AttachAccessCode(request, eventId);
        return await GetAsync<SessionState?>(request, eventId);
    }

    public virtual async Task<List<CarPosition>> LoadCarLapsAsync(int eventId, int sessionId, string carNumber)
    {
        var request = new RestRequest("LoadCarLaps", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        request.AddQueryParameter("carNumber", carNumber);
        AttachAccessCode(request, eventId);
        return await GetAsync<List<CarPosition>>(request, eventId) ?? [];
    }

    public virtual async Task<List<Session>> LoadSessionsAsync(int eventId)
    {
        var request = new RestRequest("LoadSessions", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        AttachAccessCode(request, eventId);
        return await GetAsync<List<Session>>(request, eventId) ?? [];
    }

    public virtual async Task<SessionState?> LoadSessionResultsAsync(int eventId, int sessionId)
    {
        var request = new RestRequest("LoadSessionResults", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        AttachAccessCode(request, eventId);
        return await GetAsync<SessionState?>(request, eventId);
    }

    public virtual async Task<CompetitorMetadata?> LoadCompetitorMetadataAsync(int eventId, string car)
    {
        var request = new RestRequest("LoadCompetitorMetadata", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("car", car);
        AttachAccessCode(request, eventId);
        return await GetAsync<CompetitorMetadata?>(request, eventId);
    }

    public virtual async Task<List<ControlLogEntry>> LoadControlLogAsync(int eventId)
    {
        var request = new RestRequest("LoadControlLog", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        AttachAccessCode(request, eventId);
        return await GetAsync<List<ControlLogEntry>>(request, eventId) ?? [];
    }

    public virtual async Task<List<ControlLogEntry>> LoadSessionHistoricalControlLogAsync(int eventId, int sessionId)
    {
        var request = new RestRequest("LoadSessionHistoricalControlLog", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        AttachAccessCode(request, eventId);
        return await GetAsync<List<ControlLogEntry>>(request, eventId) ?? [];
    }

    public virtual async Task<CarControlLogs?> LoadCarControlLogsAsync(int eventId, string car)
    {
        var request = new RestRequest("LoadCarControlLogs", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("car", car);
        AttachAccessCode(request, eventId);
        return await GetAsync<CarControlLogs?>(request, eventId);
    }

    public virtual async Task<InCarPayload?> LoadInCarDriverModePayloadAsync(int eventId, string car)
    {
        var request = new RestRequest("LoadInCarPayload", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("car", car);
        AttachAccessCode(request, eventId);
        return await GetAsync<InCarPayload?>(request, eventId);
    }

    public virtual async Task<List<FlagDuration>> LoadFlagsAsync(int eventId, int sessionId)
    {
        var request = new RestRequest("LoadFlags", Method.Get);
        request.AddQueryParameter("eventId", eventId);
        request.AddQueryParameter("sessionId", sessionId);
        AttachAccessCode(request, eventId);
        return await GetAsync<List<FlagDuration>>(request, eventId) ?? [];
    }

    public virtual async Task<UIVersionInfo?> LoadUIVersionInfoAsync(CancellationToken cancellationToken = default)
    {
        var request = new RestRequest("GetUIVersionInfo", Method.Get);
        return await RestClient.GetAsync<UIVersionInfo>(request, cancellationToken);
    }

    private void AttachAccessCode(RestRequest request, int eventId)
    {
        var code = accessCodeStore.Get(eventId);
        if (!string.IsNullOrEmpty(code))
        {
            request.AddHeader(EventAccessCodeStore.HeaderName, code);
        }
    }

    /// <summary>
    /// Executes a request and translates HTTP 401 responses into <see cref="EventAccessDeniedException"/>
    /// so callers can re-prompt for the access code.
    /// </summary>
    private async Task<T?> GetAsync<T>(RestRequest request, int eventId)
    {
        var response = await RestClient.ExecuteAsync<T>(request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Broadcast so MainViewModel can re-prompt even if the caller swallows the throw.
            WeakReferenceMessenger.Default.Send(new EventAccessDeniedNotification(eventId));
            throw new EventAccessDeniedException(eventId);
        }
        if (!response.IsSuccessful)
        {
            if (response.ErrorException != null)
                throw response.ErrorException;
            return default;
        }
        return response.Data;
    }
}
