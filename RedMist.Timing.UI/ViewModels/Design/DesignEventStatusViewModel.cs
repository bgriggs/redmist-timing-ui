﻿using RedMist.Timing.UI.Clients;
using RedMist.TimingCommon.Models;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels.Design;

public partial class DesignEventStatusViewModel : EventStatusViewModel
{
    public DesignEventStatusViewModel() : base(new HubClient(new DebugLoggerFactory(), new DesignConfiguration()), new DebugLoggerFactory())
    {
        EventName = "Design Event";
        Flag = "Green";
        TimeToGo = "03:45:00";
        TotalTime = "01:00:00";

        Cars.Add(new CarViewModel
        {
            Number = "34",
            Name = "Team Awesome 1",
            OverallPosition = 13,
            LastLap = 33,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
            Class = "GP3",
        });

        Cars.Add(new CarViewModel
        {
            Number = "14",
            Name = "Team Awesome 2",
            OverallPosition = 11,
            LastLap = 33,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
            Class = "GP3",
        });

        Cars.Add(new CarViewModel
        {
            Number = "12",
            Name = "Team Awesome 3",
            OverallPosition = 2,
            LastLap = 33,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
            Class = "GP3",
        });

        Cars.Add(new CarViewModel
        {
            Number = "1x",
            Name = "Team Stale",
            OverallPosition = 26,
            LastLap = 1,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
            Class = "GP1",
            IsStale = false,
        });

        Cars.First(c => c.Number == "1x").ApplyStatus(new CarPosition
        {
            Number = "1x",
            OverallPosition = 26,
            LastLap = 1,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
            OverallPositionsGained = -5,
            IsStale = true,
        }, out var _);

        Cars.Add(new CarViewModel
        {
            Number = "111",
            Name = "Team Cars Best Time",
            OverallPosition = 25,
            LastLap = 2,
            LastTime = "00:02:17.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
            Class = "GP1",
        });

        Cars.First(c => c.Number == "111").ApplyStatus(new CarPosition
        {
            Number = "111",
            OverallPosition = 25,
            LastLap = 2,
            LastTime = "00:02:17.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
        }, out var _);

        Cars.Add(new CarViewModel
        {
            Number = "222",
            Name = "Team Overall Best Time",
            OverallPosition = 24,
            LastLap = 3,
            LastTime = "00:02:17.872",
            BestLap = 2,
            BestTime = "00:01:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
            Class = "GP1",
        });

        Cars.First(c => c.Number == "222").ApplyStatus(new CarPosition
        {
            Number = "222",
            OverallPosition = 24,
            LastLap = 3,
            LastTime = "00:02:17.872",
            BestLap = 2,
            BestTime = "00:01:17.872",
            OverallGap = "00:00:02.872",
            OverallDifference = "00:00:12.872",
            OverallPositionsGained = 5,
            IsOverallMostPositionsGained = true,
            IsBestTime = true,
        }, out var _);
    }
}
