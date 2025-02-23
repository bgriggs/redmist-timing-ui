using Avalonia.Threading;
using BigMission.Avalonia.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// Available events to select from.
/// </summary>
public class EventsListViewModel : ObservableObject
{
    private readonly EventClient eventClient;
    private ILogger Logger { get; }

    public LargeObservableCollection<EventViewModel> Events { get; } = [];


    public EventsListViewModel(EventClient eventClient, ILoggerFactory loggerFactory)
    {
        this.eventClient = eventClient;
        Logger = loggerFactory.CreateLogger(GetType().Name);
    }


    public async Task Initialize()
    {
        try
        {
            var temp = await eventClient.LoadCarLapsAsync(1, "909");
            var events = await eventClient.LoadRecentEventsAsync();
            if (events != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var vms = new List<EventViewModel>();
                    foreach (var e in events)
                    {
                        var vm = new EventViewModel
                        {
                            EventId = e.EventId,
                            Name = e.EventName,
                        };
                        vms.Add(vm);
                        Logger.LogInformation($"Event: {e.EventName}");
                    }
                    Events.SetRange(vms);
                });
            }
            else
            {
                Logger.LogInformation("No events found.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"Error loading events: {ex.Message}");
        }
    }

    public void RefreshEvents()
    {
        _ = Task.Run(Initialize);
    }
}
