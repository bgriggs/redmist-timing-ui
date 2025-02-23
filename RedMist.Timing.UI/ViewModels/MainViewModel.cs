namespace RedMist.Timing.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public EventsListViewModel EventsListViewModel { get; }
    public LiveTimingViewModel LiveTimingViewModel { get; }

    public MainViewModel(EventsListViewModel eventsListViewModel, LiveTimingViewModel liveTimingViewModel)
    {
        EventsListViewModel = eventsListViewModel;
        LiveTimingViewModel = liveTimingViewModel;
    }
}
