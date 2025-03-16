﻿using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class ResultsViewModel : ObservableObject, IRecipient<ValueChangedMessage<RouterEvent>>
{
    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    public Event EventModel { get; }
    public string Name => EventModel.EventName;
    public string OrganizationName => EventModel.OrganizationName;
    public string Dates => EventModel.EventDate;
    public string TrackName => EventModel.TrackName;
    public string Distance => EventModel.Distance;
    public string CourseConfiguration => EventModel.CourseConfiguration;

    [ObservableProperty]
    private LiveTimingViewModel? liveTimingViewModel;
    [ObservableProperty]
    private bool isLiveTimingVisible;
    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private readonly ILoggerFactory loggerFactory;


    public ResultsViewModel(Event eventModel, HubClient hubClient, EventClient eventClient, ILoggerFactory loggerFactory)
    {
        EventModel = eventModel;
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        this.loggerFactory = loggerFactory;

        WeakReferenceMessenger.Default.RegisterAll(this);

        InitializeSessions(eventModel.Sessions);
    }


    private void InitializeSessions(Session[] sessions)
    {
        Sessions.Clear();
        foreach (var session in sessions.Where(s => s.EndTime.HasValue).OrderByDescending(s => s.StartTime))
        {
            Sessions.Add(new SessionViewModel(session));
        }
    }

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    public async void Receive(ValueChangedMessage<RouterEvent> message)
    {
        // Show live timing when session results are selected
        if (message.Value.Path == "SessionResults" && message.Value.Data is Session session)
        {
            Payload? results = null;
            try
            {
                results = await eventClient.LoadSessionResultsAsync(session.EventId, session.Id);
            }
            catch //(Exception ex)
            {
                //logger.LogError(ex, "Error loading session results");
            }

            LiveTimingViewModel = new LiveTimingViewModel(hubClient, eventClient, loggerFactory) { BackRouterPath = "SessionResultsList" };
            if (results != null)
            {
                LiveTimingViewModel.ProcessUpdate(results);
            }
            IsLiveTimingVisible = true;
        }
        else if (message.Value.Path == "SessionResultsList")
        {
            RefreshSessions();
            IsLiveTimingVisible = false;
            LiveTimingViewModel = null;
        }
        else if (message.Value.Path == "ResultsTab" && message.Value.Data is bool isResultsTabVisible && isResultsTabVisible)
        {
            RefreshSessions();
            IsLiveTimingVisible = false;
            LiveTimingViewModel = null;
        }
    }

    private void RefreshSessions()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var sessions = await eventClient.LoadSessionsAsync(EventModel.EventId);
                Dispatcher.UIThread.Post(() => InitializeSessions([.. sessions]), DispatcherPriority.Background);
            }
            catch //(Exception ex)
            {
                //logger.LogError(ex, "Error loading sessions");
            }
        });
    }
}
