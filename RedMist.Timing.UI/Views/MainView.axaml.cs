using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Views;

public partial class MainView : UserControl, IRecipient<ValueChangedMessage<RouterEvent>>
{
    public MainView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    public void Receive(ValueChangedMessage<RouterEvent> message)
    {
        var router = message.Value;
        if (router.Path == "EventStatus")
        {
            eventsPage.IsVisible = false;
            if (eventStatusPage.DataContext is EventStatusViewModel esVm && router.Data is int eventId)
            {
                _ = Task.Run(() => esVm.InitializeAsync(eventId));
                eventStatusPage.IsVisible = true;
            }
        }
        else if (router.Path == "EventsList")
        {
            if (eventStatusPage.DataContext is EventStatusViewModel esVm)
            {
                _ = Task.Run(esVm.UnsubscribeAsync);
            }

            eventsPage.IsVisible = true;
            eventStatusPage.IsVisible = false;
        }
        else
        {
            eventsPage.IsVisible = false;
            eventStatusPage.IsVisible = false;
        }
    }
}
