using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using DynamicData;
using DynamicData.Binding;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels.DataCollections;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class LiveTimingViewModel : ObservableObject, IRecipient<StatusNotification>
{
    private readonly ObservableCollection<CarViewModel> cars = [];
    public ObservableCollection<CarViewModel> Cars => cars;
    protected readonly SourceCache<CarViewModel, string> carCache = new(car => car.Number);

    //public CarPositionCollection CarPositions { get; }
    //public GroupedCarPositionCollection GroupedCarPositions { get; }

    private readonly HubClient hubClient;
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


    public LiveTimingViewModel(HubClient hubClient, ILoggerFactory loggerFactory)
    {
        this.hubClient = hubClient;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);

        //CarPositions = new CarPositionCollection(cars);
        //GroupedCarPositions = new GroupedCarPositionCollection(cars);
        carCache.Connect().AutoRefresh(t => t.OverallPosition)
            .SortAndBind(cars, SortExpressionComparer<CarViewModel>.Ascending(t => t.OverallPosition))
            .DisposeMany()
            .Subscribe();
    }


    public async Task InitializeAsync(int eventId)
    {
        this.eventId = eventId;
        try
        {
            await hubClient.SubscribeToEvent(eventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Error subscribing to event: {ex.Message}");
        }
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
                var vm = new CarViewModel();
                vm.ApplyEntry(entry);
                carCache.AddOrUpdate(vm);
            }
            else
            {
                carVm.Value.ApplyEntry(entry);
            }
        }

        if (!isDeltaUpdate)
        {
            // Remove cars not in entries
            foreach (var carVm in carCache.Items)
            {
                if (!entries.Any(e => e.Number == carVm.Number))
                {
                    carCache.Remove(carVm);
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
                carVm.Value.ApplyStatus(carUpdate, out var positionChanged);
            }
        }
    }

    private void ResetEvent()
    {
        carCache.Clear();
        EventName = string.Empty;
        Flag = string.Empty;
        TimeToGo = string.Empty;
        TotalTime = string.Empty;
        TotalLaps = string.Empty;
    }

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

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

        foreach (var car in cars)
        {
            car.CurrentGroupMode = CurrentGrouping;
        }
    }
}
