using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels;

public partial class EventViewModel : ObservableObject
{
    public Event EventModel { get; }
    public string Name => EventModel.EventName;

    public string Organization => EventModel.OrganizationName;

    public Bitmap? OrganizationLogo
    {
        get
        {
            if (EventModel.OrganizationLogo is not null)
            {
                using MemoryStream ms = new(EventModel.OrganizationLogo);
                return Bitmap.DecodeToWidth(ms, 55);
            }
            return null;
        }
    }

    public ObservableCollection<ScheduleDayViewModel> ScheduleDays { get; } = [];

    public bool IsLive
    {
        get
        {
            if (EventModel.Sessions is not null)
            {
                return EventModel.Sessions.Any(s => s.IsLive);
            }
            return false;
        }
    }


    public EventViewModel(Event eventModel)
    {
        EventModel = eventModel;
        if (EventModel.Schedule != null)
        {
            var today = DateTime.Now.Day;
            foreach (var day in EventModel.Schedule.Entries.GroupBy(e => e.DayOfEvent.Date))
            {
                if (today == day.Key.Day)
                {
                    ScheduleDays.Add(new ScheduleDayViewModel(day.Key, [.. day]));
                }

                if (ScheduleDays.Count >= 3)
                {
                    break;
                }
            }
        }
    }


    public void SelectEvent(object eventViewModel)
    {
        if (eventViewModel is EventViewModel evm)
        {
            var routerEvent = new RouterEvent { Path = "EventStatus", Data = EventModel };
            WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
        }
    }
}
