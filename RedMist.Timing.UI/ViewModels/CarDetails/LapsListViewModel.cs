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


    public LapsListViewModel()
    {
        lapCache.Connect()
            .AutoRefresh(t => t.LapNumber)
            .SortAndBind(Laps, SortExpressionComparer<LapViewModel>.Descending(t => t.LapNumber))
            .DisposeMany()
            .Subscribe();
    }


    public void UpdateLaps(List<CarPosition> carPositions)
    {
        foreach (var carUpdate in carPositions)
        {
            if (carUpdate.Number == null || carUpdate.LastLap <= 0)
                continue;
            lapCache.AddOrUpdate(new LapViewModel(carUpdate));
        }

        // Update best lap
        var bestLap = lapCache.Items.Where(l => l.LapTimeDt.TimeOfDay > TimeSpan.Zero).OrderBy(l => l.LapTimeDt).FirstOrDefault();
        if (bestLap != null)
        {
            foreach (var lap in lapCache.Items)
            {
                if (lap == bestLap)
                    continue;
                lap.IsBestLap = false;
            }
            bestLap.IsBestLap = true;
        }

        // Update gained/lost position
        for (int i = 0; i < lapCache.Items.Count; i++)
        {
            var lap = lapCache.Items[i];
            if (i == 0)
            {
                lap.GainedOverallPosition = false;
                lap.LostOverallPosition = false;
                lap.GainedClassPosition = false;
                lap.LostClassPosition = false;
            }
            else
            {
                lap.GainedOverallPosition = lapCache.Items[i - 1].OverallPosition > lap.OverallPosition;
                lap.LostOverallPosition = lapCache.Items[i - 1].OverallPosition < lap.OverallPosition;
                lap.GainedClassPosition = lapCache.Items[i - 1].ClassPosition > lap.ClassPosition;
                lap.LostClassPosition = lapCache.Items[i - 1].ClassPosition < lap.ClassPosition;
            }
        }
    }
}
