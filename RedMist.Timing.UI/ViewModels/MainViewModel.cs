using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels.InCarDriverMode;
using RedMist.TimingCommon.Models;
using System;
using System.ComponentModel;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public enum TabTypes { LiveTiming, Results, ControlLog, EventInformation }

public partial class MainViewModel : ObservableObject, IRecipient<ValueChangedMessage<RouterEvent>>,
    IRecipient<SizeChangedNotification>, IRecipient<EventAccessDeniedNotification>,
    IRecipient<AccessCodeRequestNotification>
{
    public event Action<bool>? IsTimingTabStripVisibleChanged;
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
    private SettingsViewModel? settingsViewModel;
    [ObservableProperty]
    private InCarSettingsViewModel? inCarSettingsViewModel;

    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private readonly ILoggerFactory loggerFactory;
    private readonly ViewSizeService viewSizeService;
    private readonly EventContext eventContext;
    private readonly IPlatformDetectionService platformDetectionService;
    private readonly IVersionCheckService versionCheckService;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;
    private readonly OrganizationIconCacheService iconCacheService;
    private readonly SponsorRotatorViewModel sponsorRotator;
    private readonly IPreferencesService preferencesService;
    private readonly IScreenWakeService screenWakeService;
    private readonly ILogger Logger;
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
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await (ControlLogViewModel?.Initialize() ?? Task.CompletedTask);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Error initializing control log");
                        }
                    });
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await (ControlLogViewModel?.UnsubscribeFromControlLogs() ?? Task.CompletedTask);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Error unsubscribing from control logs");
                        }
                    });
                }
            }
        }
    }
    [ObservableProperty]
    //[NotifyPropertyChangedFor(nameof(ShowControlLogTab))]
    private bool isControlLogAvailable;
    //[ObservableProperty]
    //[NotifyPropertyChangedFor(nameof(ShowControlLogTab))]
    //private bool isControlLogTabVisible;
    //public bool ShowControlLogTab => IsControlLogAvailable && IsControlLogTabVisible;

    //[ObservableProperty]
    //private bool isFlagsTabVisible;
    
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

    [ObservableProperty]
    private bool isSettingsTabSelected;

    //private const int FlagShowWidth = 500;
    //private const int ControlLogShowWidth = 450;
    private const int VersionCheckTimeoutSeconds = 5;

    [ObservableProperty]
    private bool isDriverModeVisible = false;

    [ObservableProperty]
    private VersionCheckResult? optionalUpdateNotification;

    [ObservableProperty]
    private bool isUpdateNotificationVisible = false;

    [ObservableProperty]
    private VersionCheckResult? mandatoryUpdateResult;

    [ObservableProperty]
    private bool isMandatoryUpdateVisible = false;

    [ObservableProperty]
    private AccessCodePromptViewModel? accessCodePromptViewModel;

    [ObservableProperty]
    private bool isAccessCodePromptVisible = false;

    private readonly EventAccessCodeStore accessCodeStore;
    private Event? currentEvent;
    private string currentEventOrganizationName = string.Empty;

    /// <summary>
    /// The event being navigated into, recorded before the event itself is loaded.
    /// </summary>
    /// <remarks>
    /// LoadEvent is gated by the access code on the server, so a private event answers 401 there and
    /// <see cref="currentEvent"/> is never assigned - it still holds the previous event, or nothing at
    /// all on a cold start. <see cref="Receive(EventAccessDeniedNotification)"/> matches the denial
    /// against this so it can still raise the prompt; without it the first visit to a private event
    /// left the viewer on a blank screen with no way to enter a code.
    /// </remarks>
    private PendingEventNavigation? pendingEventAccess;

    /// <summary>
    /// What is known about an event before it has been loaded: enough to title the access code prompt,
    /// plus the route to replay once a code has been accepted.
    /// </summary>
    private sealed record PendingEventNavigation(int EventId, string EventName, string OrganizationName, RouterEvent Route);


    public MainViewModel(EventsListViewModel eventsListViewModel, LiveTimingViewModel liveTimingViewModel, HubClient hubClient,
        EventClient eventClient, ILoggerFactory loggerFactory, ViewSizeService viewSizeService, EventContext eventContext,
        IPlatformDetectionService platformDetectionService, IVersionCheckService versionCheckService, IHttpClientFactory httpClientFactory, IConfiguration configuration, OrganizationIconCacheService iconCacheService, SponsorRotatorViewModel sponsorRotator, IPreferencesService preferencesService, IScreenWakeService screenWakeService, EventAccessCodeStore accessCodeStore)
    {
        EventsListViewModel = eventsListViewModel;
        LiveTimingViewModel = liveTimingViewModel;
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        this.loggerFactory = loggerFactory;
        this.viewSizeService = viewSizeService;
        this.eventContext = eventContext;
        this.platformDetectionService = platformDetectionService;
        this.versionCheckService = versionCheckService;
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        this.iconCacheService = iconCacheService;
        this.sponsorRotator = sponsorRotator;
        this.preferencesService = preferencesService;
        this.screenWakeService = screenWakeService;
        this.accessCodeStore = accessCodeStore;
        Logger = loggerFactory.CreateLogger(GetType().Name);
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
        // Perform version check before loading events list (User Stories 1, 2, 3)
        await PerformVersionCheckAsync();
        
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

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsTimingTabStripVisible))
        {
            IsTimingTabStripVisibleChanged?.Invoke(IsTimingTabStripVisible);
        }
    }

    public async void Receive(ValueChangedMessage<RouterEvent> message)
    {
        var router = message.Value;

        // Give up any event we were waiting on a code for. Only the navigation that is in flight
        // has a use for it, and a record left behind would answer a later denial for that event by
        // prompting over whatever the viewer had moved on to - driver mode, say - and then replaying
        // a route they had already left. The branch below sets it again for its own load.
        pendingEventAccess = null;

        try
        {
            if (router.Path == "EventStatus")
            {
                IsEventsListVisible = false;

                int eventId = 0;
                string organizationName = string.Empty;
                string eventName = string.Empty;
                if (router.Data is EventListSummary @event)
                {
                    eventId = @event.Id;
                    organizationName = @event.OrganizationName;
                    eventName = @event.EventName;
                }
                else if (router.Data is int id)
                {
                    eventId = id;
                }

                // Record the destination before loading it: the load is the call that gets denied,
                // and the denial has to be able to find its way back to an event to prompt for.
                pendingEventAccess = new PendingEventNavigation(eventId, eventName, organizationName, router);

                Event? eventModel = null;
                if (eventId > 0)
                {
                    eventModel = await eventClient.LoadEventAsync(eventId);
                }

                if (eventModel == null)
                {
                    // Not a denial - the load failed or was skipped, and nothing is coming. Holding
                    // the record until the next navigation would leave a denial for this event able
                    // to prompt in the meantime.
                    pendingEventAccess = null;
                    return;
                }

                currentEvent = eventModel;
                currentEventOrganizationName = organizationName;
                pendingEventAccess = null;

                if (eventModel.IsPrivate && string.IsNullOrEmpty(accessCodeStore.Get(eventModel.EventId)))
                {
                    ShowAccessCodePrompt(eventModel, organizationName);
                    return;
                }

                await SetupForEventAsync(eventModel);
            }
            else if (router.Path == "EventsList")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await EventsListViewModel.InitializeAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error initializing events list");
                    }
                });
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await LiveTimingViewModel.UnsubscribeLiveAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error unsubscribing from live timing");
                    }
                });
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (ControlLogViewModel != null)
                            await ControlLogViewModel.UnsubscribeFromControlLogs();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error unsubscribing from control logs");
                    }
                });

                IsEventsListVisible = true;

                IsTimingTabStripVisible = false;
                IsDriverModeVisible = false;

                // Leaving driver mode: release the hub subscription and the screen wake lock now
                // rather than waiting for the next trip into driver mode to replace it.
                InCarSettingsViewModel?.Dispose();
                InCarSettingsViewModel = null;

                // Same for the results tab, which may still be holding a session's timing grid -
                // and, behind any row the user expanded, a control log subscription on the hub.
                // The back button from an open session lands here without passing through the
                // results view model's own router branches, so this is the only place that catches
                // it before the next event replaces the view model wholesale.
                ResultsViewModel?.Dispose();
                ResultsViewModel = null;

                IsAccessCodePromptVisible = false;
                AccessCodePromptViewModel = null;
                currentEvent = null;
                currentEventOrganizationName = string.Empty;
            }
            else if (router.Path == "InCarDriverSettings")
            {
                // Release the previous one's hub subscription before dropping it.
                InCarSettingsViewModel?.Dispose();
                InCarSettingsViewModel = new InCarSettingsViewModel(eventClient, hubClient, accessCodeStore,
                    preferencesService, screenWakeService, loggerFactory);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await InCarSettingsViewModel.Initialize();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error initializing in-car settings");
                    }
                });
                
                IsDriverModeVisible = true;

                IsEventsListVisible = false;
                IsTimingTabStripVisible = false;
            }
            else if (router.Path == "InCarDriverActiveSettings")
            {
                InCarSettingsViewModel?.BackToSettings();
            }
        }
        catch (EventAccessDeniedException ex)
        {
            // Expected for a private event the viewer has no code for. EventClient has already sent
            // the notification that raises the prompt, so this is a navigation that stops early
            // rather than a failure - logging it as an error only fills the crash reporter with it.
            Logger.LogInformation("Access code required for event {EventId}; prompting", ex.EventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling router message for path {Path}", router?.Path);
        }
    }

    private void ShowAccessCodePrompt(Event eventModel, string organizationName,
        Func<Task>? onSuccess = null, Action? onCancel = null)
    {
        var orgName = !string.IsNullOrEmpty(organizationName) ? organizationName : eventModel.OrganizationName;
        ShowAccessCodePrompt(eventModel.EventId, eventModel.EventName, orgName,
            onSuccess ?? (() => SetupForEventAsync(eventModel)), onCancel);
    }

    /// <summary>
    /// Raises the access code prompt for an event that has not been loaded, and so is known only by
    /// what the events list said about it.
    /// </summary>
    /// <remarks>
    /// <paramref name="onSuccess"/> is required here, unlike on the overload above: with no event
    /// model there is nothing sensible to fall back to, and a prompt that closes on the right code
    /// without going anywhere is the dead end this whole path exists to remove.
    /// </remarks>
    private void ShowAccessCodePrompt(int eventId, string eventName, string organizationName,
        Func<Task> onSuccess, Action? onCancel = null)
    {
        AccessCodePromptViewModel = new AccessCodePromptViewModel(
            eventId,
            eventName,
            organizationName,
            eventClient,
            accessCodeStore,
            loggerFactory,
            onSuccess: async () =>
            {
                IsAccessCodePromptVisible = false;
                AccessCodePromptViewModel = null;
                await onSuccess();
            },
            onCancel: () =>
            {
                IsAccessCodePromptVisible = false;
                AccessCodePromptViewModel = null;
                if (onCancel != null)
                {
                    onCancel();
                }
                else
                {
                    WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(new RouterEvent { Path = "EventsList" }));
                }
            });
        IsAccessCodePromptVisible = true;
    }

    private async Task SetupForEventAsync(Event eventModel)
    {
        if (eventModel.IsLive)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Note: InitializeLiveAsync swallows its own exceptions, so an access-denied
                    // response never surfaces here. The re-prompt happens through
                    // EventAccessDeniedNotification, which EventClient raises on any 401.
                    await LiveTimingViewModel.InitializeLiveAsync(eventModel);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error initializing live timing");
                }
            });
        }

        // Opening an event while one is already set up replaces these. Only the results view model
        // owns anything that has to be handed back - a session's timing grid, and the rows in it.
        ResultsViewModel?.Dispose();
        ResultsViewModel = new ResultsViewModel(eventModel, hubClient, eventClient, loggerFactory, viewSizeService, eventContext, httpClientFactory, configuration, iconCacheService, sponsorRotator);
        EventInformationViewModel = new EventInformationViewModel(eventModel, iconCacheService, loggerFactory);
        ControlLogViewModel = new ControlLogViewModel(eventModel, hubClient, eventClient, eventContext, iconCacheService, loggerFactory);
        FlagsViewModel = new FlagsViewModel(eventModel, eventClient, eventContext, httpClientFactory, configuration, iconCacheService, loggerFactory);
        var isMobile = platformDetectionService.GetCurrentPlatform() is AppPlatform.Android or AppPlatform.iOS;
        SettingsViewModel = new SettingsViewModel(preferencesService, screenWakeService, isMobile, loggerFactory);
        IsControlLogAvailable = eventModel.HasControlLog;

        IsTimingTabStripVisible = true;
        IsLiveTimingTabVisible = eventModel.IsLive;

        if (eventModel.IsLive)
        {
            IsLiveTimingTabSelected = true;
        }
        else
        {
            IsResultsTabSelected = true;
        }

        await Task.CompletedTask;
    }

    public bool HandleDeviceBackButton()
    {
        if (IsEventsListVisible)
        {
            return false; // There is nothing to go back to, so do not handle the back button
        }
        else if (IsDriverModeVisible)
        {
            // Driver mode selects no tab, so it has to be handled before the tab checks below -
            // otherwise every flag is false and back backgrounds the app instead of navigating.
            if (InCarSettingsViewModel is { IsPositionsVisible: true } positions)
            {
                positions.BackToSettings();
            }
            else
            {
                InCarSettingsViewModel?.Back();
            }

            return true;
        }
        else // The main tab strip is visible
        {
            if (IsLiveTimingTabSelected)
            {
                LiveTimingViewModel.Back();
            }
            else if (IsResultsTabSelected) // Session Results Tab
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
            else if (IsSettingsTabSelected) // Settings Tab
            {
                // Settings doesn't need a back action, so just return to events list
                WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(new RouterEvent { Path = "EventsList" }));
            }
            else
            {
                return false; // No tab is selected, so do not handle the back button
            }

            return true;
        }
    }

    #region Version Checking

    /// <summary>
    /// Performs version check before loading events list. Implements User Stories 1, 2, and 3.
    /// </summary>
    private async Task PerformVersionCheckAsync()
    {
        try
        {
            var platform = platformDetectionService.GetCurrentPlatform();

            // Check if version checking should be performed (skip for Desktop)
            if (!platformDetectionService.ShouldCheckVersion())
            {
                //Logger.LogInformation("Version check skipped for Desktop platform");
                platform = AppPlatform.iOS;
            }
            
            Logger.LogInformation("Performing version check for platform: {Platform}", platform);

            // Get version info from server with timeout
            var versionInfo = await versionCheckService.GetVersionInfoAsync(timeoutSeconds: VersionCheckTimeoutSeconds);

            // Graceful degradation when GetVersionInfoAsync returns null (timeout/error)
            if (versionInfo == null)
            {
                Logger.LogWarning("Version check timed out or failed - proceeding without version check");
                return;
            }

            // Get current app version and perform version check
            var currentVersion = versionCheckService.GetCurrentApplicationVersion();
            var result = versionCheckService.CheckVersion(currentVersion, versionInfo, platform);

            Logger.LogInformation("Version check result: {Requirement}, Current: {Current}, Latest: {Latest}, Minimum: {Minimum}",
                result.Requirement, result.CurrentVersion, result.LatestVersion, result.MinimumVersion);

            // Handle result based on requirement
            await HandleVersionCheckResultAsync(result);
        }
        catch (Exception ex)
        {
            // Error logging for version check failures
            Logger.LogError(ex, "Error during version check - proceeding without version check");
            // Gracefully degrade - allow app to continue
        }
    }
    
    /// <summary>
    /// Handles the version check result by displaying appropriate UI based on requirement.
    /// </summary>
    private async Task HandleVersionCheckResultAsync(VersionCheckResult result)
    {
        switch (result.Requirement)
        {
            case UpdateRequirement.Mandatory:
                // User Story 1: Block app access and show mandatory update dialog
                await ShowMandatoryUpdateDialogAsync(result);
                break;

            case UpdateRequirement.Optional:
                // User Story 2: Show dismissible notification and allow app usage
                await ShowOptionalUpdateNotificationAsync(result);
                break;

            case UpdateRequirement.None:
                // User Story 3: No UI shown, proceed directly to normal functionality
                Logger.LogInformation("App is up to date, proceeding normally");
                break;
        }
    }

    /// <summary>
    /// Shows a mandatory update dialog that blocks the user from proceeding. (User Story 1)
    /// </summary>
    private async Task ShowMandatoryUpdateDialogAsync(VersionCheckResult result)
    {
        Logger.LogWarning("Mandatory update required - blocking app access");
        
        // T021, T022, T023 - Set properties that MainView will bind to for overlay display
        MandatoryUpdateResult = result;
        IsMandatoryUpdateVisible = true;
        
        // T028 - Dialog cannot be dismissed until user takes action
        await Task.CompletedTask;
    }

    /// <summary>
    /// Shows an optional update notification that can be dismissed. (User Story 2)
    /// </summary>
    private async Task ShowOptionalUpdateNotificationAsync(VersionCheckResult result)
    {
        Logger.LogInformation("Optional update available - showing dismissible notification");
        
        // T029, T030, T031 - Show non-modal notification for optional updates
        OptionalUpdateNotification = result;
        IsUpdateNotificationVisible = true;
        
        // T034, T035 - Notification is dismissible and styled differently
        await Task.CompletedTask;
    }

    /// <summary>
    /// Dismisses the optional update notification
    /// </summary>
    [RelayCommand]
    public void DismissUpdateNotification()
    {
        IsUpdateNotificationVisible = false;
        OptionalUpdateNotification = null;
    }

    /// <summary>
    /// Launches the update URL for optional updates
    /// </summary>
    [RelayCommand]
    public void LaunchOptionalUpdate()
    {
        if (OptionalUpdateNotification?.ActionUrl != null)
        {
            try
            {
                WeakReferenceMessenger.Default.Send(new LauncherEvent(OptionalUpdateNotification.ActionUrl));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error launching update URL");
            }
        }
    }

    /// <summary>
    /// Launches the update URL for mandatory updates
    /// </summary>
    [RelayCommand]
    public void LaunchMandatoryUpdate()
    {
        if (MandatoryUpdateResult?.ActionUrl != null)
        {
            try
            {
                WeakReferenceMessenger.Default.Send(new LauncherEvent(MandatoryUpdateResult.ActionUrl));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error launching mandatory update URL");
            }
        }
    }

    #endregion

    /// <summary>
    /// Handles notifications related to size changes.
    /// </summary>
    public void Receive(SizeChangedNotification message)
    {
        //IsFlagsTabVisible = viewSizeService.CurrentSize.Width > FlagShowWidth;
        //IsControlLogTabVisible = viewSizeService.CurrentSize.Width > ControlLogShowWidth;
    }

    /// <summary>
    /// Another VM (e.g. In-Car driver settings) needs the access-code prompt.
    /// Honor it using this view model's overlay so there's a single UI surface.
    /// </summary>
    public void Receive(AccessCodeRequestNotification message)
    {
        var req = message.Value;
        Avalonia.Threading.Dispatcher.UIThread.PostSafe(() =>
            ShowAccessCodePrompt(req.EventModel, req.OrganizationName, req.OnSuccess, req.OnCancel), Logger);
    }

    /// <summary>
    /// A gated endpoint rejected the stored access code. Clear it and re-prompt
    /// if this notification matches the event we're currently viewing.
    /// </summary>
    public void Receive(EventAccessDeniedNotification message)
    {
        var eventId = message.Value;

        // Chiefly for the prompt's own validation probe, which is denied when the entered code is
        // wrong: that belongs to the open prompt, which reports it inline, and re-raising here would
        // replace the prompt mid-attempt and throw away what the viewer had typed. Not mutual
        // exclusion - the flag is only set once the posted job runs - so simultaneous denials from
        // the several requests an event screen has in flight can still each post a prompt.
        if (IsAccessCodePromptVisible)
            return;

        // Read once: EventsList navigation clears this from the dispatcher thread while denials
        // arrive on whichever thread the request completed on.
        var evt = currentEvent;
        if (evt != null && evt.EventId == eventId)
        {
            accessCodeStore.Clear(eventId);
            Avalonia.Threading.Dispatcher.UIThread.PostSafe(() => ShowAccessCodePrompt(evt, currentEventOrganizationName), Logger);
            return;
        }

        // Denied by the load that would have set currentEvent, so there is no event model to prompt
        // from - only what the events list knew. Replay the route once a code is accepted, which
        // runs the same load again with the code attached.
        if (pendingEventAccess is { } pending && pending.EventId == eventId)
        {
            accessCodeStore.Clear(eventId);
            Avalonia.Threading.Dispatcher.UIThread.PostSafe(
                () => ShowAccessCodePrompt(pending.EventId, pending.EventName, pending.OrganizationName,
                    onSuccess: () =>
                    {
                        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(pending.Route));
                        return Task.CompletedTask;
                    }),
                Logger);
        }
    }
}
