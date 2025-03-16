using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

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
    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private readonly ILoggerFactory loggerFactory;

    [ObservableProperty]
    private bool isLiveTimingTabVisible;

    private bool isResultsTabVisible;
    public bool IsResultsTabVisible
    {
        get => isResultsTabVisible;
        set
        {
            if (SetProperty(ref isResultsTabVisible, value))
            {
                WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(new RouterEvent { Path = "ResultsTab", Data = value }));
            }
        }
    }


    public MainViewModel(EventsListViewModel eventsListViewModel, LiveTimingViewModel liveTimingViewModel, HubClient hubClient, EventClient eventClient, ILoggerFactory loggerFactory)
    {
        EventsListViewModel = eventsListViewModel;
        LiveTimingViewModel = liveTimingViewModel;
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        this.loggerFactory = loggerFactory;
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

                ResultsViewModel = new ResultsViewModel(eventModel, hubClient, eventClient, loggerFactory);
                EventInformationViewModel = new EventInformationViewModel(eventModel);
                IsTimingVisible = true;

                // Set active tab
                IsResultsTabVisible = !hasLiveSession;
                IsLiveTimingTabVisible = hasLiveSession;
            }
        }
        else if (router.Path == "EventsList")
        {
            _ = Task.Run(EventsListViewModel.Initialize);
            _ = Task.Run(LiveTimingViewModel.UnsubscribeLiveAsync);

            IsEventsListVisible = true;
            IsTimingVisible = false;
        }
    }
}
