using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels;

public partial class FlagsViewModel : ObservableObject, IRecipient<StatusNotification>
{
    private Event eventModel;
    public ObservableCollection<FlagViewModel> Flags { get; } = [];
    public string Name => eventModel.EventName ?? string.Empty;
    public Bitmap? OrganizationLogo
    {
        get
        {
            if (eventModel.OrganizationLogo is not null)
            {
                using MemoryStream ms = new(eventModel.OrganizationLogo);
                return Bitmap.DecodeToWidth(ms, 55);
            }
            return null;
        }
    }

    [ObservableProperty]
    private bool allowEventList = true;


    public FlagsViewModel(Event eventModel)
    {
        this.eventModel = eventModel;
        WeakReferenceMessenger.Default.RegisterAll(this);
    }


    public void Receive(StatusNotification message)
    {
        if (message.Value.FlagDurations == null)
            return;

        var fds = message.Value.FlagDurations.OrderByDescending(x => x.StartTime);

        // Make the number of view models in the collection match the number of flags
        while (Flags.Count < fds.Count())
        {
            Flags.Add(new FlagViewModel());
        }

        // Remove extra view models from the collection
        while (Flags.Count > fds.Count())
        {
            Flags.RemoveAt(Flags.Count - 1);
        }

        DateTime tod = default;
        if (message.Value.EventStatus != null)
        {
            DateTime.TryParseExact(message.Value.EventStatus.LocalTimeOfDay, "HH:mm:ss", null, DateTimeStyles.None, out tod);
        }

        // Update the view models with the flag durations
        for (int i = 0; i < Flags.Count; i++)
        {
            Flags[i].Update(fds.ElementAt(i), tod, i == 0);
        }
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

