using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System.Collections.Generic;

namespace RedMist.Timing.UI.ViewModels;

public class ResultsViewModel : ObservableObject
{
    public List<SessionViewModel> Sessions { get; } = [];
    public Event EventModel { get; }
    public string EventName => EventModel.EventName;
    public string OrganizationName => EventModel.OrganizationName;
    public string Dates => EventModel.EventDate;
    public string TrackName => EventModel.TrackName;
    public string Distance => EventModel.Distance;
    public string CourseConfiguration => EventModel.CourseConfiguration;


    public ResultsViewModel(Event eventModel)
    {
        foreach (var session in eventModel.Sessions)
        {
            Sessions.Add(new SessionViewModel(session));
        }

        EventModel = eventModel;
    }


    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }
}
