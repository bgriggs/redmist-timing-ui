using RedMist.Timing.UI.Models;
using RedMist.TimingCommon.Models;
using System;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignFlagsViewModel : FlagsViewModel
{
    public DesignFlagsViewModel() : base(new Event { EventName = "World Racing League - Barber 2025" }, new DesignEventClient(new DesignConfiguration()), new EventContext())
    {
        var fg1 = new FlagDuration 
        { 
            Flag = TimingCommon.Models.Flags.Green, 
            StartTime = new DateTime(2025, 3, 30, 9, 0, 0),
            EndTime = new DateTime(2025, 3, 30, 9, 30, 0)
        };
        var fg2 = new FlagDuration
        {
            Flag = TimingCommon.Models.Flags.Yellow,
            StartTime = new DateTime(2025, 3, 30, 9, 30, 1),
            EndTime = new DateTime(2025, 3, 30, 9, 45, 0)
        };

        var fg3 = new FlagDuration
        {
            Flag = TimingCommon.Models.Flags.Red,
            StartTime = new DateTime(2025, 3, 30, 9, 45, 1),
            EndTime = new DateTime(2025, 3, 30, 10, 0, 0)
        };

        var fg4 = new FlagDuration
        {
            Flag = TimingCommon.Models.Flags.Yellow,
            StartTime = new DateTime(2025, 3, 30, 10, 0, 1),
            EndTime = new DateTime(2025, 3, 30, 10, 30, 0)
        };
        var fg5 = new FlagDuration
        {
            Flag = TimingCommon.Models.Flags.Green,
            StartTime = new DateTime(2025, 3, 30, 10, 30, 1),
            //EndTime = new DateTime(2025, 3, 30, 11, 30, 33)
        };

        var p = new SessionStatePatch { FlagDurations = [fg1, fg2, fg3, fg4, fg5] };
        var sn = new SessionStatusNotification(p);
        Receive(sn);
    }
}
