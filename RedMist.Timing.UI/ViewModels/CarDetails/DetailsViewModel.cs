using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
using System.Linq;
using System.Net.Http;
using System.Threading;
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
    private ILogger Logger { get; }

    /// <summary>1 while a load is out, metadata included. See <see cref="Initialize"/>.</summary>
    private int loadInFlight;

    /// <summary>1 when a load has been asked for and not yet started. See <see cref="Initialize"/>.</summary>
    private int reloadOwed;

    /// <summary>Set by <see cref="Dispose"/>; a closed panel starts no more loads.</summary>
    private volatile bool disposed;

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

    public bool IsControlLogAvailable => evt.HasControlLog;

    public ChartViewModel Chart { get; } = new ChartViewModel();
    public LapsListViewModel LapList { get; } = new LapsListViewModel();
    public ObservableCollection<ControlLogEntryViewModel> ControlLog { get; } = [];


    public DetailsViewModel(Event evt, int sessionId, string carNumber, EventClient serverClient, HubClient hubClient,
        PitTracking pitTracking, IHttpClientFactory httpClientFactory, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        this.evt = evt;
        this.sessionId = sessionId;
        this.carNumber = carNumber;
        this.serverClient = serverClient;
        this.hubClient = hubClient;
        this.pitTracking = pitTracking;
        this.httpClientFactory = httpClientFactory;
        archiveBaseUrl = (configuration["Cdn:ArchiveUrl"] ?? throw new ArgumentException("Cdn:ArchiveUrl is not configured.")).TrimEnd('/');
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public async Task Initialize()
    {
        // One load out at a time for this panel. An app resume starts a load, and nothing used to stop
        // one starting while the last was still out, so on a connection that had stalled each resume
        // sent a whole set of requests - laps, competitor metadata, control log - alongside the ones
        // still waiting. A load asked for mid-load is not dropped, though: it runs once when the
        // current one finishes, however many asked, because the current one may be about to time out
        // and leave the panel empty - and the resume that came after the connection returned is the
        // one that would have filled it.
        //
        // The request is recorded before the guard is tried, so a load finishing at the same moment
        // either sees it and runs again, or has already let go of the guard for this call to take.
        // A closed panel starts no further loads. One already out still finishes, but a reload still
        // owed when it closed would subscribe to its control log again, taking HubClient's single slot
        // back from whichever car was opened since.
        Volatile.Write(ref reloadOwed, 1);
        while (!disposed && Volatile.Read(ref reloadOwed) == 1
               && Interlocked.CompareExchange(ref loadInFlight, 1, 0) == 0)
        {
            try
            {
                while (!disposed && Interlocked.Exchange(ref reloadOwed, 0) == 1)
                {
                    try
                    {
                        await LoadOnceAsync();
                    }
                    catch (Exception ex)
                    {
                        // LoadOnceAsync catches its own failures, so this is only for what gets past
                        // them. Faulting out of the loop would skip the check below for a request
                        // recorded during the load, leaving it unserved until the next one.
                        Logger.LogError(ex, "Error loading details for car {CarNumber}", carNumber);
                    }
                }
            }
            finally
            {
                Volatile.Write(ref loadInFlight, 0);
            }
        }
    }

    private async Task LoadOnceAsync()
    {
        Task? metadataLoad = null;
        try
        {
            Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = true);
            // Subscribe to get control logs
            _ = hubClient.SubscribeToCarControlLogsAsync(evt.EventId, carNumber);

            // Load Competitor Metadata
            metadataLoad = Task.Run(async () =>
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
                    Logger.LogError(ex, "Error loading competitor metadata for car {CarNumber}", carNumber);
                }
            });

            // Load control logs. Started before the laps so the two load together, and through a
            // wrapper that cannot fault: when the laps load below throws, nothing awaits this, and a
            // faulted task nobody awaits is reported from the finalizer as an unobserved exception -
            // which the app's global handler sends as unhandled, past the noise policy, once for every
            // expanded car on a bad connection.
            var carControlLogsTask = LoadCarControlLogsAsync();

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
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading details for car {CarNumber}", carNumber);
        }
        finally
        {
            Dispatcher.UIThread.InvokeOnUIThread(() => IsLoading = false);
        }

        // The panel stops showing as loading without the metadata, but the load is not over until it
        // answers: letting go of the guard while it was still out would let the next load send
        // another, so a metadata request that stalls holds back a reload just as a stalled laps request
        // does. It cannot fault - it catches its own failures above.
        if (metadataLoad is not null)
        {
            await metadataLoad;
        }
    }

    /// <summary>The car's control log, or null if it could not be loaded.</summary>
    /// <remarks>Never faults, because <see cref="LoadOnceAsync"/> may never await it. The failure is
    /// logged here instead, where the noise policy groups and rations it like any other load.</remarks>
    private async Task<CarControlLogs?> LoadCarControlLogsAsync()
    {
        try
        {
            return await serverClient.LoadCarControlLogsAsync(evt.EventId, carNumber);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading the control log for car {CarNumber}", carNumber);
            return null;
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
            // Sort and create ViewModels off the UI thread
            var entries = message.Value.ControlLogEntries
                .OrderByDescending(l => l.OrderId)
                .Select(e => new ControlLogEntryViewModel(e))
                .ToArray();

            Dispatcher.UIThread.InvokeOnUIThread(() =>
            {
                try
                {
                    ControlLog.Clear();
                    foreach (var entry in entries)
                    {
                        ControlLog.Add(entry);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error applying control log entries for car {CarNumber}", carNumber);
                }
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
            Logger.LogError(ex, "Error reloading car details after app resume");
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
            var url = $"{archiveBaseUrl}/event-laps/event-{eventId}-session-{sessionId}-car-laps/car-{carNumber}-laps.gz";
            var laps = await ArchiveHelper.DownloadArchivedDataAsync<List<CarPosition>>(httpClientFactory, url, Logger);
            return laps ?? [];
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading archived laps for car {CarNumber}", carNumber);
            return [];
        }
    }

    private async Task<CompetitorMetadata?> LoadArchivedCompetitorMetadataAsync(int eventId, string carNumber)
    {
        try
        {
            var url = $"{archiveBaseUrl}/event-competitor-metadata/event-{eventId}-competitor-metadata.gz";
            var eventMetadata = await ArchiveHelper.DownloadArchivedDataAsync<List<CompetitorMetadata>>(httpClientFactory, url, Logger);
            var carMetadata = eventMetadata?.FirstOrDefault(cm => cm.CarNumber == carNumber);
            return carMetadata;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading archived competitor metadata for car {CarNumber}", carNumber);
            return null;
        }
    }

    public void Dispose()
    {
        disposed = true;
        try
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _ = hubClient.UnsubscribeFromCarControlLogsAsync(evt.EventId, carNumber);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error releasing car details for {CarNumber}", carNumber);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}
