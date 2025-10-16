using BigMission.Shared.Auth;
using BigMission.Shared.SignalR;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.InCarDriverMode;
using RedMist.TimingCommon.Models.InCarVideo;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using MessagePack.Resolvers;

namespace RedMist.Timing.UI.Clients;

/// <summary>
/// Client for communicating with the cloud SignalR hub.
/// </summary>
public class HubClient : HubClientBase
{
    private HubConnection? hub;
    private ILogger Logger { get; }
    private int? subscribedEventId;
    private (int eventId, string car)? subscribedInCarDriverEventIdAndCar;
    private readonly Debouncer debouncer = new(TimeSpan.FromMilliseconds(5));
    private readonly IConfiguration configuration;
    private long sessionUpdateCount;

    static HubClient()
    {
        // Configure MessagePack for AOT compatibility (iOS)
        // Use StaticCompositeResolver to combine multiple resolvers
        var resolver = CompositeResolver.Create(
            // Enable native resolvers first
            NativeDateTimeResolver.Instance,
            // Then use the generated resolver (if you have one) or StandardResolver
            StandardResolver.Instance
        );
        
        var options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
        MessagePackSerializer.DefaultOptions = options;
    }

    public HubClient(ILoggerFactory loggerFactory, IConfiguration configuration) : base(loggerFactory, configuration)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        ConnectionStatusChanged += HubClient_ConnectionStatusChanged;
        this.configuration = configuration;
    }


    protected override HubConnection GetConnection()
    {
        string hubUrl = configuration["Hub:Url"] ?? throw new InvalidOperationException("Hub URL is not configured.");
        string authUrl = configuration["Keycloak:AuthServerUrl"] ?? throw new InvalidOperationException("Keycloak URL is not configured.");
        string realm = configuration["Keycloak:Realm"] ?? throw new InvalidOperationException("Keycloak realm is not configured.");
    
        var hubConnection = new HubConnectionBuilder().WithUrl(hubUrl, delegate (HttpConnectionOptions options)
        {
            options.AccessTokenProvider = async delegate
            {
                try
                {
                    var clientId = GetClientId();
                    var clientSecret = GetClientSecret();
                    return await KeycloakServiceToken.RequestClientToken(authUrl, realm, clientId, clientSecret);
                }
                catch (Exception exception)
                {
                    Logger.LogError(exception, "Failed to get server hub access token");
                    return null;
                }
            };
        })
        .WithAutomaticReconnect(new InfiniteRetryPolicy())
        .AddMessagePackProtocol(options =>
        {
            // Configure MessagePack options for the SignalR protocol
            options.SerializerOptions = MessagePackSerializer.DefaultOptions;
        })
        .Build();

        InitializeStateLogging(hubConnection);
        return hubConnection;
    }

    private void HubClient_ConnectionStatusChanged(HubConnectionState obj)
    {
        if (hub == null)
            return;
        try
        {
            if (hub.State == HubConnectionState.Connected)
            {
                if (subscribedEventId != null)
                {
                    _ = debouncer.ExecuteAsync(async () => await hub.InvokeAsync("SubscribeToEventV2", subscribedEventId));
                }
                else if (subscribedInCarDriverEventIdAndCar != null)
                {
                    _ = debouncer.ExecuteAsync(async () =>
                        await hub.InvokeAsync("SubscribeToInCarDriverEventV2", subscribedInCarDriverEventIdAndCar.Value.eventId, subscribedInCarDriverEventIdAndCar.Value.car));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to resubscribe to event");
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    #region Car Timing Status

    public async Task SubscribeToEventAsync(int eventId)
    {
        if (hub != null)
        {
            try
            {
                await hub.DisposeAsync();
                hub = null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to dispose hub connection");
            }
        }
        
        try
        {
            subscribedEventId = eventId;
            hub = StartConnection();

            hub.Remove("ReceiveSessionPatch");
            hub.On("ReceiveSessionPatch", (SessionStatePatch ssp) => ProcessSessionMessage(ssp));

            hub.Remove("ReceiveCarPatches");
            hub.On("ReceiveCarPatches", (CarPositionPatch[] cpps) => ProcessCarPatches(cpps));

            hub.Remove("ReceiveReset");
            hub.On("ReceiveReset", ProcessReset);

            hub.Remove("ReceiveInCarVideoMetadata");
            hub.On("ReceiveInCarVideoMetadata", (List<VideoMetadata> vm) => ProcessInCarVideoMetadata(vm));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to subscribe to event");
        }
    }

    public async Task UnsubscribeFromEventAsync(int eventId)
    {
        subscribedEventId = null;

        if (hub == null)
            return;

        try
        {
            await hub.InvokeAsync("UnsubscribeFromEventV2", eventId);
            await hub.DisposeAsync();
            hub = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to dispose hub connection");
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            int compressedLength = 0;
            if (!message.StartsWith('{'))
            {
                compressedLength = message.Length;
                var compressedBytes = Convert.FromBase64String(message);
                using var input = new MemoryStream(compressedBytes);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();

                gzip.CopyTo(output);
                var decompressedBytes = output.ToArray();

                message = Encoding.UTF8.GetString(decompressedBytes);
            }

            var payload = JsonSerializer.Deserialize<Payload>(message);
            if (payload == null)
                return;

            var size = compressedLength > 0 ? compressedLength : message.Length;
            Logger.LogInformation("RX: {len} bytes, cars: {c}", size * 8, payload.CarPositions.Count + payload.CarPositionUpdates.Count);
            WeakReferenceMessenger.Default.Send(new StatusNotification(payload));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process message.");
        }
    }

    private void ProcessSessionMessage(SessionStatePatch sessionStatePatch)
    {
        try
        {
            sessionUpdateCount++;
            Logger.LogInformation("RX Session Patch {c}", sessionUpdateCount);
            WeakReferenceMessenger.Default.Send(new SessionStatusNotification(sessionStatePatch));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process session message.");
        }
    }

    private void ProcessCarPatches(CarPositionPatch[] carPatches)
    {
        try
        {
            Logger.LogInformation("RX Car Patches: {c}", carPatches.Length);
            WeakReferenceMessenger.Default.Send(new CarStatusNotification(carPatches));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process car patches.");
        }
    }

    private void ProcessReset()
    {
        try
        {
            Logger.LogInformation("RX Reset");
            WeakReferenceMessenger.Default.Send(new ResetNotification());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process reset message.");
        }
    }

    #endregion

    #region Control Logs

    public async Task SubscribeToControlLogsAsync(int eventId)
    {
        if (hub == null)
            return;

        try
        {
            await hub.InvokeAsync("SubscribeToControlLogs", eventId);

            hub.Remove("ReceiveControlLog");
            hub.On("ReceiveControlLog", (CarControlLogs s) => ProcessControlLogs(s));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to subscribe to control logs");
        }
    }

    public async Task UnsubscribeFromControlLogsAsync(int eventId)
    {
        if (hub == null)
            return;
            
        try
        {
            await hub.InvokeAsync("UnsubscribeFromControlLogs", eventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to unsubscribe from control logs");
        }
    }

    public async Task SubscribeToCarControlLogsAsync(int eventId, string carNum)
    {
        if (hub == null)
            return;
            
        try
        {
            await hub.InvokeAsync("SubscribeToCarControlLogs", eventId, carNum);

            hub.Remove("ReceiveControlLog");
            hub.On("ReceiveControlLog", (CarControlLogs s) => ProcessControlLogs(s));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to subscribe to car control logs");
        }
    }

    public async Task UnsubscribeFromCarControlLogsAsync(int eventId, string carNum)
    {
        if (hub == null)
            return;
            
        try
        {
            await hub.InvokeAsync("UnsubscribeFromCarControlLogs", eventId, carNum);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to unsubscribe from car control logs");
        }
    }

    private void ProcessControlLogs(CarControlLogs ccl)
    {
        try
        {
            Logger.LogInformation("RX Control Logs: {cl} car {cn}", ccl.ControlLogEntries.Count, ccl.CarNumber);
            WeakReferenceMessenger.Default.Send(new ControlLogNotification(ccl));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process control log message");
        }
    }

    #endregion

    #region In-Car Video Metadata

    private void ProcessInCarVideoMetadata(List<VideoMetadata> metadata)
    {
        try
        {
            Logger.LogInformation("RX In-Car Video Metadata: {c}", metadata.Count);
            WeakReferenceMessenger.Default.Send(new InCarVideoMetadataNotification(metadata));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process in-car video metadata message");
        }
    }

    #endregion

    #region Driver Mode

    public async Task SubscribeToInCarDriverEventAsync(int eventId, string car)
    {
        if (hub != null)
        {
            try
            {
                await hub.DisposeAsync();
                hub = null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to dispose hub connection");
            }
        }
        
        try
        {
            subscribedInCarDriverEventIdAndCar = (eventId, car);
            hub = StartConnection();

            hub.Remove("ReceiveInCarUpdateV2");
            hub.On("ReceiveInCarUpdateV2", (InCarPayload s) => ProcessInCarPayload(s));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to subscribe to in-car driver event");
        }
    }

    public async Task UnsubscribeFromInCarDriverEventAsync(int eventId, string car)
    {
        subscribedInCarDriverEventIdAndCar = null;

        if (hub == null)
            return;

        try
        {
            await hub.InvokeAsync("UnsubscribeFromInCarDriverEventV2", eventId, car);
            await hub.DisposeAsync();
            hub = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to dispose hub connection");
        }
    }

    private void ProcessInCarPayload(InCarPayload payload)
    {
        try
        {
            if (payload == null)
                return;
            Logger.LogInformation("RX InCarPayload: {c}", payload.Cars.Count);
            WeakReferenceMessenger.Default.Send(new InCarPositionUpdate(payload));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process in-car payload");
        }
    }

    #endregion
}
