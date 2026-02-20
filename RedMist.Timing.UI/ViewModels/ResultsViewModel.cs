using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Mappers;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class ResultsViewModel : ObservableObject, IRecipient<ValueChangedMessage<RouterEvent>>, IRecipient<AppResumeNotification>
{
    public ObservableCollection<SessionViewModel> Sessions { get; } = [];
    public bool HasNoSessions => Sessions.Count == 0;
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

    [ObservableProperty]
    private LiveTimingViewModel? liveTimingViewModel;
    [ObservableProperty]
    private bool isLiveTimingVisible;
    private readonly HubClient hubClient;
    private readonly EventClient eventClient;
    private readonly ILoggerFactory loggerFactory;
    private readonly ViewSizeService viewSizeService;
    private readonly EventContext eventContext;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;
    private readonly OrganizationIconCacheService iconCacheService;
    private readonly SponsorRotatorViewModel sponsorRotator;

    public Bitmap? OrganizationLogo
    {
        get
        {
            if (EventModel.OrganizationId > 0)
            {
                // Try to get from cache first
                var cached = iconCacheService.GetCachedIcon(EventModel.OrganizationId);
                if (cached != null)
                {
                    return cached;
                }
            }

            // Fallback to decoding byte array if not in cache
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


    public ResultsViewModel(Event eventModel, HubClient hubClient, EventClient eventClient, ILoggerFactory loggerFactory, ViewSizeService viewSizeService, EventContext eventContext, IHttpClientFactory httpClientFactory, IConfiguration configuration, OrganizationIconCacheService iconCacheService, SponsorRotatorViewModel sponsorRotator)
    {
        EventModel = eventModel;
        this.hubClient = hubClient;
        this.eventClient = eventClient;
        this.loggerFactory = loggerFactory;
        this.viewSizeService = viewSizeService;
        this.eventContext = eventContext;
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        this.iconCacheService = iconCacheService;
        this.sponsorRotator = sponsorRotator;
        WeakReferenceMessenger.Default.RegisterAll(this);

        InitializeSessions(eventModel.Sessions);

        // Load organization icon from cache or CDN
        if (EventModel.OrganizationId > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await iconCacheService.GetOrganizationIconAsync(EventModel.OrganizationId);
                    // Notify that the logo may have changed
                    Dispatcher.UIThread.InvokeOnUIThread(() => OnPropertyChanged(nameof(OrganizationLogo)));
                }
                catch (Exception)
                {
                    // Ignore errors loading icon
                }
            });
        }
    }


    private void InitializeSessions(Session[] sessions)
    {
        Sessions.Clear();
        foreach (var session in sessions.Where(s => s.EndTime.HasValue).OrderByDescending(s => s.StartTime))
        {
            Sessions.Add(new SessionViewModel(session));
        }
        OnPropertyChanged(nameof(HasNoSessions));
    }

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    public async void Receive(ValueChangedMessage<RouterEvent> message)
    {
        try
        {
            // Show live timing when session results are selected
            if (message.Value.Path == "SessionResults" && message.Value.Data is Session session)
            {
                SessionState? results = null;
                try
                {
                    eventContext.SetContext(EventModel.EventId, session.Id);
                    results = await eventClient.LoadSessionResultsAsync(session.EventId, session.Id);
                }
                catch //(Exception ex)
                {
                    //logger.LogError(ex, "Error loading session results");
                }

                LiveTimingViewModel = new LiveTimingViewModel(hubClient, eventClient, loggerFactory, viewSizeService, eventContext, httpClientFactory, configuration, iconCacheService, sponsorRotator) 
                { 
                    BackRouterPath = "SessionResultsList",
                    EventModel = EventModel,
                    IsRealTime = false,
                };

                if (results != null)
                {
                    var p = SessionStateMapper.ToPatch(results);
                    var statusNotification = new SessionStatusNotification(p);
                    Dispatcher.UIThread.InvokeOnUIThread(() => LiveTimingViewModel.ApplySessionUpdate(statusNotification));
                }
                IsLiveTimingVisible = true;
            }
            else if (message.Value.Path == "SessionResultsList")
            {
                RefreshSessions();
                IsLiveTimingVisible = false;
                LiveTimingViewModel = null;
                eventContext.ClearContext();
            }
            else if (message.Value.Path == "ResultsTab" && message.Value.Data is bool isResultsTabVisible && isResultsTabVisible)
            {
                RefreshSessions();
                IsLiveTimingVisible = false;
                LiveTimingViewModel = null;
                eventContext.ClearContext();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in router message handler for ResultsViewModel: {ex}");
        }
    }

    private void RefreshSessions()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var sessions = await eventClient.LoadSessionsAsync(EventModel.EventId);
                Dispatcher.UIThread.InvokeOnUIThread(() => InitializeSessions([.. sessions]), DispatcherPriority.Background);
            }
            catch //(Exception ex)
            {
                //logger.LogError(ex, "Error loading sessions");
            }
        });
    }

    /// <summary>
    /// Handle case where the app was in the background not getting updates and now becomes active again.
    /// </summary>
    public void Receive(AppResumeNotification message)
    {
        RefreshSessions();
    }
}
