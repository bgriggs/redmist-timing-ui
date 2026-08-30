using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.InCarDriverMode;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.InCarDriverMode;

public partial class InCarPositionsViewModel : ObservableObject, IRecipient<InCarPositionUpdate>, IDisposable
{
    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private ILogger Logger { get; }
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


    public InCarPositionsViewModel(HubClient hubClient, EventClient eventClient, ILoggerFactory loggerFactory)
    {
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        DriversCar.SetAsDriver();
        CarAheadOutOfClass.SetAsOutOfClass();
        WeakReferenceMessenger.Default.RegisterAll(this);
        hubClient.ConnectionStatusChanged += HubClient_ConnectionStatusChanged;
    }


    private void HubClient_ConnectionStatusChanged(HubConnectionState c)
    {
        Dispatcher.UIThread.PostSafe(() => ConnectionStatus = c.ToString(), Logger);

        if (lastHubConnectionState != HubConnectionState.Connected && c == HubConnectionState.Connected)
        {
            Dispatcher.UIThread.PostSafe(async () => await LoadPayload(eventId, carNumber), Logger);
        }

        lastHubConnectionState = c;
    }

    public void Initialize(int eventId, string carNumber, bool showInClassOnly)
    {
        this.eventId = eventId;
        this.carNumber = carNumber;
        ShowInClassOnly = showInClassOnly;

        Dispatcher.UIThread.PostSafe(async () =>
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
                Logger.LogError(ex, "Failed to connect to event {EventId} for car {CarNumber}", eventId, carNumber);

                // Same reasoning as the settings screen this came from: the driver gets something
                // they can act on, and the exception goes to the log and Sentry on the line above.
                Message = "Could not connect to the event. Check your connection and try again.";
#if DEBUG
                Message += $"\n\nDebug info: {ex.Message}";
#endif
            }
        }, Logger);
    }

    private async Task LoadPayload(int eventId, string carNumber)
    {
        try
        {
            var payload = await eventClient.LoadInCarDriverModePayloadAsync(eventId, carNumber);
            if (payload == null)
            {
                Message = "No position data yet.";
            }
            else
            {
                Message = "Payload loaded";
                Receive(new InCarPositionUpdate(payload));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading the in-car payload for event {EventId}, car {CarNumber}", eventId, carNumber);
            Message = "Could not load the latest positions.";
        }
    }

    public void Unsubscribe()
    {
        _ = hubClient.UnsubscribeFromInCarDriverEventAsync(eventId, carNumber);
    }

    /// <summary>
    /// Detaches from the shared hub client and the messenger.
    /// </summary>
    /// <remarks>
    /// <see cref="HubClient"/> is a singleton, so its ConnectionStatusChanged event holds a strong
    /// reference to every instance that ever subscribed. Without this, each trip into driver mode
    /// leaks a view model that keeps reloading the payload on every reconnect.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            hubClient.ConnectionStatusChanged -= HubClient_ConnectionStatusChanged;
            WeakReferenceMessenger.Default.UnregisterAll(this);

            // Clears the hub's remembered subscription too, so a later reconnect doesn't
            // re-subscribe on behalf of a car nobody is watching any more.
            Unsubscribe();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error releasing the in-car positions view model");
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "InCarDriverActiveSettings" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
        Unsubscribe();
    }

    public void Receive(InCarPositionUpdate message)
    {
        Dispatcher.UIThread.PostSafe(() => ProcessInCarPayload(message.Value), Logger);
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
