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

public partial class DetailsViewModel : ObservableObject, IRecipient<ControlLogNotification>, IRecipient<AppResumeNotification>, IDisposable
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

    [ObservableProperty]
    private bool isTableTabSelected = true;
    [ObservableProperty]
    private bool isChartTabSelected = false;
    [ObservableProperty]
    private bool isPenaltiesTabSelected = false;

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
            _ = hubClient.SubscribeToCarControlLogsAsync(eventId, carNumber);

            // Load Competitor Metadata
            _ = serverClient.LoadCompetitorMetadataAsync(eventId, carNumber).ContinueWith(t =>
            {
                if (t.Result != null)
                {
                    UpdateCompetitorMetadata(t.Result);
                }
            });

            // Load control logs
            var carControlLogsTask = serverClient.LoadCarControlLogsAsync(eventId, carNumber);

            // Load laps
            var carPositions = await serverClient.LoadCarLapsAsync(eventId, sessionId, carNumber);
            Chart.UpdateLaps(carPositions);
            LapList.UpdateLaps(carPositions);

            // Apply control logs
            var carControlLogs = await carControlLogsTask;
            if (carControlLogs != null)
            {
                Receive(new ControlLogNotification(carControlLogs));
            }

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
    /// Handle chase where the app was in the background not getting updates and now becomes active again.
    /// </summary>
    public async void Receive(AppResumeNotification message)
    {
        await Initialize();
    }

    private void UpdateCompetitorMetadata(CompetitorMetadata cm)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Name = cm.FirstName + " " + cm.LastName;
            NationState = cm.NationState;
            Sponsor = cm.Sponsor;
            Hometown = cm.Hometown;
            Make = cm.Make;
            ModelEngine = cm.ModelEngine;
            Tires = cm.Tires;
            Club = cm.Club;
            IsCarMetadataVisible = true;
        });
    }

    public void Dispose()
    {
        try
        {
            _ = hubClient.UnsubscribeFromCarControlLogsAsync(eventId, carNumber);
        }
        catch { }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}
