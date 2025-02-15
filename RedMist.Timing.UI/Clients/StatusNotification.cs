using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Clients;

public class StatusNotification : ValueChangedMessage<Payload>
{
    public StatusNotification(Payload p) : base(p)
    {
    }
}
