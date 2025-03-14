using RedMist.TimingCommon.Models.Configuration;
using System;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignEventInformationViewModel : EventInformationViewModel
{
    public DesignEventInformationViewModel() : base(new TimingCommon.Models.Event
    {
        EventName = "World Racing League - Barber 2025",
        EventDate = "February 28 - March 2, 2025",
        EventUrl = "https://www.racewrl.com",
        OrganizationName = "World Racing League",
        TrackName = "Barber Motorsports Park",
        Distance = "2.38 miles",
        CourseConfiguration = "Full Course",
        Schedule = new TimingCommon.Models.Configuration.EventSchedule
        {
            Entries = 
            [ 
                new EventScheduleEntry { Name = "Practice", DayOfEvent = new DateTime(2025, 2, 28), StartTime = new DateTime(2025, 2, 28, 8, 0, 0), EndTime = new DateTime(2025, 2, 28, 8, 40, 0) },
                new EventScheduleEntry { Name = "Race 1", DayOfEvent = new DateTime(2025, 3, 1), StartTime = new DateTime(2025, 3, 1, 8, 0, 0), EndTime = new DateTime(2025, 3, 1, 16, 0, 0) },
                new EventScheduleEntry { Name = "Race 2", DayOfEvent = new DateTime(2025, 3, 2), StartTime = new DateTime(2025, 3, 1, 8, 0, 0), EndTime = new DateTime(2025, 3, 2, 15, 0, 0) }
            ]
        },
        Broadcast = new BroadcasterConfig { CompanyName = "Driver's Eye", Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ" }
    })
    { }
}
