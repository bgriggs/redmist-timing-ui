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

public partial class EventInformationViewModel : ObservableObject
{
    public Event EventModel { get; }
    public string Name => EventModel.EventName;
    public string OrganizationName => EventModel.OrganizationName;
    public string Dates
     {
        get
        {
            _ = DateTime.TryParse(EventModel.EventDate, out DateTime parsedDate);
            return parsedDate == default
                ? EventModel.EventDate
                : parsedDate.ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    public string TrackName => EventModel.TrackName;
    public string Distance => EventModel.Distance;
    public string CourseConfiguration => EventModel.CourseConfiguration;
    public bool IsTrackVisible => !string.IsNullOrEmpty(EventModel.TrackName);
    public bool IsScheduleVisible => EventModel.Schedule?.Entries.Count > 0;
    public ObservableCollection<ScheduleDayViewModel> ScheduleDays { get; } = [];
    public string? BroadcastCompanyName => EventModel.Broadcast?.CompanyName;
    public string? BroadcastUrl => EventModel.Broadcast?.Url;
    public bool IsBroadcastVisible => EventModel.Broadcast != null && !string.IsNullOrEmpty(EventModel.Broadcast.Url);
    public bool IsEventDetailsVisible => !string.IsNullOrEmpty(EventModel.EventUrl);
    public Bitmap? OrganizationLogo
    {
        get
        {
            if (EventModel.OrganizationLogo is not null && EventModel.OrganizationLogo.Length > 0)
            {
                using MemoryStream ms = new(EventModel.OrganizationLogo);
                return Bitmap.DecodeToWidth(ms, 165);
            }
            return null;
        }
    }

    [ObservableProperty]
    private bool allowEventList = true;

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

    public void LaunchDetailsUrl()
    {
        WeakReferenceMessenger.Default.Send(new LauncherEvent(EventModel.EventUrl));
    }

    public void LaunchBroadcastUrl()
    {
        if (EventModel.Broadcast != null && !string.IsNullOrEmpty(EventModel.Broadcast.Url))
        {
            WeakReferenceMessenger.Default.Send(new LauncherEvent(EventModel.Broadcast.Url));
        }
    }
}
