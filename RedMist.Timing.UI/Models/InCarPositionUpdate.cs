using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models.InCarDriverMode;

namespace RedMist.Timing.UI.Models;

public class InCarPositionUpdate(InCarPayload p) : ValueChangedMessage<InCarPayload>(p)
{
}
