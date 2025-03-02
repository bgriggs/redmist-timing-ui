using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Models;

public class ControlLogNotification(CarControlLogs ce) : ValueChangedMessage<CarControlLogs>(ce)
{
}
