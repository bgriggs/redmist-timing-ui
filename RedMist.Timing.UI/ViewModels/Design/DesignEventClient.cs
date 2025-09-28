using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Clients;
using RedMist.TimingCommon.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignEventClient : EventClient
{
    public DesignEventClient(IConfiguration configuration) : base(configuration, new DebugLoggerFactory())
    {
    }

    public override Task<List<EventListSummary>> LoadRecentEventsAsync()
    {
        var e1 = new EventListSummary { Id = 1, EventName = "World Racing League - Eagles Canyon", EventDate = "2/2/2025" };
        var e2 = new EventListSummary { Id = 2, EventName = "World Racing League - Barber", EventDate = "2/2/2025" };
        return Task.FromResult<List<EventListSummary>>([e1, e2]);
    }
}
