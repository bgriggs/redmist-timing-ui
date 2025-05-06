using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignEventsListViewModel : EventsListViewModel
{
    public DesignEventsListViewModel() : 
        base(new DesignEventClient(new DesignConfiguration()), new DesignOrganizationClient(), new DebugLoggerFactory())
    {
        //var s1 = new Session { IsLive = true };
        var e1 = new EventListSummary { Id = 1, EventName = "World Racing League - Eagles Canyon", EventDate = "2/2/2025" };
        var e2 = new EventListSummary { Id = 2, EventName = "World Racing League - Barber", EventDate = "2/2/2025" };
        Events.Add(new EventViewModel(e1, null) { });
        Events.Add(new EventViewModel(e2, null));
    }
}
