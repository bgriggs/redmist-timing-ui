using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.Timing.UI.Clients;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public class EventsListViewModel : ObservableObject
{
    private readonly EventClient eventClient;

    public EventsListViewModel(EventClient eventClient)
    {
        this.eventClient = eventClient;
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
                    Console.WriteLine($"Event: {e.EventName}");
                }
            }
            else
            {
                Console.WriteLine("No events found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading events: {ex.Message}");
        }
    }
}
