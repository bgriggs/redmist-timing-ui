using DynamicData;
using DynamicData.Binding;
using RedMist.TimingCommon.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels.CarDetails;

public class LapsListViewModel
{
    public ObservableCollection<LapViewModel> Laps { get; } = [];
    protected readonly SourceCache<LapViewModel, int> lapCache = new(lap => lap.LapNumber);
    private LapViewModel? currentBestLap;


    public LapsListViewModel()
    {
        lapCache.Connect()
            .SortAndBind(Laps, SortExpressionComparer<LapViewModel>.Descending(t => t.LapNumber))
            .DisposeMany()
            .Subscribe();
    }


    public void UpdateLaps(List<CarPosition> carPositions)
    {
        // Track which lap numbers are being added/updated for targeted position gain/loss recalculation
        int minAffectedLap = int.MaxValue;

        foreach (var carUpdate in carPositions)
        {
            if (carUpdate.Number == null || carUpdate.LastLapCompleted <= 0)
                continue;

            if (carUpdate.LastLapCompleted < minAffectedLap)
                minAffectedLap = carUpdate.LastLapCompleted;

            lapCache.AddOrUpdate(new LapViewModel(carUpdate));
        }

        if (minAffectedLap == int.MaxValue)
            return;

        // Update best lap — only change IsBestLap when the best lap actually changes
        var bestLap = lapCache.Items
            .Where(l => l.LapTimeDt.TimeOfDay > TimeSpan.Zero)
            .MinBy(l => l.LapTimeDt);

        if (bestLap != null && bestLap != currentBestLap)
        {
            if (currentBestLap != null)
                currentBestLap.IsBestLap = false;
            bestLap.IsBestLap = true;
            currentBestLap = bestLap;
        }

        // Update gained/lost position only for affected laps and their successor
        var laps = lapCache.Items.OrderBy(l => l.LapNumber).ToArray();
        int startIndex = 0;
        if (minAffectedLap > 1)
        {
            // Find the index of the lap just before the first affected lap
            for (int i = 0; i < laps.Length; i++)
            {
                if (laps[i].LapNumber >= minAffectedLap - 1)
                {
                    startIndex = i;
                    break;
                }
            }
        }

        for (int i = startIndex; i < laps.Length; i++)
        {
            var lap = laps[i];
            if (i == 0)
            {
                lap.GainedOverallPosition = false;
                lap.LostOverallPosition = false;
                lap.GainedClassPosition = false;
                lap.LostClassPosition = false;
            }
            else
            {
                var prev = laps[i - 1];
                lap.GainedOverallPosition = prev.OverallPosition > lap.OverallPosition;
                lap.LostOverallPosition = prev.OverallPosition < lap.OverallPosition;
                lap.GainedClassPosition = prev.ClassPosition > lap.ClassPosition;
                lap.LostClassPosition = prev.ClassPosition < lap.ClassPosition;
            }
        }
    }
}
