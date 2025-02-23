namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignMainViewModel : MainViewModel
{
    public DesignMainViewModel() :
        base(new EventsListViewModel(new Clients.EventClient(new DesignConfiguration()), new DebugLoggerFactory()), 
        new DesignLiveTimingViewModel())
    {
    }
}
