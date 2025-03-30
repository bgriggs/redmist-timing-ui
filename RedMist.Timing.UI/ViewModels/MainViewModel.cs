using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using System.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public enum TabTypes { LiveTiming, Results, ControlLog, EventInformation }

public partial class MainViewModel : ObservableObject, IRecipient<ValueChangedMessage<RouterEvent>>
{
    public EventsListViewModel EventsListViewModel { get; }
    public LiveTimingViewModel LiveTimingViewModel { get; }

    [ObservableProperty]
    private bool isEventsListVisible = true;
    [ObservableProperty]
    private bool isTimingVisible = false;
    [ObservableProperty]
    private ResultsViewModel? resultsViewModel;
    [ObservableProperty]
    private EventInformationViewModel? eventInformationViewModel;
    [ObservableProperty]
    private ControlLogViewModel? controlLogViewModel;

    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private readonly ILoggerFactory loggerFactory;
    private readonly ViewSizeService viewSizeService;
    [ObservableProperty]
    private bool isLiveTimingTabVisible;

    private bool isResultsTabSelected;
    public bool IsResultsTabSelected
    {
        get => isResultsTabSelected;
        set
        {
            if (SetProperty(ref isResultsTabSelected, value))
            {
                WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(new RouterEvent { Path = "ResultsTab", Data = value }));
            }
        }
    }

    private bool isControlLogTabSelected;
    public bool IsControlLogTabSelected
    {
        get => isControlLogTabSelected;
        set
        {
            if (SetProperty(ref isControlLogTabSelected, value))
            {
                if (value)
                {
                    _ = ControlLogViewModel?.SubscribeToControlLogs();
                }
                else
                {
                    _ = ControlLogViewModel?.UnsubscribeFromControlLogs();
                }
            }
        }
    }
    [ObservableProperty]
    private bool isControlLogTabVisible;


    public MainViewModel(EventsListViewModel eventsListViewModel, LiveTimingViewModel liveTimingViewModel, HubClient hubClient, 
        EventClient eventClient, ILoggerFactory loggerFactory, ViewSizeService viewSizeService)
    {
        EventsListViewModel = eventsListViewModel;
        LiveTimingViewModel = liveTimingViewModel;
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        this.loggerFactory = loggerFactory;
        this.viewSizeService = viewSizeService;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public void Receive(ValueChangedMessage<RouterEvent> message)
    {
        var router = message.Value;
        if (router.Path == "EventStatus")
        {
            IsEventsListVisible = false;
            if (router.Data is Event eventModel)
            {
                var hasLiveSession = eventModel.Sessions.Any(s => s.IsLive);
                if (hasLiveSession)
                {
                    _ = Task.Run(() => LiveTimingViewModel.InitializeLiveAsync(eventModel));
                }

                ResultsViewModel = new ResultsViewModel(eventModel, hubClient, eventClient, loggerFactory, viewSizeService);
                EventInformationViewModel = new EventInformationViewModel(eventModel);
                ControlLogViewModel = new ControlLogViewModel(eventModel, hubClient);
                IsTimingVisible = true;
                IsControlLogTabVisible = eventModel.HasControlLog;

                IsResultsTabSelected = !hasLiveSession;
                IsLiveTimingTabVisible = hasLiveSession;
            }
        }
        else if (router.Path == "EventsList")
        {
            _ = Task.Run(EventsListViewModel.Initialize);
            _ = Task.Run(LiveTimingViewModel.UnsubscribeLiveAsync);
            _ = ControlLogViewModel?.UnsubscribeFromControlLogs();

            IsEventsListVisible = true;
            IsTimingVisible = false;
        }
    }
}
