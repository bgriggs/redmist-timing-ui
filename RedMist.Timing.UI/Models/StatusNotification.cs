﻿using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Models;

public class StatusNotification(Payload p) : ValueChangedMessage<Payload>(p)
{
}
