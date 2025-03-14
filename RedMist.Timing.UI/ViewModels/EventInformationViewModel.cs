using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public class EventInformationViewModel : ObservableObject
{
    public Event EventModel { get; }
    public string Name => EventModel.EventName;
    public string OrganizationName => EventModel.OrganizationName;
    public string Dates => EventModel.EventDate;
    public string TrackName => EventModel.TrackName;
    public string Distance => EventModel.Distance;
    public string CourseConfiguration => EventModel.CourseConfiguration;
    public bool IsScheduleVisible => EventModel.Schedule?.Entries.Count > 0;
    public ObservableCollection<ScheduleDayViewModel> ScheduleDays { get; } = [];
    public string? BroadcastCompanyName => EventModel.Broadcast?.CompanyName;
    public string? BroadcastUrl => EventModel.Broadcast?.Url;
    public bool IsBroadcastVisible => EventModel.Broadcast != null && !string.IsNullOrEmpty(EventModel.Broadcast.Url);


    public EventInformationViewModel(Event eventModel)
    {
        EventModel = eventModel;

        if (EventModel.Schedule != null)
        {
            foreach (var day in EventModel.Schedule.Entries.GroupBy(e => e.DayOfEvent.Date))
            {
                ScheduleDays.Add(new ScheduleDayViewModel(day.Key, [.. day]));
            }
        }
    }


    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    public void LaunchDetailsUrl() { }
    public void LaunchBroadcastUrl() { }
}
