using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels.InCarDriverMode;
using RedMist.TimingCommon.Models;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public enum TabTypes { LiveTiming, Results, ControlLog, EventInformation }

public partial class MainViewModel : ObservableObject, IRecipient<ValueChangedMessage<RouterEvent>>, IRecipient<SizeChangedNotification>
{
    public EventsListViewModel EventsListViewModel { get; }
    public LiveTimingViewModel LiveTimingViewModel { get; }

    [ObservableProperty]
    private bool isEventsListVisible = true;
    [ObservableProperty]
    private ResultsViewModel? resultsViewModel;
    [ObservableProperty]
    private EventInformationViewModel? eventInformationViewModel;
    [ObservableProperty]
    private ControlLogViewModel? controlLogViewModel;
    [ObservableProperty]
    private FlagsViewModel? flagsViewModel;
    [ObservableProperty]
    private InCarSettingsViewModel? inCarSettingsViewModel;

    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private readonly ILoggerFactory loggerFactory;
    private readonly ViewSizeService viewSizeService;
    private readonly EventContext eventContext;
    [ObservableProperty]
    private bool isContentVisible = false;
    [ObservableProperty]
    private bool isTimingTabStripVisible = false;
    [ObservableProperty]
    private bool isLiveTimingTabVisible;
    [ObservableProperty]
    private bool isLiveTimingTabSelected;

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

    [ObservableProperty]
    private bool isInformationTabSelected;

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
                    _ = ControlLogViewModel?.Initialize();
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
    
    private bool isFlagsTabSelected;
    public bool IsFlagsTabSelected
    {
        get => isFlagsTabSelected;
        set
        {
            if (SetProperty(ref isFlagsTabSelected, value) && value)
            {
                FlagsViewModel?.Initialize();
            }
        }
    }
    private const int FlagShowWidth = 500;

    [ObservableProperty]
    private bool isDriverModeVisible = false;


    public MainViewModel(EventsListViewModel eventsListViewModel, LiveTimingViewModel liveTimingViewModel, HubClient hubClient,
        EventClient eventClient, ILoggerFactory loggerFactory, ViewSizeService viewSizeService, EventContext eventContext)
    {
        EventsListViewModel = eventsListViewModel;
        LiveTimingViewModel = liveTimingViewModel;
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        this.loggerFactory = loggerFactory;
        this.viewSizeService = viewSizeService;
        this.eventContext = eventContext;
        WeakReferenceMessenger.Default.RegisterAll(this);

        if (Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatableLifetime)
        {
            activatableLifetime.Activated += ActivatableLifetime_Activated;
            //activatableLifetime.Deactivated += OnDeactivated;
        }
    }


    private void ActivatableLifetime_Activated(object? sender, ActivatedEventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new AppResumeNotification());
    }

    public async Task Initialize()
    {
        if (OperatingSystem.IsBrowser())
        {
            await BrowserInterop.InitializeJsModuleAsync();

            // Check for browser URL event ID parameter to go directly to that event
            var eventIdStr = BrowserInterop.GetQueryParameter("eventId");
            if (int.TryParse(eventIdStr, out var eventId) && eventId > 0)
            {
                var routerEvent = new RouterEvent { Path = "EventStatus", Data = eventId };
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

        IsContentVisible = true;
    }

    public async void Receive(ValueChangedMessage<RouterEvent> message)
    {
        var router = message.Value;
        if (router.Path == "EventStatus")
        {
            IsEventsListVisible = false;

            int eventId = 0;
            if (router.Data is EventListSummary @event)
            {
                eventId = @event.Id;
            }
            else if (router.Data is int id)
            {
                eventId = id;
            }

            Event? eventModel = null;
            if (eventId > 0)
            {
                eventModel = await eventClient.LoadEventAsync(eventId);
            }

            if (eventModel != null)
            {
                //var hasLiveSession = eventModel.Sessions.Any(s => s.IsLive);
                if (eventModel.IsLive)
                {
                    _ = Task.Run(() => LiveTimingViewModel.InitializeLiveAsync(eventModel));
                }

                ResultsViewModel = new ResultsViewModel(eventModel, hubClient, eventClient, loggerFactory, viewSizeService, eventContext);
                EventInformationViewModel = new EventInformationViewModel(eventModel);
                ControlLogViewModel = new ControlLogViewModel(eventModel, hubClient, eventClient);
                FlagsViewModel = new FlagsViewModel(eventModel, eventClient, eventContext);
                IsControlLogTabVisible = eventModel.HasControlLog && eventModel.IsLive;

                IsTimingTabStripVisible = true;
                IsResultsTabSelected = !eventModel.IsLive;
                IsLiveTimingTabVisible = eventModel.IsLive;
            }
        }
        else if (router.Path == "EventsList")
        {
            _ = Task.Run(EventsListViewModel.Initialize);
            _ = Task.Run(LiveTimingViewModel.UnsubscribeLiveAsync);
            _ = ControlLogViewModel?.UnsubscribeFromControlLogs();

            IsEventsListVisible = true;

            IsTimingTabStripVisible = false;
            IsDriverModeVisible = false;
        }
        else if (router.Path == "InCarDriverSettings")
        {
            InCarSettingsViewModel = new InCarSettingsViewModel(eventClient, hubClient);
            _ = InCarSettingsViewModel.Initialize();
            IsDriverModeVisible = true;

            IsEventsListVisible = false;
            IsTimingTabStripVisible = false;
        }
        else if (router.Path == "InCarDriverActiveSettings")
        {
            InCarSettingsViewModel?.BackToSettings();
        }
    }

    public bool HandleDeviceBackButton()
    {
        if (IsEventsListVisible)
        {
            return false; // There is nothing to go back to, so do not handle the back button
        }
        else // The main tab strip is visible
        {
            if (IsLiveTimingTabSelected)
            {
                LiveTimingViewModel.Back();
            }
            if (IsResultsTabSelected) // Session Results Tab
            {
                ResultsViewModel?.Back();
            }
            else if (IsInformationTabSelected) // Information Tab
            {
                EventInformationViewModel?.Back();
            }
            else if (IsControlLogTabSelected) // Control Log Tab
            {
                ControlLogViewModel?.Back();
            }
            else if (IsFlagsTabSelected) // Flags Tab
            {
                FlagsViewModel?.Back();
            }
            else
            {
                return false; // No tab is selected, so do not handle the back button
            }

            return true;
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
