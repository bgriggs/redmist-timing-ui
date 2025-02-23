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
            if (liveTimingPage.DataContext is LiveTimingViewModel ltVm && router.Data is int eventId)
            {
                _ = Task.Run(() => ltVm.InitializeAsync(eventId));
                liveTimingPage.IsVisible = true;
            }
        }
        else if (router.Path == "EventsList")
        {
            if (liveTimingPage.DataContext is LiveTimingViewModel ltVm)
            {
                _ = Task.Run(ltVm.UnsubscribeAsync);
            }

            eventsPage.IsVisible = true;
            liveTimingPage.IsVisible = false;
        }
        else
        {
            eventsPage.IsVisible = false;
            liveTimingPage.IsVisible = false;
        }
    }
}
