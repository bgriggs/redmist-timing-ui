namespace RedMist.Timing.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public string Greeting => "Welcome to Avalonia!";

    public EventsListViewModel EventsListViewModel { get; }
    public EventStatusViewModel EventStatusViewModel { get; }

    public MainViewModel(EventsListViewModel eventsListViewModel, EventStatusViewModel eventStatusViewModel)
    {
        EventsListViewModel = eventsListViewModel;
        EventStatusViewModel = eventStatusViewModel;
    }
}
