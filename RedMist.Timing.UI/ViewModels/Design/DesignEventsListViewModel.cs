namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignEventsListViewModel : EventsListViewModel
{
    public DesignEventsListViewModel() : 
        base(new DesignEventClient(new DesignConfiguration()), new DebugLoggerFactory())
    {
    }
}
