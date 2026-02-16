using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class LiveTimingViewModel : ObservableObject, IRecipient<SizeChangedNotification>,
    IRecipient<AppResumeNotification>, IRecipient<SessionStatusNotification>,
    IRecipient<CarStatusNotification>, IRecipient<ResetNotification>
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

    public IImage? SentinelLegendImage => GetLegendImage(CarViewModel.SENTINEL_IMAGE);
    public IImage? MrlLegendImage => GetLegendImage(CarViewModel.MRL_IMAGE);

    private static IImage? GetLegendImage(string resourceKey)
    {
        if (Application.Current?.FindResource(Application.Current.ActualThemeVariant, resourceKey) is string uri)
        {
            return new Bitmap(AssetLoader.Open(new Uri(uri)));
        }
        return null;
    }

    [ObservableProperty]
    private string searchText = string.Empty;

    private Func<CarViewModel, bool> searchFilter = _ => true;
    private readonly BehaviorSubject<Func<CarViewModel, bool>> searchFilterSubject = new(_ => true);

    private IDisposable? searchDebounce;

    private int logoClickCount = 0;
    private DateTime lastLogoClickTime = DateTime.MinValue;


    public LiveTimingViewModel(HubClient hubClient, EventClient serverClient, ILoggerFactory loggerFactory, 
        ViewSizeService viewSizeService, EventContext eventContext, IHttpClientFactory httpClientFactory, 
        IConfiguration configuration, OrganizationIconCacheService iconCacheService, 
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
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);

        // Subscribe to log events
        if (logProvider != null)
        {
            logProvider.LogAdded += OnLogAdded;
            RefreshLogMessages();
        }
        // Flat
        var f = carCache.Connect()
            .Filter(searchFilterSubject)
            .AutoRefresh(t => t.OverallPosition)
            .AutoRefresh(t => t.SortablePosition)
            .AutoRefresh(t => t.BestTime)
            .SortAndBind(Cars, SortExpressionComparer<CarViewModel>.Ascending(t => t.SortablePosition))
            .DisposeMany()
            .Subscribe();

        // Grouped by class
        carCache.Connect()
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
    }


    public async Task InitializeLiveAsync(Event eventModel)
    {
        try
        {
            Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = true, DispatcherPriority.Background);
            EventModel = eventModel;
            Flag = string.Empty;
            pitTracking.Clear();

            // Initialize ShowPenaltyColumn based on event control log availability and current viewport size
            ShowPenaltyColumn = IsControlLogAvailable && viewSizeService.CurrentSize.Width > PenaltyColumnWidth;

            Logger.LogInformation("ResetEvent...");
            ResetEvent();

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
                        Logger.LogWarning(ex, "Failed to load organization icon");
                    }
                });
            }

            try
            {
                Logger.LogInformation("ResetState...");
                await Task.Run(() => RefreshStatusAsync());
                Logger.LogInformation("Subscribe...");
                await Task.Run(() => hubClient.SubscribeToEventAsync(EventModel.EventId));
                Logger.LogInformation("Completed subscribe...");
                IsLive = true;
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

            if (fullUpdateInterval != null)
            {
                try
                {
                    fullUpdateInterval.Dispose();
                }
                catch { }
                fullUpdateInterval = null;
            }
            fullUpdateInterval = Observable.Interval(TimeSpan.FromSeconds(5)).Subscribe(tick =>
            {
                try
                {
                    _ = RefreshStatusAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in periodic refresh timer");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing live timing.");
        }
    }

    public async Task RefreshStatusAsync()
    {
        try
        {
            var sw = Stopwatch.StartNew();

            sessionStatus = await serverClient.ExecuteWithRetryAsync(
                () => serverClient.LoadEventStatusAsync(EventModel.EventId),
                nameof(serverClient.LoadEventStatusAsync));

            if (sessionStatus == null)
            {
                Logger.LogWarning("No session status returned for event {EventId}", EventModel.EventId);
                return;
            }

            var patch = SessionStateMapper.CreatePatch(new SessionState(), sessionStatus);
            Receive(new SessionStatusNotification(patch));

            var carPatches = sessionStatus.CarPositions.Select(c => CarPositionMapper.CreatePatch(new CarPosition(), c)).ToArray();
            Receive(new CarStatusNotification(carPatches));
            Logger.LogInformation("Full update in {t}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Error refreshing status: {ex.Message}");
        }
    }

    public async Task UnsubscribeLiveAsync()
    {
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
                    await RefreshStatusAsync();
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
                Dispatcher.UIThread.Post(UpdateProjectedLapTimeProgression, DispatcherPriority.Background);
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
                    .Select(CarPositionMapper.ToPatch)
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
            _ = Task.Run(async () =>
            {
                try
                {
                    await RefreshStatusAsync();
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
                var vm = new CarViewModel(EventModel, serverClient, hubClient, pitTracking, viewSizeService, httpClientFactory, configuration, loggerFactory) { CurrentGroupMode = CurrentGrouping };
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
            // Remove cars not in entries
            var entryNumbers = new HashSet<string>(entries.Select(e => e.Number));
            foreach (var num in carCache.Keys)
            {
                if (!entryNumbers.Contains(num))
                {
                    carCache.RemoveKey(num);
                }
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

    private void UpdateProjectedLapTimeProgression()
    {
        var raceTime = ParseRMTime(RaceTime);
        foreach (var carVm in carCache.Items)
        {
            carVm.UpdateProjectedLapTimeProgression(raceTime);
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

    public static TimeSpan ParseRMTime(string time)
    {
        if (TimeSpan.TryParseExact(time, @"hh\:mm\:ss\.fff", null, TimeSpanStyles.None, out var result))
            return result;
        if (TimeSpan.TryParseExact(time, @"hh\:mm\:ss", null, TimeSpanStyles.None, out result))
            return result;
        return TimeSpan.Zero;
    }

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
        try
        {
            fullUpdateInterval?.Dispose();
        }
        catch { }

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

    private void ApplySearchFilter(string text)
    {
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
        Dispatcher.UIThread.InvokeOnUIThread(RefreshLogMessages, DispatcherPriority.ContextIdle);
    }

    private void RefreshLogMessages()
    {
        if (logProvider == null)
            return;

        var logs = logProvider.GetLogEntries().Take(25);
        LogMessages = string.Join(Environment.NewLine, logs.Select(l => l.FormattedMessage));
    }

    #endregion
}
