using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels.InCarDriverMode;
using RedMist.TimingCommon.Models;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public enum TabTypes { LiveTiming, Results, ControlLog, EventInformation }

public partial class MainViewModel : ObservableObject, IRecipient<ValueChangedMessage<RouterEvent>>, 
    IRecipient<SizeChangedNotification>
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
    private readonly IPlatformDetectionService platformDetectionService;
    private readonly IVersionCheckService versionCheckService;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;
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
                            System.Diagnostics.Debug.WriteLine($"Error initializing control log: {ex}");
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
                            System.Diagnostics.Debug.WriteLine($"Error unsubscribing from control logs: {ex}");
                        }
                    });
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


    public MainViewModel(EventsListViewModel eventsListViewModel, LiveTimingViewModel liveTimingViewModel, HubClient hubClient,
        EventClient eventClient, ILoggerFactory loggerFactory, ViewSizeService viewSizeService, EventContext eventContext,
        IPlatformDetectionService platformDetectionService, IVersionCheckService versionCheckService, IHttpClientFactory httpClientFactory, IConfiguration configuration)
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

    public async void Receive(ValueChangedMessage<RouterEvent> message)
    {
        try
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
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await LiveTimingViewModel.InitializeLiveAsync(eventModel);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error initializing live timing: {ex}");
                                // If you have access to a logger, use it instead:
                                // Logger?.LogError(ex, "Error initializing live timing");
                            }
                        });
                    }

                    ResultsViewModel = new ResultsViewModel(eventModel, hubClient, eventClient, loggerFactory, viewSizeService, eventContext, httpClientFactory, configuration);
                    EventInformationViewModel = new EventInformationViewModel(eventModel);
                    ControlLogViewModel = new ControlLogViewModel(eventModel, hubClient, eventClient, eventContext);
                    FlagsViewModel = new FlagsViewModel(eventModel, eventClient, eventContext, httpClientFactory, configuration);
                    IsControlLogTabVisible = eventModel.HasControlLog && eventModel.IsLive;

                    IsTimingTabStripVisible = true;
                    IsLiveTimingTabVisible = eventModel.IsLive;

                    // Ensure at least one tab is selected when tab strip becomes visible
                    if (eventModel.IsLive)
                    {
                        IsLiveTimingTabSelected = true;
                    }
                    else
                    {
                        IsResultsTabSelected = true;
                    }
                }
            }
            else if (router.Path == "EventsList")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await EventsListViewModel.Initialize();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error initializing events list: {ex}");
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
                        System.Diagnostics.Debug.WriteLine($"Error unsubscribing from live timing: {ex}");
                    }
                });
                
                try
                {
                    _ = ControlLogViewModel?.UnsubscribeFromControlLogs();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error unsubscribing from control logs: {ex}");
                }

                IsEventsListVisible = true;

                IsTimingTabStripVisible = false;
                IsDriverModeVisible = false;
            }
            else if (router.Path == "InCarDriverSettings")
            {
                InCarSettingsViewModel = new InCarSettingsViewModel(eventClient, hubClient);
                
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await InCarSettingsViewModel.Initialize();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error initializing in-car settings: {ex}");
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in router message handler: {ex}");
            // If you have access to a logger, use it instead:
            // Logger?.LogError(ex, "Error handling router message");
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

    #region Version Checking

    /// <summary>
    /// Performs version check before loading events list. Implements User Stories 1, 2, and 3.
    /// </summary>
    private async Task PerformVersionCheckAsync()
    {
        try
        {
            //// Check if version checking should be performed (skip for Desktop)
            //if (!platformDetectionService.ShouldCheckVersion())
            //{
            //    Logger.LogInformation("Version check skipped for Desktop platform");
            //    return;
            //}

            var platform = platformDetectionService.GetCurrentPlatform();
            platform = AppPlatform.iOS;
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
        IsFlagsTabVisible = viewSizeService.CurrentSize.Width > FlagShowWidth;
    }
}
