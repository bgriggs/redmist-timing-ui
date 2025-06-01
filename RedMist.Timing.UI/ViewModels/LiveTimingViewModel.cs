using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.InCarVideo;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class LiveTimingViewModel : ObservableObject, IRecipient<StatusNotification>, IRecipient<SizeChangedNotification>, IRecipient<InCarVideoMetadataNotification>
{
    // Flat collection for the view
    public ObservableCollection<CarViewModel> Cars { get; } = [];
    // Grouped by class collection for the view
    public ObservableCollection<GroupHeaderViewModel> GroupedCars { get; } = [];
    protected readonly SourceCache<CarViewModel, string> carCache = new(car => car.Number);

    private readonly HubClient hubClient;
    private readonly EventClient serverClient;
    private readonly ViewSizeService viewSizeService;

    private ILogger Logger { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OrganizationLogo))]
    [NotifyPropertyChangedFor(nameof(IsBroadcastVisible))]
    [NotifyPropertyChangedFor(nameof(BroadcastCompanyName))]
    private Event eventModel = new();

    [ObservableProperty]
    private string eventName = string.Empty;

    [ObservableProperty]
    private string flag = string.Empty;

    [ObservableProperty]
    private string timeToGo = string.Empty;

    [ObservableProperty]
    private string localTime = string.Empty;

    [ObservableProperty]
    private string raceTime = string.Empty;

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

    private IDisposable? consistencyCheckInterval;

    public string BackRouterPath { get; set; } = "EventsList";

    public Bitmap? OrganizationLogo
    {
        get
        {
            if (EventModel.OrganizationLogo is not null)
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
    private Payload? lastFullPayload;
    private DateTime? lastConsistencyCheckReset;

    private readonly PitTracking pitTracking = new();

    [ObservableProperty]
    private bool showPenaltyColumn = false;
    public const int PenaltyColumnWidth = 470;

    [ObservableProperty]
    private bool allowEventList = true;



    public LiveTimingViewModel(HubClient hubClient, EventClient serverClient, ILoggerFactory loggerFactory, ViewSizeService viewSizeService)
    {
        this.hubClient = hubClient;
        this.serverClient = serverClient;
        this.viewSizeService = viewSizeService;
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
        EventModel = eventModel;
        Flag = string.Empty;
        pitTracking.Clear();
        ResetEvent();
        try
        {
            await hubClient.SubscribeToEventAsync(EventModel.EventId);
            IsLive = true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Error subscribing to event: {ex.Message}");
        }

        if (consistencyCheckInterval != null)
        {
            try
            {
                consistencyCheckInterval.Dispose();
            }
            catch { }
            consistencyCheckInterval = null;
        }
        consistencyCheckInterval = Observable.Interval(TimeSpan.FromSeconds(3)).Subscribe(_ => RunConsistencyCheck());

        
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
    /// Handles incoming status notifications and processes updates if certain conditions are met.
    /// </summary>
    public void Receive(StatusNotification message)
    {
        var status = message.Value;
        if (!IsLive || status.EventId != EventModel.EventId)
            return;

        Dispatcher.UIThread.Post(() => ProcessUpdate(status));
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
        Dispatcher.UIThread.Post(() => ProcessInCarVideoUpdate(message.Value), DispatcherPriority.Background);
    }

    public void ProcessUpdate(Payload status)
    {
        if (status.IsReset)
        {
            ResetEvent();
            return;
        }

        if (status.EventName != null)
        {
            EventName = status.EventName;
        }

        if (status.EventStatus != null)
        {
            Flag = status.EventStatus.Flag.ToString();
            TimeToGo = status.EventStatus.TimeToGo;
            RaceTime = status.EventStatus.RunningRaceTime;
            if (DateTime.TryParseExact(status.EventStatus.LocalTimeOfDay, "HH:mm:ss", null, DateTimeStyles.None, out var tod))
            {
                LocalTime = tod.ToString("h:mm:ss tt");
            }
            if (lastIsQualifying == null || lastIsQualifying != status.EventStatus.IsPracticeQualifying)
            {
                // Only update the sort if it has changed to avoid overriding the user
                if (status.EventStatus.IsPracticeQualifying && CurrentSortMode != SortMode.Fastest)
                {
                    ToggleSortMode();
                }
                else if (!status.EventStatus.IsPracticeQualifying && CurrentSortMode != SortMode.Position)
                {
                    ToggleSortMode();
                }
                lastIsQualifying = status.EventStatus.IsPracticeQualifying;
            }
        }

        // Update event entries
        if (status.EventEntries.Count > 0)
        {
            ApplyEntries(status.EventEntries, isDeltaUpdate: false);
        }
        else if (status.EventEntryUpdates.Count > 0)
        {
            ApplyEntries(status.EventEntryUpdates, isDeltaUpdate: true);
        }

        // Apply car position updates
        var carPositions = status.CarPositions.Concat(status.CarPositionUpdates);
        UpdateCarTiming([.. carPositions]);

        if (carCache.Count > 0)
        {
            TotalLaps = carCache.Items.Max(c => c.LastLap).ToString();
        }
        else
        {
            TotalLaps = string.Empty;
        }

        if (status.CarPositions.Count > 0)
        {
            lastFullPayload = status;
        }

        //Receive(new InCarVideoMetadataNotification([new VideoMetadata { TransponderId = 11650187, IsLive = true, SystemType = VideoSystemType.Sentinel, Destinations = [new() { Type = VideoDestinationType.Youtube, Url = "https://www.youtube.com/@bigmissionmotorsport" }] }]));
    }

    private void ApplyEntries(List<EventEntry> entries, bool isDeltaUpdate = false)
    {
        foreach (var entry in entries)
        {
            var carVm = carCache.Lookup(entry.Number);
            if (!carVm.HasValue && !isDeltaUpdate)
            {
                var vm = new CarViewModel(EventModel.EventId, serverClient, hubClient, pitTracking, viewSizeService);
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

    private void UpdateCarTiming(List<CarPosition> carPositions)
    {
        foreach (var carUpdate in carPositions)
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
                carVm.Value.ApplyStatus(carUpdate);

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
            EventName = string.Empty;
            Flag = string.Empty;
            TimeToGo = string.Empty;
            RaceTime = string.Empty;
            LocalTime = string.Empty;
            TotalLaps = string.Empty;
        }
    }

    private void RunConsistencyCheck()
    {
        if (lastFullPayload == null)
            return;

        if (lastConsistencyCheckReset != null && (DateTime.Now - lastConsistencyCheckReset) < TimeSpan.FromSeconds(60))
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

                // Reset the event
                carCache.Clear();
                Cars.Clear();
                ProcessUpdate(lastFullPayload);
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
        if (Cars.Count > 0)
        {
            var c = Cars.First();
            if (c.LastCarPosition != null)
            {
                vm.ApplyStatus(c.LastCarPosition);
                Cars.Insert(0, vm);
            }
        }
    }

    #endregion
}
