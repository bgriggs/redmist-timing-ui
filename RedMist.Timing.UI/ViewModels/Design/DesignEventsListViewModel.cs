using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignEventsListViewModel : EventsListViewModel
{
    public DesignEventsListViewModel() : 
        base(new DesignEventClient(new DesignConfiguration()), new DebugLoggerFactory())
    {
        var e1 = new Event { EventId = 1, EventName = "World Racing League - Eagles Canyon", EventDate = "2/2/2025" };
        var e2 = new Event { EventId = 2, EventName = "World Racing League - Barber", EventDate = "2/2/2025" };
        Events.Add(new EventViewModel(e1));
        Events.Add(new EventViewModel(e2));
    }
}
