using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MessagePack;
using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Utilities;
using RedMist.Timing.UI.ViewModels.CarDetails;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class DetailsViewModel : ObservableObject, IRecipient<ControlLogNotification>, IRecipient<AppResumeNotification>, IDisposable
{
    private readonly Event evt;
    private readonly int sessionId;
    private readonly string carNumber;
    private readonly EventClient serverClient;
    private readonly HubClient hubClient;
    private readonly PitTracking pitTracking;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly string archiveBaseUrl;

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


    public DetailsViewModel(Event evt, int sessionId, string carNumber, EventClient serverClient, HubClient hubClient,
        PitTracking pitTracking, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        this.evt = evt;
        this.sessionId = sessionId;
        this.carNumber = carNumber;
        this.serverClient = serverClient;
        this.hubClient = hubClient;
        this.pitTracking = pitTracking;
        this.httpClientFactory = httpClientFactory;
        archiveBaseUrl = configuration["Cdn:ArchiveUrl"] ?? throw new ArgumentException("Cdn:ArchiveUrl is not configured.");
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public async Task Initialize()
    {
        try
        {
            Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = true);
            // Subscribe to get control logs
            _ = hubClient.SubscribeToCarControlLogsAsync(evt.EventId, carNumber);

            // Load Competitor Metadata
            _ = Task.Run(async () =>
            {
                try
                {
                    CompetitorMetadata? competitorMetadata = null;
                    if (evt.IsArchived)
                    {
                        competitorMetadata = await LoadArchivedCompetitorMetadataAsync(evt.EventId, carNumber);
                    }
                    else
                    {
                        competitorMetadata = await serverClient.LoadCompetitorMetadataAsync(evt.EventId, carNumber);
                    }
                    if (competitorMetadata != null)
                    {
                        UpdateCompetitorMetadata(competitorMetadata);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading competitor metadata: {ex}");
                }
            });

            // Load control logs
            var carControlLogsTask = serverClient.LoadCarControlLogsAsync(evt.EventId, carNumber);

            List<CarPosition>? laps = null;
            if (evt.IsArchived)
            {
                laps = await LoadArchivedLapsAsync(evt.EventId, sessionId, carNumber);
            }
            else // Load laps
            {
                laps = await serverClient.LoadCarLapsAsync(evt.EventId, sessionId, carNumber);
            }

            Dispatcher.UIThread.InvokeOnUIThread(() =>
            {
                Chart.UpdateLaps(laps);
                LapList.UpdateLaps(laps);
            });

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
            Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = false);
        }
    }

    public void UpdateLaps(List<CarPosition> carPositions)
    {
        pitTracking.ApplyPitStop(carPositions);
        Dispatcher.UIThread.InvokeOnUIThread(() =>
        {
            Chart.UpdateLaps(carPositions);
            LapList.UpdateLaps(carPositions);
        });
    }

    /// <summary>
    /// Control log data.
    /// </summary>
    public void Receive(ControlLogNotification message)
    {
        if (message.Value.CarNumber == carNumber)
        {
            var controlLog = message.Value.ControlLogEntries.OrderByDescending(l => l.OrderId);
            Dispatcher.UIThread.InvokeOnUIThread(() =>
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
        try
        {
            await Initialize();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in AppResumeNotification handler for DetailsViewModel: {ex}");
        }
    }

    private void UpdateCompetitorMetadata(CompetitorMetadata cm)
    {
        Dispatcher.UIThread.InvokeOnUIThread(() =>
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

    private async Task<List<CarPosition>> LoadArchivedLapsAsync(int eventId, int sessionId, string carNumber)
    {
        try
        {
            // Build the URL: {archiveBaseUrl}/event-{eventId}-session-{sessionId}-car-laps/car-{carNum}-laps.gz
            var url = $"{archiveBaseUrl.TrimEnd('/')}/event-laps/event-{eventId}-session-{sessionId}-car-laps/car-{carNumber}-laps.gz";
            var laps = await ArchiveHelper.LoadArchivedData<List<CarPosition>>(httpClientFactory, url);
            return laps ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading archived laps: {ex}");
            return [];
        }
    }

    private async Task<CompetitorMetadata?> LoadArchivedCompetitorMetadataAsync(int eventId, string carNumber)
    {
        try
        {
            var url = $"{archiveBaseUrl.TrimEnd('/')}/event-competitor-metadata/event-{eventId}-competitor-metadata.gz";
            var eventMetadata = await ArchiveHelper.LoadArchivedData<List<CompetitorMetadata>>(httpClientFactory, url);
            var carMetadata = eventMetadata?.FirstOrDefault(cm => cm.CarNumber == carNumber);
            return carMetadata;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading archived laps: {ex}");
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            _ = hubClient.UnsubscribeFromCarControlLogsAsync(evt.EventId, carNumber);
        }
        catch { }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}
