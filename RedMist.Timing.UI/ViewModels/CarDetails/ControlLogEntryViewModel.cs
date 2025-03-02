using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.ViewModels.CarDetails;

public partial class ControlLogEntryViewModel : ObservableObject
{
    private readonly ControlLogEntry logEntry;

    public string Timestamp => logEntry.Timestamp.TimeOfDay.ToString(@"hh\:mm");

    public string Corner => logEntry.Corner;

    public string Car1 => logEntry.Car1;

    public string Car2 => logEntry.Car2;

    public string Note => logEntry.Note;

    public string Status => logEntry.Status;

    public string PenalityAction => logEntry.PenalityAction;

    public string OtherNotes => logEntry.OtherNotes;


    public ControlLogEntryViewModel(ControlLogEntry logEntry)
    {
        this.logEntry = logEntry;
    }
}
