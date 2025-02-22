using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;

namespace RedMist.Timing.UI.ViewModels;

public partial class EventViewModel : ObservableObject
{
    public int EventId { get; set; }
    [ObservableProperty]
    private string organization = string.Empty;
    [ObservableProperty]
    private string name = string.Empty;

    public void SelectEvent(object eventViewModel)
    {
        if (eventViewModel is EventViewModel evm)
        {
            var routerEvent = new RouterEvent { Path = "EventStatus", Data = evm.EventId };
            WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
        }
    }
}
