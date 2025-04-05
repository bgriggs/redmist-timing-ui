using DynamicData;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using System;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignLiveTimingViewModel : LiveTimingViewModel
{
    public DesignLiveTimingViewModel() : base(new HubClient(new DebugLoggerFactory(), new DesignConfiguration()), new DesignEventClient(new DesignConfiguration()), new DebugLoggerFactory(), new ViewSizeService())
    {
        var pitTracking = new PitTracking();
        var viewSizeService = new ViewSizeService();
        var ec = new DesignEventClient(new DesignConfiguration());
        var hc = new DesignHubClient();
        EventName = "Design Event";
        Flag = "Green";
        TimeToGo = "03:45:00";
        RaceTime = "01:00:00";
        LocalTime = "9:14:33 am";

        carCache.AddOrUpdate(new CarViewModel(1, ec, hc, pitTracking, viewSizeService)
        {
            Number = "34",
            OriginalName = "Team Awesome 1",
            OverallPosition = 13,
            LastLap = 33,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            Class = "GP3",
            PitState = PitStates.ExitedPit
        });

        carCache.AddOrUpdate(new CarViewModel(1, ec, hc, pitTracking, viewSizeService)
        {
            Number = "14",
            OriginalName = "Team Awesome 2",
            OverallPosition = 11,
            LastLap = 33,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            Class = "GP3",
        });

        carCache.AddOrUpdate(new CarViewModel(1, ec, hc, pitTracking, viewSizeService)
        {
            Number = "12",
            OriginalName = "Team Awesome Really Long Team Name 12345678",
            OverallPosition = 2,
            LastLap = 33,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            PenaltyWarnings = 1,
            PenaltyLaps = 2,
            Class = "GP3",
        });

        carCache.AddOrUpdate(new CarViewModel(1, ec, hc, pitTracking, viewSizeService)
        {
            Number = "1x",
            OriginalName = "Team Stale",
            OverallPosition = 26,
            LastLap = 1,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            Class = "GP1",
            IsStale = false,
        });

        carCache.Lookup("1x").Value.ApplyStatus(new CarPosition
        {
            Number = "1x",
            OverallPosition = 26,
            LastLap = 1,
            LastTime = "00:02:46.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            OverallPositionsGained = -5,
            IsStale = true,
            IsPitStartFinish = true,
        });

        carCache.AddOrUpdate(new CarViewModel(1, ec, hc, pitTracking, viewSizeService)
        {
            Number = "111",
            OriginalName = "Team Cars Best Time",
            OverallPosition = 25,
            LastLap = 2,
            LastTime = "00:02:17.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            Class = "GP1",
            PenaltyLaps = 1,
        });

        carCache.Lookup("111").Value.ApplyStatus(new CarPosition
        {
            Number = "111",
            OverallPosition = 25,
            LastLap = 2,
            LastTime = "00:02:17.872",
            BestLap = 2,
            BestTime = "00:02:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            IsEnteredPit = true,
        });

        carCache.AddOrUpdate(new CarViewModel(1, ec, hc, pitTracking, viewSizeService)
        {
            Number = "222",
            OriginalName = "Team Overall Best Time",
            OverallPosition = 24,
            LastLap = 3,
            LastTime = "00:02:17.872",
            BestLap = 2,
            BestTime = "00:01:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            Class = "GP1",
        });

        carCache.Lookup("222").Value.CarDetailsViewModel = new DetailsViewModel(1, "222", new DesignEventClient(new DesignConfiguration()), hc, pitTracking);
        carCache.Lookup("222").Value.ApplyStatus(new CarPosition
        {
            Number = "222",
            OverallPosition = 24,
            LastLap = 3,
            LastTime = "00:02:17.872",
            BestLap = 2,
            BestTime = "00:01:17.872",
            OverallGap = "00:02.872",
            OverallDifference = "00:12.872",
            OverallPositionsGained = 5,
            IsOverallMostPositionsGained = true,
            IsBestTime = true,
            IsInPit = true,
            TotalTime = "12:17:12.872",
            Flag = Flags.Green,
        });
        carCache.Lookup("222").Value?.CarDetailsViewModel?.ControlLog.Add(new ControlLogEntryViewModel(
            new ControlLogEntry
            {
                OrderId = 1,
                Timestamp = new DateTime(2025, 2, 28, 13, 1, 1),
                Corner = "2",
                Car1 = "222",
                Car2 = "123",
                Status = "In progress",
                Note = "off track",
                OtherNotes = "Continued"
            }));

        carCache.Lookup("222").Value?.CarDetailsViewModel?.Receive(new Models.CompetitorMetadataNotification(new CompetitorMetadata 
        { 
            FirstName = "Bob",
            LastName = "Smith",
            NationState = "USA",
            Sponsor = "Awesome Company",
            CarNumber = "222",
            Class = "GP1",
            Transponder = 123456789,
            Make = "Toyota",
            ModelEngine = "Corolla",
            //Club = "Awesome Club",
            Tires = "Super Soft",
        }));
        //ToggleGroupMode();
    }
}
