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
        var bestLap = lapCache.Items.Where(l => l.LapTimeDt > DateTime.MinValue).OrderBy(l => l.LapTimeDt).FirstOrDefault();
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
    }
}
