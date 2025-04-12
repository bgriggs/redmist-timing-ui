using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// Available events to select from.
/// </summary>
public partial class EventsListViewModel : ObservableObject
{
    private readonly EventClient eventClient;
    private ILogger Logger { get; }

    public LargeObservableCollection<EventViewModel> Events { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string message = string.Empty;

    public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;


    public EventsListViewModel(EventClient eventClient, ILoggerFactory loggerFactory)
    {
        this.eventClient = eventClient;
        Logger = loggerFactory.CreateLogger(GetType().Name);
    }


    public async Task Initialize()
    {
        Message = string.Empty;
        try
        {
            var events = await eventClient.LoadRecentEventsAsync();
            if (events != null)
            {
                if (events.Length == 0)
                {
                    Message = "No events found";
                    Logger.LogInformation(Message);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    var vms = new List<EventViewModel>();
                    foreach (var e in events)
                    {
                        vms.Add(new EventViewModel(e));
                    }
                    Events.SetRange(vms);
                });
            }
            else
            {
                Message = "No events found - null";
                Logger.LogInformation(Message);
            }
        }
        catch (Exception ex)
        {
            Message = $"Error loading events: {ex}";
            Logger.LogError(ex, "Error loading events");
        }
    }

    public void RefreshEvents()
    {
        _ = Task.Run(Initialize);
    }
}
