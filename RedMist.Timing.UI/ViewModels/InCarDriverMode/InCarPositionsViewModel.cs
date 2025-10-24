using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.InCarDriverMode;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.InCarDriverMode;

public partial class InCarPositionsViewModel : ObservableObject, IRecipient<InCarPositionUpdate>
{
    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private int eventId;
    private string carNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAheadOutOfClassVisible))]
    private bool showInClassOnly;
    [ObservableProperty]
    private string positionInClass = string.Empty;
    [ObservableProperty]
    private string positionOverall = string.Empty;

    public LargeObservableCollection<CarViewModel> Cars { get; } = new LargeObservableCollection<CarViewModel>();
    [ObservableProperty]
    private CarViewModel carAhead = new();
    [ObservableProperty]
    private CarViewModel carAheadOutOfClass = new();
    [ObservableProperty]
    private CarViewModel driversCar = new();
    [ObservableProperty]
    private CarViewModel carBehind = new();

    [ObservableProperty]
    private string message = string.Empty;
    [ObservableProperty]
    private string connectionStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAheadOutOfClassVisible))]
    public bool hasOutOfClassAhead;

    public bool IsAheadOutOfClassVisible => HasOutOfClassAhead && !ShowInClassOnly;

    [ObservableProperty]
    private Flags flag;

    private HubConnectionState? lastHubConnectionState;


    public InCarPositionsViewModel(HubClient hubClient, EventClient eventClient)
    {
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        DriversCar.SetAsDriver();
        CarAheadOutOfClass.SetAsOutOfClass();
        WeakReferenceMessenger.Default.RegisterAll(this);
        hubClient.ConnectionStatusChanged += HubClient_ConnectionStatusChanged;
    }


    private void HubClient_ConnectionStatusChanged(HubConnectionState c)
    {
        Dispatcher.UIThread.Post(() => ConnectionStatus = c.ToString());

        if (lastHubConnectionState != HubConnectionState.Connected && c == HubConnectionState.Connected)
        {
            Dispatcher.UIThread.Post(async () => await LoadPayload(eventId, carNumber));
        }

        lastHubConnectionState = c;
    }

    public void Initialize(int eventId, string carNumber, bool showInClassOnly)
    {
        this.eventId = eventId;
        this.carNumber = carNumber;
        ShowInClassOnly = showInClassOnly;

        Dispatcher.UIThread.Post(async () =>
        {
            Message = "Connecting to event...";
            try
            {
                await LoadPayload(eventId, carNumber);

                await hubClient.SubscribeToInCarDriverEventAsync(eventId, carNumber);
                Message = "Waiting for position updates...";
            }
            catch (Exception ex)
            {
                Message = "Failed to connect to event: " + ex.Message;
            }
        });
    }

    private async Task LoadPayload(int eventId, string carNumber)
    {
        try
        {
            var payload = await eventClient.LoadInCarDriverModePayloadAsync(eventId, carNumber);
            if (payload == null)
            {
                Message = "Empty payload.";
            }
            else
            {
                Message = "Payload loaded";
                Receive(new InCarPositionUpdate(payload));
            }
        }
        catch
        {
            Message = "Error loading last payload";
        }
    }

    public void Unsubscribe()
    {
        _ = hubClient.UnsubscribeFromInCarDriverEventAsync(eventId, carNumber);
    }

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "InCarDriverActiveSettings" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
        Unsubscribe();
    }

    public void Receive(InCarPositionUpdate message)
    {
        Dispatcher.UIThread.Post(() => ProcessInCarPayload(message.Value));
    }

    private void ProcessInCarPayload(InCarPayload payload)
    {
        PositionInClass = payload.PositionInClass;
        PositionOverall = payload.PositionOverall;
        Flag = payload.Flag;

        if (payload.Cars == null || payload.Cars.Count != 4)
        {
            Message = "Cars unavailable.";
            return;
        }

        var cars = new List<CarViewModel>();
        if (payload.Cars[0] != null)
        {
            CarAhead.Update(payload.Cars[0]);
            cars.Add(CarAhead);
        }
        if (payload.Cars[1] != null && !ShowInClassOnly)
        {
            CarAheadOutOfClass.Update(payload.Cars[1]);
            cars.Add(CarAheadOutOfClass);
        }
        if (payload.Cars[2] != null)
        {
            DriversCar.Update(payload.Cars[2]);
            cars.Add(DriversCar);
        }
        if (payload.Cars[3] != null)
        {
            CarBehind.Update(payload.Cars[3]);
            cars.Add(CarBehind);
        }

        bool carsChanged = false;
        if (Cars.Count != cars.Count)
        {
            carsChanged = true;
        }
        else
        {
            for (int i = 0; i < Cars.Count; i++)
            {
                if (Cars[i] != cars[i])
                {
                    carsChanged = true;
                    break;
                }
            }
        }
        if (carsChanged)
        {
            Cars.SetRange(cars);
        }

        Message = DateTime.Now.ToString();
    }
}
