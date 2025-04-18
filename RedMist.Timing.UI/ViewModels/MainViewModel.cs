﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Views;
using RedMist.TimingCommon.Models;
using SkiaSharp;
using System;
using System.Linq;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace RedMist.Timing.UI.ViewModels;

public enum TabTypes { LiveTiming, Results, ControlLog, EventInformation }

public partial class MainViewModel : ObservableObject, IRecipient<ValueChangedMessage<RouterEvent>>, IRecipient<SizeChangedNotification>
{
    public EventsListViewModel EventsListViewModel { get; }
    public LiveTimingViewModel LiveTimingViewModel { get; }

    [ObservableProperty]
    private bool isEventsListVisible = true;
    [ObservableProperty]
    private bool isTimingVisible = false;
    [ObservableProperty]
    private ResultsViewModel? resultsViewModel;
    [ObservableProperty]
    private EventInformationViewModel? eventInformationViewModel;
    [ObservableProperty]
    private ControlLogViewModel? controlLogViewModel;
    [ObservableProperty]
    private FlagsViewModel? flagsViewModel;

    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private readonly ILoggerFactory loggerFactory;
    private readonly ViewSizeService viewSizeService;

    [ObservableProperty]
    private bool isContentVisible = false;

    [ObservableProperty]
    private bool isLiveTimingTabVisible;

    private bool isResultsTabSelected;
    public bool IsResultsTabSelected
    {
        get => isResultsTabSelected;
        set
        {
            if (SetProperty(ref isResultsTabSelected, value))
            {
                WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(new RouterEvent { Path = "ResultsTab", Data = value }));
            }
        }
    }

    private bool isControlLogTabSelected;
    public bool IsControlLogTabSelected
    {
        get => isControlLogTabSelected;
        set
        {
            if (SetProperty(ref isControlLogTabSelected, value))
            {
                if (value)
                {
                    _ = ControlLogViewModel?.SubscribeToControlLogs();
                }
                else
                {
                    _ = ControlLogViewModel?.UnsubscribeFromControlLogs();
                }
            }
        }
    }
    [ObservableProperty]
    private bool isControlLogTabVisible;
    [ObservableProperty]
    private bool isFlagsTabVisible;
    private const int FlagShowWidth = 500;


    public MainViewModel(EventsListViewModel eventsListViewModel, LiveTimingViewModel liveTimingViewModel, HubClient hubClient,
        EventClient eventClient, ILoggerFactory loggerFactory, ViewSizeService viewSizeService)
    {
        EventsListViewModel = eventsListViewModel;
        LiveTimingViewModel = liveTimingViewModel;
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        this.loggerFactory = loggerFactory;
        this.viewSizeService = viewSizeService;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public async Task Initialize()
    {
        if (OperatingSystem.IsBrowser())
        {
            await BrowserInterop.InitializeJsModuleAsync();
            //string currentUrl = BrowserInterop.GetCurrentUrl();

            var eventIdStr = BrowserInterop.GetQueryParameter("eventId");
            if (int.TryParse(eventIdStr, out var eventId) && eventId > 0)
            {
                try
                {
                    var @event = await eventClient.LoadEventAsync(eventId);
                    if (@event != null)
                    {
                        var routerEvent = new RouterEvent { Path = "EventStatus", Data = @event };
                        Receive(new ValueChangedMessage<RouterEvent>(routerEvent));

                        LiveTimingViewModel.AllowEventList = false;
                        if (ControlLogViewModel != null)
                        {
                            ControlLogViewModel.AllowEventList = false;
                        }
                        if (EventInformationViewModel != null)
                        {
                            EventInformationViewModel.AllowEventList = false;
                        }
                        if (EventInformationViewModel != null)
                        {
                            EventInformationViewModel.AllowEventList = false;
                        }
                        if (FlagsViewModel != null)
                        {
                            FlagsViewModel.AllowEventList = false;
                        }
                        if (ResultsViewModel != null)
                        {
                            ResultsViewModel.AllowEventList = false;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        IsContentVisible = true;
    }

    public void Receive(ValueChangedMessage<RouterEvent> message)
    {
        var router = message.Value;
        if (router.Path == "EventStatus")
        {
            IsEventsListVisible = false;
            if (router.Data is Event eventModel)
            {
                var hasLiveSession = eventModel.Sessions.Any(s => s.IsLive);
                if (hasLiveSession)
                {
                    _ = Task.Run(() => LiveTimingViewModel.InitializeLiveAsync(eventModel));
                }

                ResultsViewModel = new ResultsViewModel(eventModel, hubClient, eventClient, loggerFactory, viewSizeService);
                EventInformationViewModel = new EventInformationViewModel(eventModel);
                ControlLogViewModel = new ControlLogViewModel(eventModel, hubClient);
                FlagsViewModel = new FlagsViewModel(eventModel);
                IsTimingVisible = true;
                IsControlLogTabVisible = eventModel.HasControlLog;

                IsResultsTabSelected = !hasLiveSession;
                IsLiveTimingTabVisible = hasLiveSession;
            }
        }
        else if (router.Path == "EventsList")
        {
            _ = Task.Run(EventsListViewModel.Initialize);
            _ = Task.Run(LiveTimingViewModel.UnsubscribeLiveAsync);
            _ = ControlLogViewModel?.UnsubscribeFromControlLogs();

            IsEventsListVisible = true;
            IsTimingVisible = false;
        }
    }

    /// <summary>
    /// Handles notifications related to size changes.
    /// </summary>
    public void Receive(SizeChangedNotification message)
    {
        IsFlagsTabVisible = viewSizeService.CurrentSize.Width > FlagShowWidth;
    }
}
