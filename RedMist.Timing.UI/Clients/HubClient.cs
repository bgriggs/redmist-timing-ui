using BigMission.Shared.SignalR;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
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


    public HubClient(ILoggerFactory loggerFactory, IConfiguration configuration) : base(loggerFactory, configuration)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        ConnectionStatusChanged += HubClient_ConnectionStatusChanged;
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
                    _ = debouncer.ExecuteAsync(async () => await hub.InvokeAsync("SubscribeToEvent", subscribedEventId));
                }
                else if (subscribedInCarDriverEventIdAndCar != null)
                {
                    _ = debouncer.ExecuteAsync(async () =>
                        await hub.InvokeAsync("SubscribeToInCarDriverEvent", subscribedInCarDriverEventIdAndCar.Value.eventId, subscribedInCarDriverEventIdAndCar.Value.car));
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
        subscribedEventId = eventId;
        hub = StartConnection();

        hub.Remove("ReceiveMessage");
        hub.On("ReceiveMessage", (string s) => ProcessMessage(s));

        hub.Remove("ReceiveInCarVideoMetadata");
        hub.On("ReceiveInCarVideoMetadata", (string s) => ProcessInCarVideoMetadata(s));
    }

    public async Task UnsubscribeFromEventAsync(int eventId)
    {
        subscribedEventId = null;

        if (hub == null)
            return;
        
        try
        {
            await hub.InvokeAsync("UnsubscribeFromEvent", eventId);
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

    #endregion

    #region Control Logs

    public async Task SubscribeToControlLogsAsync(int eventId)
    {
        if (hub == null)
            return;
        await hub.InvokeAsync("SubscribeToControlLogs", eventId);

        hub.Remove("ReceiveControlLog");
        hub.On("ReceiveControlLog", (CarControlLogs s) => ProcessControlLogs(s));
    }

    public async Task UnsubscribeFromControlLogsAsync(int eventId)
    {
        if (hub == null)
            return;
        await hub.InvokeAsync("UnsubscribeFromControlLogs", eventId);
    }

    public async Task SubscribeToCarControlLogsAsync(int eventId, string carNum)
    {
        if (hub == null)
            return;
        await hub.InvokeAsync("SubscribeToCarControlLogs", eventId, carNum);

        hub.Remove("ReceiveControlLog");
        hub.On("ReceiveControlLog", (CarControlLogs s) => ProcessControlLogs(s));
    }

    public async Task UnsubscribeFromCarControlLogsAsync(int eventId, string carNum)
    {
        if (hub == null)
            return;
        await hub.InvokeAsync("UnsubscribeFromCarControlLogs", eventId, carNum);
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

    private void ProcessInCarVideoMetadata(string metadataJson)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<List<VideoMetadata>>(metadataJson);
            if (metadata == null)
                return;

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
        subscribedInCarDriverEventIdAndCar = (eventId, car);
        hub = StartConnection();

        hub.Remove("ReceiveInCarUpdate");
        hub.On("ReceiveInCarUpdate", async (string s) => await ProcessInCarPayloadAsync(s));
    }

    public async Task UnsubscribeFromInCarDriverEventAsync(int eventId, string car)
    {
        subscribedInCarDriverEventIdAndCar = null;

        if (hub == null)
            return;

        try
        {
            await hub.InvokeAsync("UnsubscribeFromInCarDriverEvent", eventId, car);
            await hub.DisposeAsync();
            hub = null;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to dispose hub connection");
        }
    }

    private async Task ProcessInCarPayloadAsync(string compressedMessage)
    {
        try
        {
            var compressedBytes = Convert.FromBase64String(compressedMessage);
            using var input = new MemoryStream(compressedBytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            await gzip.CopyToAsync(output);
            var decompressedBytes = output.ToArray();

            var json = Encoding.UTF8.GetString(decompressedBytes);
            var payload = JsonSerializer.Deserialize<InCarPayload>(json);
            if (payload == null)
                return;

            Logger.LogInformation("RX-in-Car: {len} bytes", compressedMessage.Length * 8);
            WeakReferenceMessenger.Default.Send(new InCarPositionUpdate(payload));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process in-car payload");
        }
    }

    #endregion
}
