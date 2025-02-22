using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Models;

public class StatusNotification : ValueChangedMessage<Payload>
{
    public StatusNotification(Payload p) : base(p)
    {
    }
}
