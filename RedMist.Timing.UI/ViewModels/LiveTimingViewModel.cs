using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.InCarVideo;
using RedMist.TimingCommon.Models.Mappers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class LiveTimingViewModel : ObservableObject, IRecipient<SizeChangedNotification>,
    IRecipient<InCarVideoMetadataNotification>, IRecipient<AppResumeNotification>, IRecipient<SessionStatusNotification>,
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
    private readonly SessionState lastSessionState = new();

    private ILogger Logger { get; }

    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrganizationLogo))]
    [NotifyPropertyChangedFor(nameof(IsBroadcastVisible))]
    [NotifyPropertyChangedFor(nameof(BroadcastCompanyName))]
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
            if (EventModel.OrganizationLogo is not null && EventModel.OrganizationLogo.Length > 0)
            {
                using MemoryStream ms = new(EventModel.OrganizationLogo);
                return Bitmap.DecodeToWidth(ms, 55);
            }
            return null;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBroadcastVisible))]
    private bool isLive = false;

    public bool IsBroadcastVisible => IsLive && EventModel.Broadcast != null && !string.IsNullOrEmpty(EventModel.Broadcast.Url);
    public string? BroadcastCompanyName => EventModel.Broadcast?.CompanyName;

    private int consistencyCheckFailures;
    private DateTime? lastConsistencyCheckReset;

    private readonly PitTracking pitTracking = new();

    [ObservableProperty]
    private bool showPenaltyColumn = false;
    public const int PenaltyColumnWidth = 470;

    [ObservableProperty]
    private bool allowEventList = true;


    public LiveTimingViewModel(HubClient hubClient, EventClient serverClient, ILoggerFactory loggerFactory, ViewSizeService viewSizeService, EventContext eventContext)
    {
        this.hubClient = hubClient;
        this.serverClient = serverClient;
        this.viewSizeService = viewSizeService;
        this.eventContext = eventContext;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);

        // Flat
        var f = carCache.Connect()
            .AutoRefresh(t => t.OverallPosition)
            .AutoRefresh(t => t.SortablePosition)
            .AutoRefresh(t => t.BestTime)
            .SortAndBind(Cars, SortExpressionComparer<CarViewModel>.Ascending(t => t.SortablePosition))
            .DisposeMany()
            .Subscribe();

        // Grouped by class
        carCache.Connect()
            .GroupOnProperty(c => c.Class)
            .Transform(g => new GroupHeaderViewModel(g.Key, g.Cache), true)
            .SortAndBind(GroupedCars, SortExpressionComparer<GroupHeaderViewModel>.Ascending(t => t.Name))
            .DisposeMany()
            .Subscribe();

        Receive(new SizeChangedNotification(Avalonia.Size.Infinity));
    }


    public async Task InitializeLiveAsync(Event eventModel)
    {
        try
        {
            Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = true);
            EventModel = eventModel;
            Flag = string.Empty;
            pitTracking.Clear();
            ResetEvent();
            try
            {
                await RefreshStatus();
                await hubClient.SubscribeToEventAsync(EventModel.EventId);
                IsLive = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error subscribing to event: {ex.Message}");
            }
            finally
            {
                Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = false);
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
            fullUpdateInterval = Observable.Interval(TimeSpan.FromSeconds(5)).Subscribe(async _ => await RefreshStatus());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error initializing live timing.");
        }
    }

    public async Task RefreshStatus()
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
        ShowPenaltyColumn = viewSizeService.CurrentSize.Width > PenaltyColumnWidth;
        Logger.LogInformation("Size changed: {Width}x{Height}", message.Size.Width, message.Size.Height);
    }

    /// <summary>
    /// Handles in-car video metadata notifications and processes updates for in-car video metadata.
    /// </summary>
    /// <param name="message"></param>
    public void Receive(InCarVideoMetadataNotification message)
    {
        Dispatcher.UIThread.InvokeOnUIThread(() => ProcessInCarVideoUpdate(message.Value), DispatcherPriority.Background);
    }

    /// <summary>
    /// Handle case where the app was in the background not getting updates and now becomes active again.
    /// </summary>
    public void Receive(AppResumeNotification message)
    {
        if (!IsRealTime)
            return;
        Dispatcher.UIThread.InvokeOnUIThread(async () =>
        {
            try
            {
                IsLoading = true;
                await RefreshStatus();
            }
            finally
            {
                IsLoading = false;
            }
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
        if (message.Value.SessionName != null)
            SessionName = message.Value.SessionName;
        if (message.Value.CurrentFlag != null)
            Flag = message.Value.CurrentFlag.ToString() ?? string.Empty;
        if (message.Value.TimeToGo != null)
            TimeToGo = message.Value.TimeToGo;
        if (message.Value.RunningRaceTime != null)
            RaceTime = message.Value.RunningRaceTime;

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
            ApplyEntries(message.Value.EventEntries, isDeltaUpdate: false);

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
            _ = RefreshStatus();
        });
    }

    private void ApplyEntries(List<EventEntry> entries, bool isDeltaUpdate = false)
    {
        foreach (var entry in entries)
        {
            var carVm = carCache.Lookup(entry.Number);
            if (!carVm.HasValue && !isDeltaUpdate)
            {
                var vm = new CarViewModel(EventModel.EventId, serverClient, hubClient, pitTracking, viewSizeService) { CurrentGroupMode = CurrentGrouping };
                vm.ApplyEntry(entry);
                carCache.AddOrUpdate(vm);

                if (CurrentSortMode == SortMode.Fastest)
                {
                    UpdatePositionsByFastestTime();
                }
            }
            else if (carVm.HasValue)
            {
                carVm.Value.ApplyEntry(entry);
            }
        }

        if (!isDeltaUpdate)
        {
            // Remove cars not in entries
            foreach (var num in carCache.Keys)
            {
                if (!entries.Any(e => e.Number == num))
                {
                    carCache.RemoveKey(num);
                }
            }
        }
    }

    private void UpdateCars(CarPositionPatch[] carUpdates)
    {
        foreach (var carUpdate in carUpdates)
        {
            if (carUpdate.Number == null)
                continue;

            bool bestLapTimeChanged = false;
            var carVm = carCache.Lookup(carUpdate.Number);
            if (carVm.HasValue)
            {
                int lastBestLapTime = 0;
                if (CurrentSortMode == SortMode.Fastest)
                {
                    lastBestLapTime = carVm.Value.BestTimeMs;
                }

                // Update the car data
                carVm.Value.ApplyPatch(carUpdate);

                if (!bestLapTimeChanged && CurrentSortMode == SortMode.Fastest && lastBestLapTime != carVm.Value.BestTimeMs)
                {
                    bestLapTimeChanged = true;
                }
            }

            if (bestLapTimeChanged)
            {
                UpdatePositionsByFastestTime();
            }
            else if (CurrentSortMode == SortMode.Position)
            {
                // Reset position override
                ResetPositionOverrides();
            }
        }
    }

    private void UpdatePositionsByFastestTime()
    {
        // Sort the cars by fastest time
        if (CurrentGrouping == GroupMode.Overall)
        {
            var sortedCars = carCache.Items.OrderBy(c => c.BestTimeMs).ToList();
            for (int i = 0; i < sortedCars.Count; i++)
            {
                sortedCars[i].OverridePosition(i + 1);
            }
        }
        else if (CurrentGrouping == GroupMode.Class)
        {
            // Sort the cars by class and then by fastest time
            var sortedCars = carCache.Items.GroupBy(c => c.Class);
            foreach (var group in sortedCars)
            {
                var sortedGroup = group.OrderBy(c => c.BestTimeMs).ToList();
                for (int i = 0; i < sortedGroup.Count; i++)
                {
                    sortedGroup[i].OverridePosition(i + 1);
                }
            }
        }
    }

    private void ResetPositionOverrides()
    {
        carCache.Items.ToList().ForEach(car => car.OverridePosition(null));
    }

    private void ResetEvent()
    {
        // Allow for reset when the event is initializing. Once it has started,
        // suppress the resets to reduce user confusion
        if (string.IsNullOrWhiteSpace(Flag))
        {
            carCache.Clear();
            SessionName = string.Empty;
            Flag = string.Empty;
            TimeToGo = string.Empty;
            RaceTime = string.Empty;
            LocalTime = string.Empty;
            TotalLaps = string.Empty;
        }
    }

    private void RunConsistencyCheck()
    {
        if (sessionStatus == null)
            return;

        if (lastConsistencyCheckReset != null && (DateTime.Now - lastConsistencyCheckReset) < TimeSpan.FromSeconds(60))
            return;

        if (CurrentSortMode != SortMode.Position)
            return;

        bool isValid = true;
        if (CurrentGrouping == GroupMode.Overall)
        {
            var cars = Cars.ToList();
            isValid = ValidateSequential(cars, car => car.OverallPosition);
            if (!isValid)
            {
                Logger.LogWarning("Consistency check failed for overall positions");
            }
        }
        else
        {
            var groupedCars = GroupedCars.ToList();
            foreach (var group in groupedCars)
            {
                var cars = group.ToList();
                isValid = ValidateSequential(cars, car => car.ClassPosition);
                if (!isValid)
                {
                    Logger.LogWarning("Consistency check failed for group {GroupName}", group.Name);
                    break;
                }
            }
        }

        if (!isValid)
        {
            Logger.LogWarning("Consistency check failed for event {EventId}", EventModel.EventId);
            consistencyCheckFailures++;

            if (consistencyCheckFailures > 3)
            {
                Logger.LogWarning("Consistency check failures exceeded, resetting event");
                consistencyCheckFailures = 0;

                Dispatcher.UIThread.InvokeOnUIThread(() =>
                {
                    // Reset the event
                    carCache.Clear();
                    Cars.Clear();
                    GroupedCars.Clear();
                });
                lastConsistencyCheckReset = DateTime.Now;
            }
        }
        else if (consistencyCheckFailures > 0)
        {
            Logger.LogInformation("Consistency check passed, resetting counter");
            consistencyCheckFailures = 0;
        }
    }

    private bool ValidateSequential(List<CarViewModel> cars, Func<CarViewModel, int> getPosition)
    {
        // Check positions are sequential and unique
        int lastPos = 0;
        foreach (var car in cars)
        {
            var pos = getPosition(car);
            if (pos == 0)
                continue; // Ignore cars with no position

            if (pos != lastPos + 1)
            {
                Logger.LogWarning("Consistency check failed for {CarNumber}. Expected position {Expected}, got {Actual}", car.Number, lastPos + 1, pos);
                return false;
            }
            lastPos = pos;
        }

        return true;
    }

    private void ProcessInCarVideoUpdate(List<VideoMetadata> videoMetadata)
    {
        try
        {
            var cars = carCache.Items.ToArray();
            foreach (var car in cars)
            {
                if (car.LastCarPosition == null)
                {
                    car.UpdateCarStream(null);
                    continue;
                }

                VideoMetadata? vm = null;
                var transponder = car.LastCarPosition.TransponderId;
                if (transponder > 0)
                {
                    vm = videoMetadata.FirstOrDefault(vm => vm.TransponderId == transponder);
                }

                if (vm == null && !string.IsNullOrWhiteSpace(car.Number))
                {
                    // Try to match by car number if transponder not found
                    vm = videoMetadata.FirstOrDefault(vm => string.Compare(vm.CarNumber, car.Number, true, CultureInfo.InvariantCulture) == 0);
                }

                car.UpdateCarStream(vm);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing in-car video metadata");
        }
    }

    #region Commands

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = BackRouterPath };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
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

    public void InsertDuplicateCar()
    {
        var vm = new CarViewModel(EventModel.EventId, serverClient, hubClient, pitTracking, viewSizeService)
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
}
