namespace RedMist.Timing.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public EventsListViewModel EventsListViewModel { get; }
    public EventStatusViewModel EventStatusViewModel { get; }
    public LiveTimingViewModel LiveTimingViewModel { get; }

    public MainViewModel(EventsListViewModel eventsListViewModel, EventStatusViewModel eventStatusViewModel, LiveTimingViewModel liveTimingViewModel)
    {
        EventsListViewModel = eventsListViewModel;
        EventStatusViewModel = eventStatusViewModel;
        LiveTimingViewModel = liveTimingViewModel;
    }
}
