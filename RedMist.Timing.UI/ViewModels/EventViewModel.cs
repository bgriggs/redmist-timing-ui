﻿using Avalonia.Media.Imaging;
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
    public EventListSummary EventModel { get; }
    public string Name => EventModel.EventName;
    public string Organization => EventModel.OrganizationName;
    public Bitmap? OrganizationLogo { get; private set; }
    public ObservableCollection<ScheduleDayViewModel> ScheduleDays { get; } = [];
    public bool IsLive => EventModel.IsLive;


    public EventViewModel(EventListSummary eventModel, byte[]? organizationLogo)
    {
        EventModel = eventModel;
        if (organizationLogo is not null && organizationLogo.Length > 0)
        {
            using MemoryStream ms = new(organizationLogo);
            OrganizationLogo = Bitmap.DecodeToWidth(ms, 55);
        }
        
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
        if (eventViewModel is EventViewModel)
        {
            var routerEvent = new RouterEvent { Path = "EventStatus", Data = EventModel };
            WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
        }
    }
}
