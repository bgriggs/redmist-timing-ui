using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.TimingCommon.Models.Configuration;

namespace RedMist.Timing.UI.ViewModels;

public partial class ScheduleEntryViewModel : ObservableObject
{
    private readonly EventScheduleEntry entry;

    public string Name => entry.Name;
    public string StartTime { get; private set; }
    public string EndTime { get; private set; }


    public ScheduleEntryViewModel(EventScheduleEntry entry)
    {
        this.entry = entry;
        StartTime = entry.StartTime.ToString("h:mm tt");
        EndTime = entry.EndTime.ToString("h:mm tt");
    }
}
