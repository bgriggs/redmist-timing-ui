using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;
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

    public ObservableCollection<EventViewModel> Events { get; } = [];


    public EventsListViewModel(EventClient eventClient, ILoggerFactory loggerFactory)
    {
        this.eventClient = eventClient;
        Logger = loggerFactory.CreateLogger(GetType().Name);
    }


    public async Task Initialize()
    {
        try
        {
            var events = await eventClient.LoadEvents();
            if (events != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Events.Clear();
                    foreach (var e in events)
                    {
                        var vm = new EventViewModel
                        {
                            EventId = e.EventId,
                            Name = e.EventName,
                        };
                        Events.Add(vm);
                        Logger.LogInformation($"Event: {e.EventName}");
                    }
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
