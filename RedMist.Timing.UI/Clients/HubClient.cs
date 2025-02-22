using BigMission.Shared.SignalR;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
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
                _ = hub.InvokeAsync("SubscribeToEvent", subscribedEventId);
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
            var payload = JsonSerializer.Deserialize<Payload>(message);
            if (payload == null)
                return;
            Logger.LogInformation("RX: {0}", payload.EventName);
            WeakReferenceMessenger.Default.Send(new StatusNotification(payload));
        }
        catch (Exception)
        {
            Logger.LogError("Failed to process message: {0}", message);
        }
    }
}
