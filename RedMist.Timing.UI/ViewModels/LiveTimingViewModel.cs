using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Utilities;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.Mappers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class LiveTimingViewModel : ObservableObject, IRecipient<SizeChangedNotification>,
    IRecipient<AppResumeNotification>, IRecipient<SessionStatusNotification>,
    IRecipient<CarStatusNotification>, IRecipient<ResetNotification>,
    IRecipient<HubResubscribedNotification>, IDisposable
{
    private SessionState? sessionStatus;

    // Flat collection for the view
    public ObservableCollection<CarViewModel> Cars { get; } = [];
    // Grouped by class collection for the view
    public ObservableCollection<GroupHeaderViewModel> GroupedCars { get; } = [];
    protected readonly SourceCache<CarViewModel, string> carCache = new(car => car.Number);

    /// <summary>
    /// Indicates whether the timing data is being shown in real-time mode or historical results.
    /// </summary>
    public bool IsRealTime { get; set; } = true;

    private readonly HubClient hubClient;
    private readonly EventClient serverClient;
    private readonly ViewSizeService viewSizeService;
    private readonly EventContext eventContext;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;
    private readonly SessionState lastSessionState = new();
    private Dictionary<string, string> classColors = [];
    private Dictionary<string, string> classOrder = [];
    private Dictionary<string, SolidColorBrush> classColorBrushCache = [];
    private readonly InMemoryLogProvider? logProvider;
    private readonly OrganizationIconCacheService iconCacheService;
    private readonly ILoggerFactory loggerFactory;

    public SponsorRotatorViewModel SponsorRotator { get; }

    private ILogger Logger { get; }

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrganizationLogo))]
    [NotifyPropertyChangedFor(nameof(IsBroadcastVisible))]
    [NotifyPropertyChangedFor(nameof(BroadcastCompanyName))]
    [NotifyPropertyChangedFor(nameof(IsControlLogAvailable))]
    private Event eventModel = new();

    [ObservableProperty]
    private string sessionName = string.Empty;

    [ObservableProperty]
    private string flag = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTimeToGo))]
    private string timeToGo = string.Empty;
    public bool ShowTimeToGo => !string.IsNullOrWhiteSpace(TimeToGo) && TimeToGo != "00:00:00";

    [ObservableProperty]
    private string localTime = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRaceTime))]
    private string raceTime = string.Empty;
    public bool ShowRaceTime => !string.IsNullOrWhiteSpace(RaceTime) && RaceTime != "00:00:00";

    [ObservableProperty]
    private string totalLaps = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlat))]
    [NotifyPropertyChangedFor(nameof(GroupToggleText))]
    private GroupMode currentGrouping = GroupMode.Overall;
    public string GroupToggleText
    {
        get
        {
            if (CurrentGrouping == GroupMode.Overall)
                return "Overall";
            return "By Class";
        }
    }
    public bool IsFlat => CurrentGrouping == GroupMode.Overall;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortToggleText))]
    private SortMode currentSortMode = SortMode.Position;
    /// <summary>
    /// The gap and difference columns are sourced from the fast-time fields when sorting by
    /// fastest, so every row has to be told which sort is in effect.
    /// </summary>
    partial void OnCurrentSortModeChanged(SortMode value)
    {
        foreach (var car in carCache.Items)
        {
            car.CurrentSortMode = value;
        }
    }
    public string SortToggleText
    {
        get
        {
            if (CurrentSortMode == SortMode.Position)
                return "By Position";
            return "By Fastest";
        }
    }
    private bool? lastIsQualifying = false;

    //private IDisposable? consistencyCheckInterval;
    private IDisposable? fullUpdateInterval;

    /// <summary>Set while a full refresh is in flight, so ticks cannot stack them up.</summary>
    /// <remarks>
    /// The refresh is started from a timer and not awaited, and it retries twice with a growing
    /// delay, so on a bad connection one can outlive the interval that started it. Without this,
    /// the worse the network got the more concurrent requests the app made of it - which is the
    /// shape of the traffic that spent the server's rate limit during a live event.
    /// </remarks>
    private int refreshInFlight;

    /// <summary>Set when a caller was turned away by <see cref="refreshInFlight"/>.</summary>
    /// <remarks>
    /// Some callers are not ticks and have no second chance: an app resume, a session reset, and a
    /// hub resubscribe each know the screen is stale and nothing else will notice. Turning one of
    /// those away silently would trade the old stacking problem for a frozen grid, so the request is
    /// remembered and served when the refresh holding the guard finishes.
    ///
    /// It is also read by <see cref="ShouldRefreshNow"/>, which closes the gap between the last time
    /// the loop below looks at this and the moment it releases the guard: a request landing in there
    /// is not lost, only deferred to the next tick.
    /// </remarks>
    private int resyncOwed;

    /// <summary>
    /// When a whole session state was last applied, in UTC ticks. See <see cref="LivePollingPolicy"/>.
    /// </summary>
    /// <remarks>
    /// Written from whichever thread ran the refresh and read from the timer, and a DateTime is
    /// wider than a 32-bit ARM head reads atomically, so it is kept as a long behind Interlocked -
    /// the same shape HubClient keeps its own clock in.
    ///
    /// Zero means no whole state has been applied to what is on screen yet, which is the state this
    /// returns to for every event: the view model is a singleton reused for all of them, so leaving
    /// the previous event's success in place would tell the gate a grid it has never filled was
    /// recently in sync.
    /// </remarks>
    private long lastFullRefreshTicks;
    /// <summary>
    /// Owns the lifetime of the rows in <see cref="carCache"/>. See where it is assigned.
    /// </summary>
    private readonly IDisposable carLifetime;
    /// <summary>Projects the cache into <see cref="Cars"/>.</summary>
    private readonly IDisposable flatProjection;
    /// <summary>Projects the cache into <see cref="GroupedCars"/>.</summary>
    private readonly IDisposable groupedProjection;
    private bool disposed;

    public string BackRouterPath { get; set; } = "EventsList";

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
    [NotifyPropertyChangedFor(nameof(IsBroadcastVisible))]
    [NotifyPropertyChangedFor(nameof(IsControlLogAvailable))]
    private bool isLive = false;

    public bool IsBroadcastVisible => IsLive && EventModel.Broadcast != null && !string.IsNullOrEmpty(EventModel.Broadcast.Url);
    public string? BroadcastCompanyName => EventModel.Broadcast?.CompanyName;
    public bool IsControlLogAvailable => EventModel.HasControlLog;

    //private int consistencyCheckFailures;
    //private DateTime? lastConsistencyCheckReset;

    private readonly PitTracking pitTracking = new();

    [ObservableProperty]
    private bool showPenaltyColumn = false;
    public const int PenaltyColumnWidth = 470;

    [ObservableProperty]
    private bool allowEventList = true;

    [ObservableProperty]
    private string logMessages = string.Empty;

    [ObservableProperty]
    private bool showLogDisplay = false;

    [ObservableProperty]
    private bool isSearchVisible = false;

    [ObservableProperty]
    private bool isLegendVisible = false;

    // Through the cache, not decoded inline: these getters are read on every binding evaluation,
    // and the legend's bindings are live from the moment the view is realized - IsLegendVisible
    // only hides it. Each read used to decode a fresh bitmap.
    public IImage? SentinelLegendImage => AssetImageCache.GetThemed(CarViewModel.SENTINEL_IMAGE);
    public IImage? MrlLegendImage => AssetImageCache.GetThemed(CarViewModel.MRL_IMAGE);

    [ObservableProperty]
    private string searchText = string.Empty;

    private Func<CarViewModel, bool> searchFilter = _ => true;
    private readonly BehaviorSubject<Func<CarViewModel, bool>> searchFilterSubject = new(_ => true);

    private IDisposable? searchDebounce;

    private int logRefreshPending;
    private int logoClickCount = 0;
    private DateTime lastLogoClickTime = DateTime.MinValue;


    public LiveTimingViewModel(HubClient hubClient, EventClient serverClient, ILoggerFactory loggerFactory, 
        ViewSizeService viewSizeService, EventContext eventContext, IHttpClientFactory httpClientFactory, 
        IConfiguration configuration, OrganizationIconCacheService iconCacheService, 
        SponsorRotatorViewModel sponsorRotator,
        InMemoryLogProvider? logProvider = null)
    {
        this.hubClient = hubClient;
        this.serverClient = serverClient;
        this.loggerFactory = loggerFactory;
        this.viewSizeService = viewSizeService;
        this.eventContext = eventContext;
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        this.logProvider = logProvider;
        this.iconCacheService = iconCacheService;
        SponsorRotator = sponsorRotator;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);

        // Subscribe to log events
        if (logProvider != null)
        {
            logProvider.LogAdded += OnLogAdded;
        }
        // Flat
        flatProjection = carCache.Connect()
            .Filter(searchFilterSubject)
            .AutoRefresh(t => t.OverallPosition)
            .AutoRefresh(t => t.SortablePosition)
            .AutoRefresh(t => t.BestTime)
            .SortAndBind(Cars, SortExpressionComparer<CarViewModel>.Ascending(t => t.SortablePosition))
            .Subscribe();

        // Grouped by class
        groupedProjection = carCache.Connect()
            .Filter(searchFilterSubject)
            .GroupOnProperty(c => c.Class)
            .Transform(g => new GroupHeaderViewModel(g.Key, GetClassColor(g.Key), g.Cache), true)
            .SortAndBind(GroupedCars, Comparer<GroupHeaderViewModel>.Create((a, b) =>
            {
                var orderA = GetClassOrder(a.Name);
                var orderB = GetClassOrder(b.Name);

                // If both have defined orders, sort by order
                if (orderA < int.MaxValue - 1 && orderB < int.MaxValue - 1)
                    return orderA.CompareTo(orderB);

                // If both have no defined order, sort alphabetically
                if (orderA == int.MaxValue - 1 && orderB == int.MaxValue - 1)
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

                // If A has defined order and B doesn't, A comes first
                if (orderA < int.MaxValue - 1)
                    return -1;

                // If B has defined order and A doesn't, B comes first
                if (orderB < int.MaxValue - 1)
                    return 1;

                // Both are null/empty (int.MaxValue), maintain order
                return orderA.CompareTo(orderB);
            }))
            .DisposeMany()
            .Subscribe();

        // Car lifetime, kept separate from the two projections above. DisposeMany disposes whatever
        // the stream it is attached to drops, and a filtered or grouped stream drops a car that is
        // merely out of view: hung off the flat projection, a search matching three cars would have
        // disposed the rest of the field, and clearing the search would have handed the same dead
        // instances straight back - the cache still holds them. Attached to the unfiltered cache it
        // fires only on a real removal, which is a car leaving the entry list or the event being
        // reset, and on teardown of this subscription.
        //
        // Subscribed last on purpose. DynamicData notifies in subscription order, so the two
        // projections above have already taken the row out of Cars and out of its group by the time
        // this runs. Disposing a row that is still bound would tear its details view model - the
        // chart, and the control log grid - out from under a realized row, part way through
        // delivering the change set that removes it.
        carLifetime = carCache.Connect()
            .DisposeMany()
            .Subscribe();
    }


    public async Task InitializeLiveAsync(Event eventModel)
    {
        try
        {
            // Callers invoke this from a background task, so every write to bound state has to be
            // marshalled. ResetEvent in particular clears carCache, which SortAndBind projects
            // straight into the Cars/GroupedCars collections the ItemsControls are bound to -
            // mutating those off the UI thread desyncs the controls from their source.
            await Dispatcher.UIThread.InvokeOnUIThreadAsync(() =>
            {
                IsLoading = true;
                EventModel = eventModel;
                Flag = string.Empty;
                pitTracking.Clear();

                // Initialize ShowPenaltyColumn based on event control log availability and current viewport size
                ShowPenaltyColumn = IsControlLogAvailable && viewSizeService.CurrentSize.Width > PenaltyColumnWidth;

                Logger.LogInformation("ResetEvent...");
                ResetEvent();
            });

            // ResetEvent has just emptied the grid, so nothing on screen has been filled from a
            // whole state - whatever the previous event on this singleton managed. Said here rather
            // than only on success below, because the load that follows may fail: the gate would
            // then read the last event's timestamp, decide this screen was recently in sync, and
            // leave it empty for the length of the refresh floor while the hub delivered deltas for
            // rows that were never created.
            MarkNotYetRefreshed();

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
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Error refreshing the organization logo for {OrganizationId}", EventModel.OrganizationId);
                    }
                });
            }

            // Start sponsor rotation
            _ = SponsorRotator.StartAsync(EventModel.EventId.ToString());

            try
            {
                Logger.LogInformation("ResetState...");
                await Task.Run(() => RefreshStatusAsync(required: true));
                Logger.LogInformation("Subscribe...");
                await Task.Run(() => hubClient.SubscribeToEventAsync(EventModel.EventId));
                Logger.LogInformation("Completed subscribe...");
                Dispatcher.UIThread.InvokeOnUIThread(() => IsLive = true);
            }
            catch (Exception ex)
            {
                Logger.LogInformation("Subscribe Error." + ex.ToString());
                Logger.LogError(ex, $"Error subscribing to event: {ex.Message}");
            }
            finally
            {
                Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = false, DispatcherPriority.Background);
            }
            //if (consistencyCheckInterval != null)
            //{
            //    try
            //    {
            //        consistencyCheckInterval.Dispose();
            //    }
            //    catch { }
            //    consistencyCheckInterval = null;
            //}
            //consistencyCheckInterval = Observable.Interval(TimeSpan.FromSeconds(3)).Subscribe(_ => RunConsistencyCheck());

            SetFullUpdateInterval(Observable.Interval(TimeSpan.FromSeconds(5)).Subscribe(tick =>
            {
                try
                {
                    // The tick still runs every five seconds; what it does with it now depends on
                    // whether the hub is already delivering. See LivePollingPolicy.
                    if (ShouldRefreshNow())
                    {
                        _ = RefreshStatusAsync();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in periodic refresh timer");
                }
            }));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing live timing.");
        }
    }

    /// <summary>
    /// Whether this tick needs to fetch a whole session state.
    /// </summary>
    /// <remarks>
    /// The hub's clock is read from the client rather than kept here on purpose: a refresh applies
    /// its result through the same recipients the hub feeds, so a timestamp taken at this end would
    /// be reset by the poll itself and the screen could never decide it had stopped needing one.
    /// </remarks>
    internal bool ShouldRefreshNow()
    {
        // A request that was turned away by the guard outranks the policy: something asked for a
        // whole state and has not had one.
        if (Volatile.Read(ref resyncOwed) == 1)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        var sinceHubMessage = hubClient.LastEventMessageUtc is { } last ? now - last : TimeSpan.MaxValue;
        var lastRefresh = Interlocked.Read(ref lastFullRefreshTicks);
        var sinceFullRefresh = lastRefresh == 0 ? TimeSpan.MaxValue : now - new DateTime(lastRefresh, DateTimeKind.Utc);

        return LivePollingPolicy.ShouldRefresh(hubClient.IsConnected, sinceHubMessage, sinceFullRefresh);
    }

    /// <summary>Returns the screen to "no whole state has been applied yet".</summary>
    private void MarkNotYetRefreshed() => Interlocked.Exchange(ref lastFullRefreshTicks, 0);

    /// <summary>
    /// Fetches a whole session state and applies it.
    /// </summary>
    /// <param name="required">
    /// True for a caller that knows the screen is stale and has no second chance - an app resume, a
    /// session reset, a hub resubscribe. If the guard turns one of those away the request is
    /// remembered and served when the refresh holding it finishes; a periodic tick is simply
    /// dropped, since another is along in five seconds and the refresh it lost to answers the same
    /// question anyway.
    /// </param>
    public async Task RefreshStatusAsync(bool required = false)
    {
        // One at a time. A refresh retries twice on failure, so on a bad connection it can outlive
        // the tick that started it, and the ticks do not wait for each other.
        if (Interlocked.Exchange(ref refreshInFlight, 1) == 1)
        {
            if (required)
            {
                Interlocked.Exchange(ref resyncOwed, 1);
            }
            return;
        }

        try
        {
            do
            {
                await RefreshOnceAsync();
            }
            while (Interlocked.Exchange(ref resyncOwed, 0) == 1);
        }
        finally
        {
            Interlocked.Exchange(ref refreshInFlight, 0);
        }
    }

    private async Task RefreshOnceAsync()
    {
        try
        {
            var sw = Stopwatch.StartNew();

            sessionStatus = await serverClient.ExecuteWithRetryAsync(
                () => serverClient.LoadEventStatusAsync(EventModel.EventId),
                nameof(serverClient.LoadEventStatusAsync));

            if (sessionStatus == null)
            {
                Logger.LogWarning("Session status was given up on for event {EventId}", EventModel.EventId);
                return;
            }

            var patch = SessionStateMapper.CreatePatch(new SessionState(), sessionStatus);
            Receive(new SessionStatusNotification(patch));

            var carPatches = sessionStatus.CarPositions.Select(c => ToFullPatch(CarPositionMapper.CreatePatch(new CarPosition(), c))).ToArray();
            Receive(new CarStatusNotification(carPatches));

            // Stamped only on success, so a failed attempt does not count as the screen having been
            // resynced and leave it running on deltas for another five minutes.
            Interlocked.Exchange(ref lastFullRefreshTicks, DateTime.UtcNow.Ticks);
            Logger.LogInformation("Full update in {t}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Error refreshing status: {ex.Message}");
        }
    }

    public async Task UnsubscribeLiveAsync()
    {
        SponsorRotator.Stop();

        // Leaving the event through a tab's back button routes here without going through Back(),
        // so the periodic refresh has to be torn down here too or it keeps polling the old event.
        StopFullUpdateInterval();

        try
        {
            await hubClient.UnsubscribeFromEventAsync(EventModel.EventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Error unsubscribing event: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the grid and everything in it.
    /// </summary>
    /// <remarks>
    /// Written for the instances <see cref="ResultsViewModel"/> builds, one per session opened, and
    /// which it used to simply drop. Nothing released those, so every car in a session's results -
    /// and the details view model behind any row that had been expanded, including its control log
    /// subscription on the hub - stayed alive for the rest of the session. Making the row disposable
    /// did not reach them, because the thing that disposes rows is the cache subscription here, and
    /// nothing disposed that either.
    ///
    /// The instance registered with the container is a singleton and lives as long as the app, so in
    /// practice this runs for the per-session ones.
    ///
    /// Order matters. Inbound work is stopped first, so nothing arrives to be applied to a grid
    /// that is being taken apart. The projections then go before <see cref="carLifetime"/>, for the
    /// same reason that one is subscribed last: disposing a row still bound into
    /// <see cref="Cars"/> would tear its chart and control log grid out from under a realized row.
    ///
    /// <see cref="SponsorRotator"/> is deliberately untouched. It is a container singleton shared
    /// with the other view models, so stopping or disposing it here would reach into whichever
    /// event is on screen.
    /// </remarks>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;

        // Stop anything that could still push an update in.
        WeakReferenceMessenger.Default.UnregisterAll(this);
        if (logProvider != null)
        {
            logProvider.LogAdded -= OnLogAdded;
        }
        StopFullUpdateInterval();
        searchDebounce?.Dispose();

        // Unbind before releasing the rows.
        flatProjection.Dispose();
        groupedProjection.Dispose();

        // Disposes every row still in the cache, which is what releases any open details.
        carLifetime.Dispose();

        carCache.Dispose();
        searchFilterSubject.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tears down the periodic full-refresh subscription, if one is running.
    /// </summary>
    /// <remarks>
    /// Interlocked because the field is written from more than one thread: leaving an event runs
    /// UnsubscribeLiveAsync on one background task while opening the next runs InitializeLiveAsync
    /// on another. A lost update there would orphan a live 5-second poller with no handle to stop it.
    /// </remarks>
    private void StopFullUpdateInterval()
    {
        try
        {
            Interlocked.Exchange(ref fullUpdateInterval, null)?.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Error disposing the periodic refresh subscription");
        }
    }

    /// <summary>
    /// Installs the periodic full-refresh subscription, disposing whatever it replaces.
    /// </summary>
    private void SetFullUpdateInterval(IDisposable subscription)
        => Interlocked.Exchange(ref fullUpdateInterval, subscription)?.Dispose();

    /// <summary>
    /// Handles notifications related to size changes.
    /// </summary>
    public void Receive(SizeChangedNotification message)
    {
        ShowPenaltyColumn = IsControlLogAvailable && viewSizeService.CurrentSize.Width > PenaltyColumnWidth;
        Logger.LogInformation("Size changed: {Width}x{Height}", message.Size.Width, message.Size.Height);
    }

    /// <summary>
    /// Handle case where the app was in the background not getting updates and now becomes active again.
    /// </summary>
    public void Receive(AppResumeNotification message)
    {
        if (!IsRealTime)
            return;
        Dispatcher.UIThread.InvokeOnUIThread(() =>
        {
            IsLoading = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshStatusAsync(required: true);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error refreshing status on app resume");
                }
                finally
                {
                    Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = false);
                }
            });
        });
        return;
    }

    /// <summary>
    /// Resyncs after the hub subscription is restored on a new connection.
    /// </summary>
    /// <remarks>
    /// Unconditional, and the one thing that makes gating the periodic refresh safe. The delta
    /// stream resumes with a gap behind it and the server sends no state on subscribe, so at this
    /// moment the hub looks perfectly healthy while the grid is quietly wrong. Left to the gate,
    /// nothing would ever ask for the state that repairs it.
    /// </remarks>
    public void Receive(HubResubscribedNotification message)
    {
        if (!IsRealTime || message.EventId != EventModel.EventId)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshStatusAsync(required: true);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error refreshing status after the hub resubscribed");
            }
        });
    }

    public void Receive(SessionStatusNotification message)
    {
        if (!IsRealTime)
            return;

        Dispatcher.UIThread.InvokeOnUIThread(() => ApplySessionUpdate(message), DispatcherPriority.Normal);
    }

    public void ApplySessionUpdate(SessionStatusNotification message)
    {
        try
        {
            if (message.Value.SessionName != null)
                SessionName = message.Value.SessionName;
            if (message.Value.CurrentFlag != null)
                Flag = message.Value.CurrentFlag.ToString() ?? string.Empty;
            if (message.Value.TimeToGo != null)
                TimeToGo = message.Value.TimeToGo;
            if (message.Value.RunningRaceTime != null)
            {
                RaceTime = message.Value.RunningRaceTime;
                Dispatcher.UIThread.PostSafe(UpdateLapProgress, Logger, DispatcherPriority.Background);
            }

            if (message.Value.LocalTimeOfDay != null &&
                DateTime.TryParseExact(message.Value.LocalTimeOfDay, "HH:mm:ss", null, DateTimeStyles.None, out var tod))
            {
                LocalTime = tod.ToString("h:mm:ss tt");
            }

            if (message.Value.IsPracticeQualifying != null)
            {
                if (lastIsQualifying == null || lastIsQualifying != message.Value.IsPracticeQualifying)
                {
                    // Only update the sort if it has changed to avoid overriding the user
                    if (message.Value.IsPracticeQualifying.Value && CurrentSortMode != SortMode.Fastest)
                    {
                        ToggleSortMode();
                    }
                    else if (!message.Value.IsPracticeQualifying.Value && CurrentSortMode != SortMode.Position)
                    {
                        ToggleSortMode();
                    }
                    lastIsQualifying = message.Value.IsPracticeQualifying;
                }
            }

            // Update event entries
            if (message.Value.EventEntries != null)
            {
                var newClassColors = message.Value.ClassColors ?? lastSessionState.ClassColors;
                classOrder = message.Value.ClassOrder ?? lastSessionState.ClassOrder;
                if (newClassColors != classColors)
                {
                    classColors = newClassColors;
                    classColorBrushCache = [];
                }
                ApplyEntries(message.Value.EventEntries, isDeltaUpdate: false);
            }

            // Update car status
            if (message.Value.CarPositions != null)
            {
                var patches = message.Value.CarPositions
                    .Where(c => c.Number != null)
                    .Select(c => ToFullPatch(CarPositionMapper.ToPatch(c)))
                    .ToArray();
                ApplyCarUpdate(new CarStatusNotification(patches));
            }

            SessionStateMapper.ApplyPatch(message.Value, lastSessionState);
            eventContext.SetContext(lastSessionState.EventId, lastSessionState.SessionId);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error applying session update: {Message}", e.Message);
        }
    }

    public void Receive(CarStatusNotification message)
    {
        if (!IsRealTime)
            return;

        Dispatcher.UIThread.InvokeOnUIThread(() => ApplyCarUpdate(message), DispatcherPriority.Normal);
    }

    private void ApplyCarUpdate(CarStatusNotification message)
    {
        // Apply car position updates
        UpdateCars(message.Value);

        // The measured track position arrives with the car patches, so the bars have to be redrawn
        // here as well as on the race clock rather than waiting for the next clock tick.
        UpdateLapProgress();

        if (carCache.Count > 0)
        {
            TotalLaps = carCache.Items.Max(c => c.LastLap).ToString();
        }
        else
        {
            TotalLaps = string.Empty;
        }
    }

    public void Receive(ResetNotification message)
    {
        if (!IsRealTime)
            return;

        Logger.LogInformation("*** RESET EVENT RECEIVED ***");
        Dispatcher.UIThread.InvokeOnUIThread(() =>
        {
            ResetEvent();
            // Same reason as in InitializeLiveAsync: the grid is empty again, and if the refresh
            // below fails the gate must not believe it is still in sync.
            MarkNotYetRefreshed();
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshStatusAsync(required: true);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error refreshing status after reset");
                }
            });
        });

        return;
    }

    private void ApplyEntries(List<EventEntry> entries, bool isDeltaUpdate = false)
    {
        foreach (var entry in entries)
        {
            var classColor = GetClassColor(entry.Class);
            var carVm = carCache.Lookup(entry.Number);
            if (!carVm.HasValue && !isDeltaUpdate)
            {
                var vm = new CarViewModel(EventModel, serverClient, hubClient, pitTracking, viewSizeService, httpClientFactory, configuration, loggerFactory) { CurrentGroupMode = CurrentGrouping, CurrentSortMode = this.CurrentSortMode };
                vm.ApplyEntry(entry, classColor);
                carCache.AddOrUpdate(vm);

                if (CurrentSortMode == SortMode.Fastest)
                {
                    UpdatePositionsByFastestTime();
                }
            }
            else if (carVm.HasValue)
            {
                carVm.Value.ApplyEntry(entry, classColor);
            }
        }

        if (!isDeltaUpdate)
        {
            // Remove cars that are no longer entered. The ToArray below materializes the matches
            // before any removal; SourceCache.Keys already hands back a snapshot array, so this is
            // belt and braces against a future change rather than a fix for a live failure.
            var entryNumbers = new HashSet<string>(entries.Select(e => e.Number));
            var staleNumbers = carCache.Keys.Where(num => !entryNumbers.Contains(num)).ToArray();
            foreach (var num in staleNumbers)
            {
                carCache.RemoveKey(num);
            }
        }
    }

    private SolidColorBrush GetClassColor(string @class)
    {
        if (string.IsNullOrEmpty(@class))
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        if (classColorBrushCache.TryGetValue(@class, out var cached))
        {
            return cached;
        }

        SolidColorBrush brush;
        if (classColors.TryGetValue(@class, out var classColorHex))
        {
            brush = Color.TryParse(classColorHex, out Color color) ? new SolidColorBrush(color) : new SolidColorBrush(Colors.Gray);
        }
        else
        {
            brush = new SolidColorBrush(Colors.Gray);
        }

        classColorBrushCache[@class] = brush;
        return brush;
    }

    /// <summary>
    /// This uses the class order dictionary to get the sort order for a class.
    /// </summary>
    private int GetClassOrder(string @class)
    {
        if (string.IsNullOrEmpty(@class))
        {
            return int.MaxValue;
        }

        if (classOrder.TryGetValue(@class, out var orderStr) && int.TryParse(orderStr, out var order))
        {
            return order;
        }

        return int.MaxValue - 1;
    }

    private void UpdateCars(CarPositionPatch[] carUpdates)
    {
        bool anyBestLapTimeChanged = false;
        bool isFastestSort = CurrentSortMode == SortMode.Fastest;

        foreach (var carUpdate in carUpdates)
        {
            if (carUpdate.Number == null)
                continue;

            var carVm = carCache.Lookup(carUpdate.Number);
            if (carVm.HasValue)
            {
                int lastBestLapTime = 0;
                if (isFastestSort)
                {
                    lastBestLapTime = carVm.Value.BestTimeMs;
                }

                // Update the car data
                carVm.Value.ApplyPatch(carUpdate);

                if (!anyBestLapTimeChanged && isFastestSort && lastBestLapTime != carVm.Value.BestTimeMs)
                {
                    anyBestLapTimeChanged = true;
                }
            }
        }

        if (anyBestLapTimeChanged)
        {
            UpdatePositionsByFastestTime();
        }
        else if (!isFastestSort)
        {
            // Reset position override
            ResetPositionOverrides();
        }
    }

    /// <summary>
    /// Adjusts a patch built from a complete car state so that absent fields read as "there is none"
    /// rather than "no change".
    /// </summary>
    /// <remarks>
    /// A patch carries "no change" as null, which a full state has no way to mean - everything it
    /// leaves out, it genuinely does not have. That only matters for the measured track position,
    /// where the difference is visible: the server normally says "no usable measurement" with the
    /// <see cref="CarPosition.InvalidTrackPosition"/> sentinel, but a timing system that stops
    /// reporting the field altogether would otherwise leave the last measurement on screen forever.
    /// </remarks>
    private static CarPositionPatch ToFullPatch(CarPositionPatch patch)
    {
        patch.LapPositionPercent ??= CarPosition.InvalidTrackPosition;
        return patch;
    }

    private void UpdateLapProgress()
    {
        var raceTime = ParseRMTime(RaceTime);
        foreach (var carVm in carCache.Items)
        {
            carVm.UpdateLapProgress(raceTime);
        }
    }

    private void UpdatePositionsByFastestTime()
    {
        // Sort the cars by fastest time
        if (CurrentGrouping == GroupMode.Overall)
        {
            var sortedCars = carCache.Items.OrderBy(c => c.BestTimeMs).ToArray();
            for (int i = 0; i < sortedCars.Length; i++)
            {
                sortedCars[i].OverridePosition(i + 1);
            }
        }
        else if (CurrentGrouping == GroupMode.Class)
        {
            // Sort the cars by class and then by fastest time
            foreach (var group in carCache.Items.GroupBy(c => c.Class))
            {
                var sortedGroup = group.OrderBy(c => c.BestTimeMs).ToArray();
                for (int i = 0; i < sortedGroup.Length; i++)
                {
                    sortedGroup[i].OverridePosition(i + 1);
                }
            }
        }
    }

    private void ResetPositionOverrides()
    {
        foreach (var car in carCache.Items)
        {
            car.OverridePosition(null);
        }
    }

    private void ResetEvent()
    {
        // Allow for reset when the event is initializing. Once it has started,
        // suppress the resets to reduce user confusion
        if (string.IsNullOrWhiteSpace(Flag))
        {
            carCache.Clear();
            pitTracking.Clear();
            SessionName = string.Empty;
            Flag = string.Empty;
            TimeToGo = string.Empty;
            RaceTime = string.Empty;
            LocalTime = string.Empty;
            TotalLaps = string.Empty;
        }
    }

    /// <summary>
    /// Parses a race clock string. Hours are unbounded - see <see cref="RaceTimeParser"/>.
    /// </summary>
    public static TimeSpan ParseRMTime(string time) => RaceTimeParser.Parse(time);

    #region Consistency Check

    //private void RunConsistencyCheck()
    //{
    //    if (sessionStatus == null)
    //        return;

    //    if (lastConsistencyCheckReset != null && (DateTime.Now - lastConsistencyCheckReset) < TimeSpan.FromSeconds(60))
    //        return;

    //    if (CurrentSortMode != SortMode.Position)
    //        return;

    //    bool isValid = true;
    //    if (CurrentGrouping == GroupMode.Overall)
    //    {
    //        var cars = Cars.ToList();
    //        isValid = ValidateSequential(cars, car => car.OverallPosition);
    //        if (!isValid)
    //        {
    //            Logger.LogWarning("Consistency check failed for overall positions");
    //        }
    //    }
    //    else
    //    {
    //        var groupedCars = GroupedCars.ToList();
    //        foreach (var group in groupedCars)
    //        {
    //            var cars = group.ToList();
    //            isValid = ValidateSequential(cars, car => car.ClassPosition);
    //            if (!isValid)
    //            {
    //                Logger.LogWarning("Consistency check failed for group {GroupName}", group.Name);
    //                break;
    //            }
    //        }
    //    }

    //    if (!isValid)
    //    {
    //        Logger.LogWarning("Consistency check failed for event {EventId}", EventModel.EventId);
    //        consistencyCheckFailures++;

    //        if (consistencyCheckFailures > 3)
    //        {
    //            Logger.LogWarning("Consistency check failures exceeded, resetting event");
    //            consistencyCheckFailures = 0;

    //            Dispatcher.UIThread.InvokeOnUIThread(() =>
    //            {
    //                // Reset the event
    //                carCache.Clear();
    //                Cars.Clear();
    //                GroupedCars.Clear();
    //            });
    //            lastConsistencyCheckReset = DateTime.Now;
    //        }
    //    }
    //    else if (consistencyCheckFailures > 0)
    //    {
    //        Logger.LogInformation("Consistency check passed, resetting counter");
    //        consistencyCheckFailures = 0;
    //    }
    //}

    //private bool ValidateSequential(List<CarViewModel> cars, Func<CarViewModel, int> getPosition)
    //{
    //    // Check positions are sequential and unique
    //    int lastPos = 0;
    //    foreach (var car in cars)
    //    {
    //        var pos = getPosition(car);
    //        if (pos == 0)
    //            continue; // Ignore cars with no position

    //        if (pos != lastPos + 1)
    //        {
    //            Logger.LogWarning("Consistency check failed for {CarNumber}. Expected position {Expected}, got {Actual}", car.Number, lastPos + 1, pos);
    //            return false;
    //        }
    //        lastPos = pos;
    //    }

    //    return true;
    //}

    public void InsertDuplicateCar()
    {
        var vm = new CarViewModel(EventModel, serverClient, hubClient, pitTracking, viewSizeService, httpClientFactory, configuration, loggerFactory)
        {
            Number = "DuplicateCar",
            Class = "Test Class",
            OverallPosition = 1,
        };
        if (Cars.Count > 0 && CurrentGrouping == GroupMode.Overall)
        {
            var c = Cars.First();
            if (c.LastCarPosition != null)
            {
                vm.ApplyPatch(CarPositionMapper.CreatePatch(new CarPosition(), c.LastCarPosition));
                Cars.Insert(0, vm);
            }
        }
        else if (CurrentGrouping == GroupMode.Class && GroupedCars.Count > 0)
        {
            var c = GroupedCars[0].First();
            if (c.LastCarPosition != null)
            {
                vm.ApplyPatch(CarPositionMapper.CreatePatch(new CarPosition(), c.LastCarPosition));
                GroupedCars[0].Insert(0, vm);
            }
        }
    }

    public void InsertDuplicateView()
    {
        var v = Cars.First();
        Cars.Insert(0, v);
    }

    #endregion

    #region Commands

    public void Back()
    {
        StopFullUpdateInterval();

        var routerEvent = new RouterEvent { Path = BackRouterPath };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    public void ToggleSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        if (!IsSearchVisible)
        {
            SearchText = string.Empty;
        }
    }

    public void ToggleLegend()
    {
        IsLegendVisible = !IsLegendVisible;
    }

    partial void OnSearchTextChanged(string value)
    {
        searchDebounce?.Dispose();
        searchDebounce = Observable.Timer(TimeSpan.FromMilliseconds(400))
            .Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => ApplySearchFilter(value)));
    }

    /// <remarks>
    /// Internal rather than private so a test can apply a filter without waiting out the debounce
    /// in <see cref="OnSearchTextChanged"/>.
    /// </remarks>
    internal void ApplySearchFilter(string text)
    {
        // The search is debounced, and disposing that timer cannot recall a callback it has already
        // posted to the dispatcher. Leaving an event while a keystroke is in flight would otherwise
        // land here after the filter subject has been disposed, and pushing onto a disposed subject
        // throws - reported, not fatal, but noise for a case that is simply finished with.
        if (disposed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            searchFilter = _ => true;
        }
        else
        {
            var terms = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            searchFilter = car =>
            {
                foreach (var term in terms)
                {
                    if (int.TryParse(term, out _))
                    {
                        if (car.Number.Equals(term, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    else
                    {
                        if (car.Number.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            car.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                return false;
            };
        }
        searchFilterSubject.OnNext(searchFilter);
    }

    /// <summary>
    /// Command to toggle between flat and grouped by class view.
    /// </summary>
    public void ToggleGroupMode()
    {
        if (CurrentGrouping == GroupMode.Overall)
        {
            CurrentGrouping = GroupMode.Class;
        }
        else
        {
            CurrentGrouping = GroupMode.Overall;
        }

        foreach (var car in carCache.Items)
        {
            car.CurrentGroupMode = CurrentGrouping;
        }

        if (CurrentSortMode == SortMode.Fastest)
        {
            UpdatePositionsByFastestTime();
        }
    }

    public void ToggleSortMode()
    {
        if (CurrentSortMode == SortMode.Position)
        {
            CurrentSortMode = SortMode.Fastest;
            UpdatePositionsByFastestTime();
        }
        else
        {
            CurrentSortMode = SortMode.Position;
            ResetPositionOverrides();
        }
    }

    public void LaunchBroadcast()
    {
        if (EventModel.Broadcast != null && !string.IsNullOrEmpty(EventModel.Broadcast.Url))
        {
            WeakReferenceMessenger.Default.Send(new LauncherEvent(EventModel.Broadcast.Url));
        }
    }

    public void OnOrganizationLogoClicked()
    {
        var now = DateTime.Now;
        // Reset counter if more than 2 seconds have passed since last click
        if ((now - lastLogoClickTime).TotalSeconds > 2)
        {
            logoClickCount = 0;
        }

        logoClickCount++;
        lastLogoClickTime = now;

        if (logoClickCount >= 5)
        {
            ShowLogDisplay = !ShowLogDisplay;
            logoClickCount = 0;
            Logger.LogInformation("Log display toggled: {ShowLogDisplay}", ShowLogDisplay);
        }
    }

    public async Task CopyLogsToClipboard()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(LogMessages))
                return;

            WeakReferenceMessenger.Default.Send(new CopyToClipboardRequest { Text = LogMessages });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error copying logs to clipboard");
        }
    }

    #endregion

    #region Logging

    private void OnLogAdded(object? sender, LogEntry logEntry)
    {
        // Only pay for rendering while the diagnostic display is actually open - entries arrive a
        // couple of times a second during a live session. Post rather than invoking inline: this
        // runs inside the logger call, which is usually itself inside a catch block.
        if (!ShowLogDisplay)
            return;

        // Queue at most one pending render: entries arrive a couple of times a second and each
        // render rebuilds the whole string, including any retained exception dumps.
        if (Interlocked.Exchange(ref logRefreshPending, 1) == 1)
            return;

        Dispatcher.UIThread.PostSafe(() =>
        {
            // Cleared first so entries arriving mid-render queue a fresh pass.
            Interlocked.Exchange(ref logRefreshPending, 0);
            RefreshLogMessages();
        }, Logger, DispatcherPriority.ContextIdle);
    }

    partial void OnShowLogDisplayChanged(bool value)
    {
        if (value)
        {
            Dispatcher.UIThread.InvokeOnUIThread(RefreshLogMessages, DispatcherPriority.ContextIdle);
        }
    }

    private void RefreshLogMessages()
    {
        if (logProvider == null)
            return;

        // Show retained warnings/errors above the rolling activity log. Routine traffic would
        // otherwise bury the one thing someone opening this display is looking for.
        var problems = logProvider.GetProblemEntries().ToArray();
        var problemSet = new HashSet<LogEntry>(problems);
        var recent = logProvider.GetLogEntries().Where(l => !problemSet.Contains(l)).Take(25);

        var sections = new List<string>();
        if (problems.Length > 0)
        {
            sections.Add("--- Warnings and errors ---");
            sections.AddRange(problems.Select(l => l.FormattedMessage));
            sections.Add("--- Recent activity ---");
        }
        sections.AddRange(recent.Select(l => l.FormattedMessage));

        LogMessages = string.Join(Environment.NewLine, sections);
    }

    #endregion
}
