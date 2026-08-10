using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Utilities;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class FlagsViewModel : ObservableObject, IRecipient<SessionStatusNotification>
{
    private readonly TimingCommon.Models.Event eventModel;
    private readonly EventContext eventContext;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly EventClient eventClient;
    private readonly OrganizationIconCacheService iconCacheService;
    private readonly string archiveBaseUrl;
    private ILogger Logger { get; }

    /// <summary>
    /// Accepted shapes of <see cref="SessionStatePatch.LocalTimeOfDay"/>, the track's wall clock.
    /// </summary>
    private static readonly string[] TimeOfDayFormats = [@"hh\:mm\:ss", @"h\:mm\:ss"];

    public ObservableCollection<FlagViewModel> Flags { get; } = [];
    private List<FlagDuration> lastFlagDurations = [];
    private TimeSpan? lastTimeOfDay;
    public bool HasNoFlags => Flags.Count == 0;
    public bool ShowNoFlagsMessage => !IsLoading && HasNoFlags;
    public string Name => eventModel.EventName ?? string.Empty;
    public Bitmap? OrganizationLogo
    {
        get
        {
            if (eventModel.OrganizationId > 0)
            {
                // Try to get from cache first
                var cached = iconCacheService.GetCachedIcon(eventModel.OrganizationId);
                if (cached != null)
                {
                    return cached;
                }
            }

            // Fallback to decoding byte array if not in cache
            if (eventModel.OrganizationLogo is not null && eventModel.OrganizationLogo.Length > 0)
            {
                using MemoryStream ms = new(eventModel.OrganizationLogo);
                return Bitmap.DecodeToWidth(ms, 165);
            }
            return null;
        }
    }

    [ObservableProperty]
    private bool allowEventList = true;
    [ObservableProperty]
    private bool isLoading = false;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowNoFlagsMessage));
    }


    public FlagsViewModel(Event eventModel, EventClient eventClient, EventContext eventContext, IHttpClientFactory httpClientFactory, IConfiguration configuration, OrganizationIconCacheService iconCacheService, ILoggerFactory loggerFactory)
    {
        this.eventModel = eventModel;
        this.eventClient = eventClient;
        this.eventContext = eventContext;
        this.httpClientFactory = httpClientFactory;
        this.iconCacheService = iconCacheService;
        archiveBaseUrl = configuration["Cdn:ArchiveUrl"] ?? throw new ArgumentException("Cdn:ArchiveUrl is not configured.");
        Logger = loggerFactory.CreateLogger(GetType().Name);
        WeakReferenceMessenger.Default.RegisterAll(this);

        // Load organization icon from cache or CDN
        if (eventModel.OrganizationId > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await iconCacheService.GetOrganizationIconAsync(eventModel.OrganizationId);
                    // Notify that the logo may have changed
                    Dispatcher.UIThread.InvokeOnUIThread(() => OnPropertyChanged(nameof(OrganizationLogo)));
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Error refreshing the organization logo for {OrganizationId}", eventModel.OrganizationId);
                }
            });
        }
    }


    public void Initialize()
    {
        Dispatcher.UIThread.InvokeOnUIThread(async () => await Refresh());
    }

    private async Task Refresh()
    {
        try
        {
            int sessionId = eventContext.SessionId;
            if (sessionId == 0)
            {
                Flags.Clear();
                OnPropertyChanged(nameof(HasNoFlags));
                OnPropertyChanged(nameof(ShowNoFlagsMessage));
                return;
            }

            IsLoading = true;
            List<FlagDuration> flags;
            if (eventModel.IsArchived)
            {
                flags = await LoadFlagsFromArchiveAsync(eventModel.EventId, sessionId);
            }
            else
            {
                flags = await eventClient.LoadFlagsAsync(eventModel.EventId, sessionId);
            }
            var sp = new SessionStatePatch { FlagDurations = flags };
            Receive(new SessionStatusNotification(sp));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading flags for event {EventId}", eventModel.EventId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Receive(SessionStatusNotification message)
    {
        Dispatcher.UIThread.InvokeOnUIThread(() => UpdateSession(message.Value));
    }

    private void UpdateSession(SessionStatePatch session)
    {
        try
        {
            // Kept as a time of day rather than a DateTime: the feed sends the track's wall clock
            // with no date, and stamping it with the viewing device's calendar date puts the two
            // sides of the duration subtraction on different days for anyone in another time zone.
            var tod = lastTimeOfDay;
            if (session.LocalTimeOfDay != null &&
                TimeSpan.TryParseExact(session.LocalTimeOfDay, TimeOfDayFormats, CultureInfo.InvariantCulture, out var parsedTod))
            {
                tod = parsedTod;
                lastTimeOfDay = parsedTod;
            }

            var fds = lastFlagDurations;
            if (session.FlagDurations != null)
            {
                fds = ProcessFlags(session.FlagDurations!);
                lastFlagDurations = fds;
            }

            // Update the view models with the flag durations
            for (int i = 0; i < Flags.Count; i++)
            {
                Flags[i].Update(fds.ElementAt(i), tod, i == 0);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error updating the flag list");
        }
    }

    private List<FlagDuration> ProcessFlags(List<FlagDuration> data)
    {
        var fds = data.OrderByDescending(x => x.StartTime).ToList();

        // Make the number of view models in the collection match the number of flags
        while (Flags.Count < fds.Count)
        {
            Flags.Add(new FlagViewModel());
        }

        // Remove extra view models from the collection
        while (Flags.Count > fds.Count)
        {
            Flags.RemoveAt(Flags.Count - 1);
        }

        OnPropertyChanged(nameof(HasNoFlags));
        OnPropertyChanged(nameof(ShowNoFlagsMessage));

        return fds;
    }

    [RelayCommand]
    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }

    private async Task<List<FlagDuration>> LoadFlagsFromArchiveAsync(int eventId, int sessionId)
    {
        var url = $"{archiveBaseUrl.TrimEnd('/')}/event-flags/event-{eventId}-session-{sessionId}-flags.gz";
        var flags = await ArchiveHelper.DownloadArchivedDataAsync<List<FlagDuration>>(httpClientFactory, url, Logger);
        return flags ?? [];
    }
}

public partial class FlagViewModel : ObservableObject
{
    [ObservableProperty]
    private string startTime = string.Empty;
    [ObservableProperty]
    private string endTime = string.Empty;
    [ObservableProperty]
    private string duration = string.Empty;
    [ObservableProperty]
    private string flagStr = string.Empty;
    [ObservableProperty]
    private Flags flag;

    /// <summary>
    /// Points this row at a flag.
    /// </summary>
    /// <param name="flagDuration">The flag to show. Its times are the track's local times.</param>
    /// <param name="trackTimeOfDay">
    /// The track's current wall clock, or null if the session hasn't reported one yet. Used only
    /// for the flag that is still running.
    /// </param>
    /// <param name="setMovingDuration">True for the newest flag, whose duration is still growing.</param>
    public void Update(FlagDuration flagDuration, TimeSpan? trackTimeOfDay, bool setMovingDuration = false)
    {
        StartTime = flagDuration.StartTime.ToString("h:mm tt");
        EndTime = flagDuration.EndTime?.ToString("h:mm tt") ?? string.Empty;

        if (flagDuration.EndTime != null)
        {
            var durTs = flagDuration.EndTime - flagDuration.StartTime;
            SetDurationStr(durTs.Value);
        }
        else if (setMovingDuration && trackTimeOfDay is { } clock)
        {
            // Anchor the track's clock to the day the flag started rather than to the viewing
            // device's calendar date - otherwise a viewer whose local date differs from the
            // track's measures the duration across a phantom day boundary. Roll forward once when
            // the session has passed midnight at the track.
            var nowAtTrack = flagDuration.StartTime.Date + clock;
            if (nowAtTrack < flagDuration.StartTime)
            {
                nowAtTrack = nowAtTrack.AddDays(1);
            }

            SetDurationStr(nowAtTrack - flagDuration.StartTime);
        }
        else
        {
            // These view models are pooled and re-pointed at whichever flag now occupies the row.
            // Without this the row would keep showing the previous flag's duration.
            Duration = string.Empty;
        }

        FlagStr = flagDuration.Flag != Flags.Unknown ? flagDuration.Flag.ToString() : string.Empty;
        Flag = flagDuration.Flag;
    }

    private void SetDurationStr(TimeSpan durTs)
    {
        if (durTs.TotalMilliseconds > 0)
        {
            Duration = FormatTimeSpan(durTs);
        }
        else
        {
            Duration = string.Empty;
        }
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalSeconds < 1)
            return "0s";

        // Total hours, not TimeSpan.Hours: the latter wraps at 24 and would render a day-long
        // green flag as a few minutes.
        var totalHours = (int)timeSpan.TotalHours;
        var parts = new[]
        {
            totalHours > 0 ? $"{totalHours}h" : null,
            timeSpan.Minutes > 0 || totalHours > 0 ? $"{timeSpan.Minutes}m" : null,
            $"{timeSpan.Seconds}s"
        };

        return string.Join(" ", parts.Where(p => p != null));
    }
}

