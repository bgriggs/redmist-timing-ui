using RedMist.TimingCommon.Models;
using System.Collections.Generic;

namespace RedMist.Timing.UI.Services;

public class PitTracking
{
    private readonly Dictionary<string, HashSet<int>> pitStops = [];

    public void AddPitStop(string carNumber, int lap)
    {
        if (!pitStops.TryGetValue(carNumber, out HashSet<int>? value))
        {
            value = [];
            pitStops[carNumber] = value;
        }

        value.Add(lap);
    }

    public void ApplyPitStop(List<CarPosition> carPositions)
    {
        foreach (var carPosition in carPositions)
        {
            if (!string.IsNullOrEmpty(carPosition.Number) && pitStops.TryGetValue(carPosition.Number, out HashSet<int>? ps))
            {
                if (ps.Contains(carPosition.LastLap))
                {
                    carPosition.LapIncludedPit = true;
                }
            }
        }
    }

    public void Clear()
    {
        pitStops.Clear();
    }
}
