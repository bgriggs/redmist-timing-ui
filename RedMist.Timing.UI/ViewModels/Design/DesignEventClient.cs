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
        var e1 = new EventListSummary { Id = 1, EventName = "World Racing League - Eagles Canyon", EventDate = "2025-02-02" };
        var e2 = new EventListSummary { Id = 2, EventName = "World Racing League - Barber", EventDate = "2025-02-02" };
        var e3 = new EventListSummary { Id = 2, EventName = "World Racing League - Sim", EventDate = "2025-02-02", IsSimulation = true, IsLive = true };
        return Task.FromResult<List<EventListSummary>>([e1, e2, e3]);
    }
}
