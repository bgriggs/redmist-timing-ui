using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Clients;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Configuration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignEventClient(IConfiguration configuration) : EventClient(configuration, new DebugLoggerFactory())
{
    public override Task<List<EventListSummary>> LoadRecentEventsAsync()
    {
        var e1 = new EventListSummary { Id = 1, OrganizationId = 1, EventName = "World Racing League - Eagles Canyon", EventDate = "2025-02-02" };
        var e2 = new EventListSummary { Id = 2, OrganizationId = 1, EventName = "World Racing League - Barber 2026 in Alabama, USA", EventDate = "2025-02-02", IsLive = true };
        var e3 = new EventListSummary { Id = 2, OrganizationId = 1, EventName = "World Racing League - Sim", EventDate = "2025-02-02", IsSimulation = true, IsLive = true };

        e1.Schedule = new EventSchedule
        {
            Name = "Event Schedule",
            Entries =
            [
                new EventScheduleEntry { Name = "Practice 1", StartTime = DateTime.Now, EndTime = DateTime.Now.AddHours(1) },
                new EventScheduleEntry { Name = "Qualifying", StartTime = DateTime.Now.AddHours(2), EndTime = DateTime.Now.AddHours(3) },
                new EventScheduleEntry { Name = "Race 1", StartTime = DateTime.Now.AddHours(4), EndTime = DateTime.Now.AddHours(8) },
            ]
        };

        e3.Schedule = new EventSchedule
        {
            Name = "Event Schedule",
            Entries =
            [
                new EventScheduleEntry { Name = "Practice 1", StartTime = DateTime.Now, EndTime = DateTime.Now.AddHours(1) },
                new EventScheduleEntry { Name = "Qualifying", StartTime = DateTime.Now.AddHours(2), EndTime = DateTime.Now.AddHours(3) },
                new EventScheduleEntry { Name = "Race 1", StartTime = DateTime.Now.AddHours(4), EndTime = DateTime.Now.AddHours(8) },
            ]
        };

        return Task.FromResult<List<EventListSummary>>([e1, e2, e3]);
    }
}
