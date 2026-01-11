using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
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
    private readonly string archiveBaseUrl;

    public ObservableCollection<FlagViewModel> Flags { get; } = [];
    private List<FlagDuration> lastFlagDurations = [];
    public string Name => eventModel.EventName ?? string.Empty;
    public Bitmap? OrganizationLogo
    {
        get
        {
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


    public FlagsViewModel(TimingCommon.Models.Event eventModel, EventClient eventClient, EventContext eventContext, IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        this.eventModel = eventModel;
        this.eventClient = eventClient;
        this.eventContext = eventContext;
        this.httpClientFactory = httpClientFactory;
        archiveBaseUrl = configuration["Cdn:ArchiveUrl"] ?? throw new ArgumentException("Cdn:ArchiveUrl is not configured.");
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public void Initialize()
    {
        Dispatcher.UIThread.InvokeOnUIThread(async () => await Refresh());
    }

    private async Task Refresh()
    {
        int sessionId = eventContext.SessionId;
        if (sessionId == 0)
        {
            Flags.Clear();
            return;
        }

        try
        {
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
        catch (Exception)
        {
            // Handle exceptions
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
            DateTime tod = default;
            if (session.LocalTimeOfDay != null)
            {
                DateTime.TryParseExact(session.LocalTimeOfDay, "HH:mm:ss", null, DateTimeStyles.None, out tod);
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
        catch { }
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
        var flags = await ArchiveHelper.DownloadArchivedDataAsync<List<FlagDuration>>(httpClientFactory, url);
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

    public void Update(FlagDuration flagDuration, DateTime timeOfDay, bool setMovingDuration = false)
    {
        StartTime = flagDuration.StartTime.ToString("h:mm tt");
        EndTime = flagDuration.EndTime?.ToString("h:mm tt") ?? string.Empty;

        if (flagDuration.EndTime != null)
        {
            var durTs = flagDuration.EndTime - flagDuration.StartTime;
            SetDurationStr(durTs.Value);
        }
        else if (setMovingDuration && timeOfDay != default)
        {
            var dts = timeOfDay - flagDuration.StartTime;
            SetDurationStr(dts);
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

        var parts = new[]
        {
            timeSpan.Hours > 0 ? $"{timeSpan.Hours}h" : null,
            timeSpan.Minutes > 0 || timeSpan.Hours > 0 ? $"{timeSpan.Minutes}m" : null,
            $"{timeSpan.Seconds}s"
        };

        return string.Join(" ", parts.Where(p => p != null));
    }
}

