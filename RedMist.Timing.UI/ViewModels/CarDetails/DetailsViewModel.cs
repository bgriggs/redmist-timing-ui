using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels.CarDetails;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class DetailsViewModel : ObservableObject, IRecipient<ControlLogNotification>, IRecipient<CompetitorMetadataNotification>, IDisposable
{
    private readonly int eventId;
    private readonly int sessionId;
    private readonly string carNumber;
    private readonly EventClient serverClient;
    private readonly HubClient hubClient;
    private readonly PitTracking pitTracking;
    [ObservableProperty]
    private bool isLoading = false;

    [ObservableProperty]
    private bool isCarMetadataVisible = false;
    [ObservableProperty]
    private string name = string.Empty;
    [ObservableProperty]
    private string nationState = string.Empty;
    [ObservableProperty]
    private string sponsor = string.Empty;
    [ObservableProperty]
    private string hometown = string.Empty;
    [ObservableProperty]
    private string make = string.Empty;
    [ObservableProperty]
    private string modelEngine = string.Empty;
    [ObservableProperty]
    private string tires = string.Empty;
    [ObservableProperty]
    private string club = string.Empty;


    public ChartViewModel Chart { get; } = new ChartViewModel();
    public LapsListViewModel LapList { get; } = new LapsListViewModel();
    public ObservableCollection<ControlLogEntryViewModel> ControlLog { get; } = [];


    public DetailsViewModel(int eventId, int sessionId, string carNumber, EventClient serverClient, HubClient hubClient, PitTracking pitTracking)
    {
        this.eventId = eventId;
        this.sessionId = sessionId;
        this.carNumber = carNumber;
        this.serverClient = serverClient;
        this.hubClient = hubClient;
        this.pitTracking = pitTracking;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public async Task Initialize()
    {
        try
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            // Subscribe to get control logs
            _ = hubClient.SubscribeToCarControlLogs(eventId, carNumber);
            // Subscribe to get competitor metadata
            _ = hubClient.SubscribeToCompetitorMetadata(eventId, carNumber);

            var carPositions = await serverClient.LoadCarLapsAsync(eventId, sessionId, carNumber);
            Chart.UpdateLaps(carPositions);
            LapList.UpdateLaps(carPositions);
            //Debug.WriteLine($"Car positions loaded: {carPositions.Count}");
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
        pitTracking.ApplyPitStop(carPositions);
        Chart.UpdateLaps(carPositions);
        LapList.UpdateLaps(carPositions);
    }

    /// <summary>
    /// Control log data.
    /// </summary>
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

    /// <summary>
    /// Competitor metadata.
    /// </summary>
    public void Receive(CompetitorMetadataNotification message)
    {
        if (message.Value.CarNumber == carNumber)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Name = message.Value.FirstName + " " + message.Value.LastName;
                NationState = message.Value.NationState;
                Sponsor = message.Value.Sponsor;
                Hometown = message.Value.Hometown;
                Make = message.Value.Make;
                ModelEngine = message.Value.ModelEngine;
                Tires = message.Value.Tires;
                Club = message.Value.Club;
                IsCarMetadataVisible = true;
            });
        }
    }

    public void Dispose()
    {
        try
        {
            _ = hubClient.UnsubscribeFromCarControlLogs(eventId, carNumber);
            _ = hubClient.UnsubscribeFromCompetitorMetadata(eventId, carNumber);
        }
        catch { }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}
