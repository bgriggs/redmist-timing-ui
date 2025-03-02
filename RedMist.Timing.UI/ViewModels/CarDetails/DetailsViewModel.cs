using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels.CarDetails;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class DetailsViewModel : ObservableObject, IRecipient<ControlLogNotification>, IDisposable
{
    private readonly int eventId;
    private readonly string carNumber;
    private readonly EventClient serverClient;
    private readonly HubClient hubClient;
    [ObservableProperty]
    private bool isLoading = false;

    public ChartViewModel Chart { get; } = new ChartViewModel();
    public LapsListViewModel LapList { get; } = new LapsListViewModel();
    public ObservableCollection<ControlLogEntryViewModel> ControlLog { get; } = [];

    public DetailsViewModel(int eventId, string carNumber, EventClient serverClient, HubClient hubClient)
    {
        this.eventId = eventId;
        this.carNumber = carNumber;
        this.serverClient = serverClient;
        this.hubClient = hubClient;

        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public async Task Initialize()
    {
        try
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            // Subscribe to get control logs
            _ = hubClient.SubscribeToControlLogs(eventId, carNumber);

            var carPositions = await serverClient.LoadCarLapsAsync(eventId, carNumber);
            Chart.UpdateLaps(carPositions);
            LapList.UpdateLaps(carPositions);
            Debug.WriteLine($"Car positions loaded: {carPositions.Count}");
        }
        catch (Exception)
        {
            // Handle exceptions
        }
        finally
        {
            Dispatcher.UIThread.Post(() => IsLoading = false);
        }
    }

    public void UpdateLaps(List<CarPosition> carPositions)
    {
        Chart.UpdateLaps(carPositions);
        LapList.UpdateLaps(carPositions);
    }

    public void Receive(ControlLogNotification message)
    {
        if (message.Value.CarNumber == carNumber)
        {
            var controlLog = message.Value.ControlLogEntries.OrderByDescending(l => l.OrderId);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    ControlLog.Clear();
                    foreach (var entry in controlLog)
                    {
                        ControlLog.Add(new ControlLogEntryViewModel(entry));
                    }
                }
                catch { }
            });
        }
    }

    public void Dispose()
    {
        try
        {
            _ = hubClient.UnsubscribeFromControlLogs(eventId, carNumber);
        }
        catch { }
    }
}
