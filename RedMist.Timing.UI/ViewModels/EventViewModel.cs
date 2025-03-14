using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.ViewModels;

public partial class EventViewModel(Event eventModel) : ObservableObject
{
    public Event EventModel { get; } = eventModel;
    public string Name => EventModel.EventName;

    public string Organization => EventModel.OrganizationName;

    public void SelectEvent(object eventViewModel)
    {
        if (eventViewModel is EventViewModel evm)
        {
            var routerEvent = new RouterEvent { Path = "EventStatus", Data = EventModel };
            WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
        }
    }
}
