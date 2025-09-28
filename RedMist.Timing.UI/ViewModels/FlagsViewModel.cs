using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class FlagsViewModel : ObservableObject, IRecipient<SessionStatusNotification>
{
    private Event eventModel;
    private EventContext eventContext;
    private readonly EventClient eventClient;

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
                return Bitmap.DecodeToWidth(ms, 55);
            }
            return null;
        }
    }

    [ObservableProperty]
    private bool allowEventList = true;
    [ObservableProperty]
    private bool isLoading = false;


    public FlagsViewModel(Event eventModel, EventClient eventClient, EventContext eventContext)
    {
        this.eventModel = eventModel;
        this.eventClient = eventClient;
        this.eventContext = eventContext;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public void Initialize()
    {
        if (Dispatcher.UIThread.CheckAccess())
            _ = Refresh();
        else
            Dispatcher.UIThread.Post(async () => await Refresh());
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
            var flags = await eventClient.LoadFlagsAsync(eventModel.EventId, sessionId);
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
        if (Dispatcher.UIThread.CheckAccess())
            UpdateSession(message.Value);
        else
            Dispatcher.UIThread.Post(() => UpdateSession(message.Value));
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

    public void Back()
    {
        var routerEvent = new RouterEvent { Path = "EventsList" };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
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

