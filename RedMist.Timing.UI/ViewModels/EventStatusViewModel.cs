using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Clients;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class EventStatusViewModel : ObservableObject, IRecipient<StatusNotification>
{
    private readonly HubClient hubClient;
    private int eventId;

    [ObservableProperty]
    private string eventName = string.Empty;

    [ObservableProperty]
    public string flag = string.Empty;

    [ObservableProperty]
    public string timeToGo = string.Empty;

    [ObservableProperty]
    public string totalTime = string.Empty;

    public EventStatusViewModel(HubClient hubClient)
    {
        this.hubClient = hubClient;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public async Task Initialize(int eventId)
    {
        this.eventId = eventId;
        try
        {
            await hubClient.SubscribeToEvent(eventId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error subscribing to event: {ex.Message}");
        }
    }

    public Task Handle(StatusNotification notification, CancellationToken cancellationToken)
    {
        
        return Task.CompletedTask;
    }

    public void Receive(StatusNotification message)
    {
        var status = message.Value;
        if (status.EventId != eventId)
            return;

        if (status.EventName != null)
        {
            EventName = status.EventName;
        }

        if (status.EventStatus != null)
        {
            Flag = status.EventStatus.Flag.ToString();
            TimeToGo = status.EventStatus.TimeToGo;
            TotalTime = status.EventStatus.TotalTime;
        }
    }
}
