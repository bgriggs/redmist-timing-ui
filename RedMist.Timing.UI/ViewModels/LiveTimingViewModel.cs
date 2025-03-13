using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class LiveTimingViewModel : ObservableObject, IRecipient<StatusNotification>
{
    // Flat collection for the view
    public ObservableCollection<CarViewModel> Cars { get; } = [];
    // Grouped by class collection for the view
    public ObservableCollection<GroupHeaderViewModel> GroupedCars { get; } = [];
    protected readonly SourceCache<CarViewModel, string> carCache = new(car => car.Number);

    private readonly HubClient hubClient;
    private readonly EventClient serverClient;

    private ILogger Logger { get; }
    private int eventId;

    [ObservableProperty]
    private string eventName = string.Empty;

    [ObservableProperty]
    private string flag = string.Empty;

    [ObservableProperty]
    private string timeToGo = string.Empty;

    [ObservableProperty]
    private string totalTime = string.Empty;

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
                return "By Class";
            return "Overall";
        }
    }
    public bool IsFlat => CurrentGrouping == GroupMode.Overall;

    private IDisposable? consistencyCheckInterval;


    public LiveTimingViewModel(HubClient hubClient, EventClient serverClient, ILoggerFactory loggerFactory)
    {
        this.hubClient = hubClient;
        this.serverClient = serverClient;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);

        // Flat
        carCache.Connect()
            .AutoRefresh(t => t.OverallPosition)
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
    }


    public async Task InitializeAsync(int eventId)
    {
        this.eventId = eventId;
        Flag = string.Empty;
        ResetEvent();
        try
        {
            await hubClient.SubscribeToEvent(eventId);
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
        consistencyCheckInterval = Observable.Interval(TimeSpan.FromSeconds(10)).Subscribe(_ => ConsistencyCheck());
    }

    public async Task UnsubscribeAsync()
    {
        try
        {
            await hubClient.UnsubscribeFromEvent(eventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Error unsubscribing event: {ex.Message}");
        }
    }

    public void Receive(StatusNotification message)
    {
        var status = message.Value;
        if (status.EventId != eventId)
            return;

        Dispatcher.UIThread.Post(() => ProcessUpdate(status));
    }

    private void ProcessUpdate(Payload status)
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
            TotalTime = status.EventStatus.TotalTime;
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
    }

    private void ApplyEntries(List<EventEntry> entries, bool isDeltaUpdate = false)
    {
        foreach (var entry in entries)
        {
            var carVm = carCache.Lookup(entry.Number);
            if (!carVm.HasValue && !isDeltaUpdate)
            {
                var vm = new CarViewModel(eventId, serverClient, hubClient);
                vm.ApplyEntry(entry);
                carCache.AddOrUpdate(vm);
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

            var carVm = carCache.Lookup(carUpdate.Number);
            if (carVm.HasValue)
            {
                carVm.Value.ApplyStatus(carUpdate);
            }
        }
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
            TotalTime = string.Empty;
            TotalLaps = string.Empty;
        }
    }

    private void ConsistencyCheck()
    {
        bool duplicates = false;

        // Check the overall positions are unique
        var positions = new Dictionary<int, List<string>>();
        foreach (var c in carCache.Items)
        {
            if (!positions.TryGetValue(c.OverallPosition, out var cars))
            {
                cars = [];
                positions[c.OverallPosition] = cars;
            }
            cars.Add(c.Number);
        }

        foreach (var pos in positions)
        {
            if (pos.Value.Count > 1)
            {
                duplicates = true;
                break;
            }
        }

        if (!duplicates)
        {
            // Check the class positions
            // Group by class then group by class position with each var in that position in a list
            var classGroups = new Dictionary<string, Dictionary<int, List<string>>>();
            foreach (var c in carCache.Items)
            {
                if (!classGroups.TryGetValue(c.Class, out var classPositions))
                {
                    classPositions = [];
                    classGroups[c.Class] = classPositions;
                }
                if (!classPositions.TryGetValue(c.ClassPosition, out var cars))
                {
                    cars = [];
                    classPositions[c.ClassPosition] = cars;
                }
                cars.Add(c.Number);
            }

            foreach (var classPos in classGroups)
            {
                foreach (var pos in classPos.Value)
                {
                    if (pos.Value.Count > 1)
                    {
                        duplicates = true;
                        break;
                    }
                }
            }
        }

        if (duplicates)
        {
            Logger.LogWarning("Consistency check duplicate positions found");
            // Reset the event
            carCache.Clear();

            // Send request to server to get the latest car positions
            try
            {
                _ = hubClient.SubscribeToEvent(eventId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error subscribing to event: {ex.Message}");
            }
        }
    }

    #region Commands

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
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
    }

    #endregion
}
