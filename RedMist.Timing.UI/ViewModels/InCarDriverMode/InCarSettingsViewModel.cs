using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.InCarDriverMode;

public partial class InCarSettingsViewModel : ObservableValidator, IDisposable
{
    private const string CAR_NUM_KEY = "DriverModeCarNumber";
    private const string IN_CLASS_KEY = "DriverModeIsInClassOnly";

    [Required]
    [StringLength(8, MinimumLength = 1)]
    [ObservableProperty]
    private string carNumber = string.Empty;

    public LargeObservableCollection<EventViewModel> Events { get; } = [];

    public EventViewModel? SelectedEvent { get; set; }

    [ObservableProperty]
    private bool isInClassOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string message = string.Empty;
    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    private bool isPositionsVisible = false;
    [ObservableProperty]
    private InCarPositionsViewModel inCarPositionsViewModel;

    private readonly EventClient eventClient;
    private readonly HubClient hubClient;
    private readonly EventAccessCodeStore accessCodeStore;
    private readonly IPreferencesService preferencesService;
    private readonly IScreenWakeService screenWakeService;
    private readonly ILoggerFactory loggerFactory;
    private ILogger Logger { get; }


    public InCarSettingsViewModel(EventClient eventClient, HubClient hubClient, EventAccessCodeStore accessCodeStore,
        IPreferencesService preferencesService, IScreenWakeService screenWakeService, ILoggerFactory loggerFactory)
    {
        this.eventClient = eventClient;
        this.hubClient = hubClient;
        this.accessCodeStore = accessCodeStore;
        this.preferencesService = preferencesService;
        this.screenWakeService = screenWakeService;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.loggerFactory = loggerFactory;
        inCarPositionsViewModel = new(hubClient, eventClient, loggerFactory);
    }


    public async Task Initialize()
    {
        // Called from a background task; hop to the UI thread so the bound property writes below
        // (IsLoading, Message, Events, and the settings loaded at the end) land on the right thread.
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(Initialize);
            return;
        }

        IsLoading = true;
        try
        {
            var events = await eventClient.LoadRecentEventsAsync();
            if (events != null)
            {
                if (events.Count == 0)
                {
                    Message = "No events found";
                }
                else
                {
                    // Order the live events at the top
                    var vms = new List<EventViewModel>();
                    foreach (var e in events.Where(e => e.IsLive).OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                    {
                        vms.Add(new EventViewModel(e, null));
                    }
                    foreach (var e in events.Where(e => !e.IsLive).OrderByDescending(e => DateTime.ParseExact(e.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                    {
                        vms.Add(new EventViewModel(e, null));
                    }

                    Events.SetRange(vms);
                }
            }
            else
            {
                Message = "No events found - null";
            }

            TryLoadSettings();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading events for driver mode");
            Message = $"Error loading events: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    public void BackToSettings()
    {
        IsPositionsVisible = false;
        ReleaseScreenWake();
        try
        {
            InCarPositionsViewModel.Unsubscribe();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error unsubscribing from driver mode updates");
        }
    }


    public void Ok()
    {
        ValidateAllProperties();
        if (HasErrors)
        {
            var errors = GetErrors();
            var sb = new StringBuilder();
            foreach (var e in errors)
            {
                sb.AppendLine(e.ErrorMessage);
            }
            Message = sb.ToString().TrimEnd();
            return;
        }

        Message = string.Empty;
        TrySaveSettings();

        var summary = SelectedEvent?.EventModel;
        var eventId = summary?.Id ?? 0;

        if (summary != null && summary.IsPrivate && string.IsNullOrEmpty(accessCodeStore.Get(eventId)))
        {
            // Use the shared prompt; once the code is set, fall through to start driver mode.
            var eventModel = new TimingCommon.Models.Event
            {
                EventId = eventId,
                EventName = summary.EventName,
                OrganizationName = summary.OrganizationName,
                IsPrivate = summary.IsPrivate,
                HideName = summary.HideName,
            };
            WeakReferenceMessenger.Default.Send(new AccessCodeRequestNotification(
                new AccessCodeRequest(
                    eventModel,
                    summary.OrganizationName,
                    onSuccess: () => { StartDriverMode(eventId); return Task.CompletedTask; },
                    onCancel: () => { /* user backed out; stay on settings */ })));
            return;
        }

        StartDriverMode(eventId);
    }

    private void StartDriverMode(int eventId)
    {
        IsPositionsVisible = true;

        // Release the outgoing view model's hub subscription before replacing it, otherwise it
        // stays alive on the singleton hub client and keeps reacting to reconnects.
        InCarPositionsViewModel.Dispose();

        InCarPositionsViewModel = new InCarPositionsViewModel(hubClient, eventClient, loggerFactory);
        InCarPositionsViewModel.Initialize(eventId, CarNumber, IsInClassOnly);

        // Drivers are looking at the screen without touching it.
        try
        {
            screenWakeService.SetKeepScreenOn(true);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to keep the screen awake for driver mode");
        }
    }

    /// <summary>
    /// Hands the screen wake lock back to whatever the user chose in settings.
    /// </summary>
    /// <remarks>
    /// The wake service is a single global flag shared with <see cref="SettingsViewModel"/>, so
    /// driver mode has to restore the user's preference rather than just switching it off.
    /// </remarks>
    private void ReleaseScreenWake()
    {
        try
        {
            screenWakeService.SetKeepScreenOn(preferencesService.Get(SettingsViewModel.KEEP_SCREEN_ON_KEY, false));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to restore the screen wake setting");
        }
    }

    /// <summary>
    /// Releases the hub subscription held by the current positions view model and the wake lock.
    /// </summary>
    public void Dispose()
    {
        InCarPositionsViewModel.Dispose();
        ReleaseScreenWake();
        GC.SuppressFinalize(this);
    }

    private void TryLoadSettings()
    {
        try
        {
            CarNumber = preferencesService.Get(CAR_NUM_KEY, string.Empty) ?? string.Empty;
            IsInClassOnly = preferencesService.Get(IN_CLASS_KEY, false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error loading driver mode settings");
        }
    }

    private void TrySaveSettings()
    {
        try
        {
            preferencesService.Set(CAR_NUM_KEY, CarNumber);
            preferencesService.Set(IN_CLASS_KEY, IsInClassOnly);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error saving driver mode settings");
        }
    }
}