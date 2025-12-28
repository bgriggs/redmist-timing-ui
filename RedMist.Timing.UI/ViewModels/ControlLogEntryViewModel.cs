using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.ViewModels;

public partial class ControlLogEntryViewModel : ObservableObject
{
    public ControlLogEntry LogEntry { get; private set; }

    public string Timestamp => LogEntry.Timestamp.ToString(@"h:mm tt");

    public string Corner => LogEntry.Corner;

    public string Car1 => LogEntry.Car1;

    public string Car2 => LogEntry.Car2;

    public string Note => LogEntry.Note;

    public string Status => LogEntry.Status;

    public string PenaltyAction => LogEntry.PenaltyAction;

    public string OtherNotes => LogEntry.OtherNotes;


    public ControlLogEntryViewModel(ControlLogEntry logEntry)
    {
        LogEntry = logEntry;
    }


    public void ApplyChanges(ControlLogEntry logEntry)
    {
        var old = LogEntry;
        this.LogEntry = logEntry;
        if (logEntry.Corner != old.Corner)
            OnPropertyChanged(nameof(Corner));
        if (logEntry.Car1 != old.Car1)
            OnPropertyChanged(nameof(Car1));
        if (logEntry.Car2 != old.Car2)
            OnPropertyChanged(nameof(Car2));
        if (logEntry.Status != old.Status)
            OnPropertyChanged(nameof(Status));
        if (logEntry.PenaltyAction != old.PenaltyAction)
            OnPropertyChanged(nameof(PenaltyAction));
        if (logEntry.OtherNotes != old.OtherNotes)
            OnPropertyChanged(nameof(OtherNotes));
    }
}
