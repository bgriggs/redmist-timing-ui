using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.ViewModels;

public partial class SessionViewModel(Session session) : ObservableObject
{
    public string Name => session.Name;
    public string StartTime
    {
        get
        {
            var localTime = session.StartTime.AddHours(session.LocalTimeZoneOffset);
            return localTime.ToString("MM/dd h:mm tt");
        }
    }

    public void SelectSession(object? obj)
    {
        var routerEvent = new RouterEvent { Path = "SessionResults", Data = session };
        WeakReferenceMessenger.Default.Send(new ValueChangedMessage<RouterEvent>(routerEvent));
    }
}
