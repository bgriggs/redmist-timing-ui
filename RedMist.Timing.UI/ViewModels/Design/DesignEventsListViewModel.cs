namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignEventsListViewModel : EventsListViewModel
{
    public DesignEventsListViewModel() : 
        base(new DesignEventClient(new DesignConfiguration()), new DesignOrganizationClient(), new DebugLoggerFactory())
    {
        // Set design-time paging properties
        CurrentPage = 2;
        HasMorePages = true;
    }
}
