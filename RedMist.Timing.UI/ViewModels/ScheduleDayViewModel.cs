using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.TimingCommon.Models.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels;

public partial class ScheduleDayViewModel : ObservableObject
{
    public string DayString { get; }
    public ObservableCollection<ScheduleEntryViewModel> EntryViewModels { get; } = [];


    public ScheduleDayViewModel(DateTime day, List<EventScheduleEntry> dayEntries)
    {
        var dtf = new CultureInfo("en-US", false).DateTimeFormat;
        dtf.DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
        DayString = dtf.GetDayName(day.DayOfWeek) + ", " + day.ToString("MM/dd");

        foreach (var entry in dayEntries.OrderBy(e => e.StartTime))
        {
            EntryViewModels.Add(new ScheduleEntryViewModel(entry));
        }
    }
}
