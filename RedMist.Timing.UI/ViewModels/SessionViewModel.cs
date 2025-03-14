using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.ViewModels;

public partial class SessionViewModel(Session session) : ObservableObject
{
    public string Name => session.Name;
    public string StartTime => session.StartTime.ToString("mm/dd h:m tt");
}
