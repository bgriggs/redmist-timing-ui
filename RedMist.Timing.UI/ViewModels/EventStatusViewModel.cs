using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.TimingCommon.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class EventStatusViewModel : ObservableObject, IRecipient<StatusNotification>
{
    private static readonly DataGridComparerSortDescription sortDesc = new(new PositionComparer(), ListSortDirection.Ascending);
    private static readonly DataGridPathGroupDescription classGroupDesc = new("Class");

    private readonly HubClient hubClient;
    private ILogger Logger { get; }
    private int eventId;

    [ObservableProperty]
    private string eventName = string.Empty;

    [ObservableProperty]
    public string flag = string.Empty;

    [ObservableProperty]
    public string timeToGo = string.Empty;

    [ObservableProperty]
    public string totalTime = string.Empty;

    [ObservableProperty]
    public string totalLaps = string.Empty;

    private GroupMode currentGrouping = GroupMode.Overall;
    public string GroupToggleText
    {
        get
        {
            if (currentGrouping == GroupMode.Overall)
            {
                return "By Class";
            }
            else
            {
                return "Overall";
            }
        }
    }

    public ObservableCollection<CarViewModel> Cars { get; } = [];
    public DataGridCollectionView DataSource { get; set; }


    public EventStatusViewModel(HubClient hubClient, ILoggerFactory loggerFactory)
    {
        this.hubClient = hubClient;
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);

        DataSource = new DataGridCollectionView(Cars);
        DataSource.SortDescriptions.Add(sortDesc);
    }


    public async Task Initialize(int eventId)
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
            ApplyEntries(status.EventEntries);
        }
        else if (status.EventEntryUpdates.Count > 0)
        {
            ApplyEntries(status.EventEntryUpdates, isDeltaUpdate: true);
        }

        // Apply car position updates
        var carPositions = status.CarPositions;
        if (carPositions.Count > 0)
        {
            Logger.LogInformation("Car positions: {0}", carPositions.Count);
        }
        if (carPositions.Count == 0)
        {
            carPositions = status.CarPositionUpdates;
        }

        if (carPositions.Count > 0)
        {
            foreach (var carUpdate in carPositions)
            {
                var carVm = Cars.FirstOrDefault(c => c.Number == carUpdate.Number);
                if (carVm != null)
                {
                    carVm.ApplyStatus(carUpdate, out var positionChanged);
                    if (positionChanged)
                    {
                        DataSource.Refresh();
                    }
                }
            }

            if (Cars.Count > 0)
            {
                TotalLaps = Cars.Max(c => c.LastLap).ToString();
            }
            else
            {
                TotalLaps = string.Empty;
            }
        }
    }

    private void ResetEvent()
    {
        Cars.Clear();
        EventName = string.Empty;
        Flag = string.Empty;
        TimeToGo = string.Empty;
        TotalTime = string.Empty;
        TotalLaps = string.Empty;
        DataSource.Refresh();
    }

    /// <summary>
    /// Apply entries to the list of cars. This is used for initial load and delta updates.
    /// </summary>
    /// <param name="entries"></param>
    /// <param name="isDeltaUpdate">True when only updating existing cars. False when exactly matching the entry list</param>
    private void ApplyEntries(List<EventEntry> entries, bool isDeltaUpdate = false)
    {
        foreach (var entry in entries)
        {
            var carVm = Cars.FirstOrDefault(c => c.Number == entry.Number);
            if (carVm == null && !isDeltaUpdate)
            {
                carVm = new CarViewModel();
                Cars.Add(carVm);
            }
            carVm?.ApplyEntry(entry);
        }

        if (!isDeltaUpdate)
        {
            // Remove cars not in entries
            foreach (var carVm in Cars.ToList())
            {
                if (!entries.Any(e => e.Number == carVm.Number))
                {
                    Cars.Remove(carVm);
                    //DataSource.Refresh();
                }
            }
            DataSource.Refresh();
        }
    }

    public void ToggleGroupMode()
    {
        if (currentGrouping == GroupMode.Overall)
        {
            currentGrouping = GroupMode.Class;
            DataSource.GroupDescriptions.Add(classGroupDesc);
        }
        else
        {
            currentGrouping = GroupMode.Overall;
            DataSource.GroupDescriptions.Clear();
        }

        foreach (var car in Cars)
        {
            car.CurrentGroupMode = currentGrouping;
        }

        // Update the group toggle text
        OnPropertyChanged(nameof(GroupToggleText));
    }

    /// <summary>
    /// Comparer for sorting car positions. This is used to sort the cars in the DataGrid.
    /// </summary>
    private class PositionComparer : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is CarViewModel carX && y is CarViewModel carY)
            {
                return carX.SortablePosition.CompareTo(carY.SortablePosition);
            }
            return 0;
        }
    }
}
