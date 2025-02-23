using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Clients;
using RedMist.TimingCommon.Models;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignEventClient : EventClient
{
    public DesignEventClient(IConfiguration configuration) : base(configuration)
    {
    }

    public override Task<Event[]> LoadRecentEventsAsync()
    {
        var e1 = new Event { EventId = 1, EventName = "World Racing League - Eagles Canyon", EventDate = "2/2/2025" };
        var e2 = new Event { EventId = 2, EventName = "World Racing League - Barber", EventDate = "2/2/2025" };
        return Task.FromResult<Event[]>([e1, e2]);
    }
}
