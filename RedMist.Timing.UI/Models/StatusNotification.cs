using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Models;


public class SessionStatusNotification(SessionStatePatch p) : ValueChangedMessage<SessionStatePatch>(p)
{
}

public class CarStatusNotification(CarPositionPatch[] p) : ValueChangedMessage<CarPositionPatch[]>(p)
{
}

public class ResetNotification() { }