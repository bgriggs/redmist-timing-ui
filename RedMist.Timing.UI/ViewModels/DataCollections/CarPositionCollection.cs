using BigMission.Avalonia.Utilities;
using RedMist.TimingCommon.Models;
using System.Collections.Generic;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels.DataCollections;

public class CarPositionCollection
{
    public LargeObservableCollection<CarViewModel> Cars { get; }

    public string TotalLaps
    {
        get
        {
            if (Cars.Count > 0)
            {
               return Cars.Max(c => c.LastLap).ToString();
            }
            return string.Empty;
        }
    }


    public CarPositionCollection(LargeObservableCollection<CarViewModel> cars)
    {
        Cars = cars;
    }


    public void Sort()
    {
        if (Cars.Count < 2) return;
        Cars.Sort();
    }

    public void UpdateEntries(List<EventEntry> entries)
    {
        ApplyEntries(entries, true);
    }

    public void SetEntries(List<EventEntry> entries)
    {
        ApplyEntries(entries, false);
    }

    private void ApplyEntries(List<EventEntry> entries, bool isDeltaUpdate = false)
    {
        bool sortCars = false;
        foreach (var entry in entries)
        {
            var carVm = Cars.FirstOrDefault(c => c.Number == entry.Number);
            if (carVm == null && !isDeltaUpdate)
            {
                carVm = new CarViewModel();
                carVm.ApplyEntry(entry);
                Cars.Add(carVm);
                sortCars = true;
            }
            else
            {
                carVm?.ApplyEntry(entry);
            }
        }

        if (sortCars)
        {
            Sort();
        }

        if (!isDeltaUpdate)
        {
            // Remove cars not in entries
            foreach (var carVm in Cars.ToList())
            {
                if (!entries.Any(e => e.Number == carVm.Number))
                {
                    Cars.Remove(carVm);
                }
            }
        }
    }

    public void UpdateCarTiming(List<CarPosition> carPositions)
    {
        foreach (var carUpdate in carPositions)
        {
            var carVm = Cars.FirstOrDefault(c => c.Number == carUpdate.Number);
            if (carVm != null)
            {
                carVm.ApplyStatus(carUpdate, out var positionChanged);
                if (positionChanged)
                {
                    Sort();
                }
            }
        }
    }

}
