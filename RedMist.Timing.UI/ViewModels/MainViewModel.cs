using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IRecipient<ValueChangedMessage<RouterEvent>>
{
    public EventsListViewModel EventsListViewModel { get; }
    public LiveTimingViewModel LiveTimingViewModel { get; }

    [ObservableProperty]
    private bool isEventsListVisible = true;
    [ObservableProperty]
    private bool isEventVisible = false;
    [ObservableProperty]
    private ResultsViewModel? resultsViewModel;
    [ObservableProperty]
    private EventInformationViewModel? eventInformationViewModel;


    public MainViewModel(EventsListViewModel eventsListViewModel, LiveTimingViewModel liveTimingViewModel)
    {
        EventsListViewModel = eventsListViewModel;
        LiveTimingViewModel = liveTimingViewModel;
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
                _ = Task.Run(() => LiveTimingViewModel.InitializeAsync(eventModel.EventId));
                ResultsViewModel = new ResultsViewModel(eventModel);
                EventInformationViewModel = new EventInformationViewModel(eventModel);
                IsEventVisible = true;
            }
        }
        else if (router.Path == "EventsList")
        {
            _ = Task.Run(LiveTimingViewModel.UnsubscribeAsync);

            IsEventsListVisible = true;
            IsEventVisible = false;
        }
        else
        {
            IsEventsListVisible = false;
            IsEventVisible = false;
        }
    }
}
