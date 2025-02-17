using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public class EventsListViewModel : ObservableObject
{
    private readonly EventClient eventClient;
    private ILogger Logger { get; }

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
                foreach (var e in events)
                {
                    Logger.LogInformation($"Event: {e.EventName}");
                }
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
}
