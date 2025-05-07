using BigMission.Shared.SignalR;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
using System.Buffers.Text;
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
            if (hub.State == HubConnectionState.Connected && subscribedEventId != null)
            {
                _ = debouncer.ExecuteAsync(async () =>
                {
                    await hub.InvokeAsync("SubscribeToEvent", subscribedEventId);
                });
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

    public async Task SubscribeToEvent(int eventId)
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
    }

    public async Task UnsubscribeFromEvent(int eventId)
    {
        subscribedEventId = null;
        if (hub == null)
            return;
        await hub.InvokeAsync("UnsubscribeFromEvent", eventId);
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

    private void ProcessMessage(string message)
    {
        try
        {
            int compressedLength = 0;
            if (!message.StartsWith("{"))
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

    public async Task SubscribeToControlLogs(int eventId)
    {
        if (hub == null)
            return;
        await hub.InvokeAsync("SubscribeToControlLogs", eventId);

        hub.Remove("ReceiveControlLog");
        hub.On("ReceiveControlLog", (CarControlLogs s) => ProcessControlLogs(s));
    }

    public async Task UnsubscribeFromControlLogs(int eventId)
    {
        if (hub == null)
            return;
        await hub.InvokeAsync("UnsubscribeFromControlLogs", eventId);
    }

    public async Task SubscribeToCarControlLogs(int eventId, string carNum)
    {
        if (hub == null)
            return;
        await hub.InvokeAsync("SubscribeToCarControlLogs", eventId, carNum);

        hub.Remove("ReceiveControlLog");
        hub.On("ReceiveControlLog", (CarControlLogs s) => ProcessControlLogs(s));
    }

    public async Task UnsubscribeFromCarControlLogs(int eventId, string carNum)
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
}
