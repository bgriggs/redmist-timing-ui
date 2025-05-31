using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.InCarDriverMode;

public partial class InCarPositionsViewModel : ObservableObject, IRecipient<InCarPositionUpdate>
{
    private readonly HubClient hubClient;
    private int eventId;
    private string carNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAheadOutOfClassVisible))]
    private bool showInClassOnly;
    [ObservableProperty]
    private string positionInClass = string.Empty;
    [ObservableProperty]
    private string positionOverall = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(IsAheadOutOfClassVisible))]
    public bool hasOutOfClassAhead;

    public bool IsAheadOutOfClassVisible => HasOutOfClassAhead && !ShowInClassOnly;

    [ObservableProperty]
    private Flags flag;


    public InCarPositionsViewModel(HubClient hubClient)
    {
        this.hubClient = hubClient;
        DriversCar.SetAsDriver();
        CarAheadOutOfClass.SetAsOutOfClass();
    }


    public async Task Initialize(int eventId, string carNumber, bool showInClassOnly)
    {
        this.eventId = eventId;
        this.carNumber = carNumber;
        ShowInClassOnly = showInClassOnly;
        await hubClient.SubscribeToInCarDriverEventAsync(eventId, carNumber);
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
        PositionInClass = message.Value.PositionInClass;
        PositionOverall = message.Value.PositionOverall;
        Flag = message.Value.Flag;

        if (message.Value.Cars.Count == 3)
        {
            CarAhead.Update(message.Value.Cars[0]);
            DriversCar.Update(message.Value.Cars[1]);
            CarBehind.Update(message.Value.Cars[2]);
            HasOutOfClassAhead = false;
        }
        else if (message.Value.Cars.Count == 4)
        {
            CarAhead.Update(message.Value.Cars[0]);
            CarAheadOutOfClass.Update(message.Value.Cars[1]);
            DriversCar.Update(message.Value.Cars[2]);
            CarBehind.Update(message.Value.Cars[3]);
            HasOutOfClassAhead = true;
        }

        Message = DateTime.Now.ToString();
    }
}
