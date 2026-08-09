using RedMist.TimingCommon.Models;
using System.Collections.Generic;
using System.Threading;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Tracks which laps a car pitted on, so lap detail views can flag them.
/// </summary>
/// <remarks>
/// Access is synchronized because the instance is shared across threads: pit stops are recorded
/// and read from the UI thread while <see cref="Clear"/> is called from the background task that
/// initializes a live event. An unsynchronized <see cref="Dictionary{TKey, TValue}"/> torn by
/// concurrent writes can spin forever inside a lookup or fault inside the runtime.
/// </remarks>
public class PitTracking
{
    private readonly Dictionary<string, HashSet<int>> pitStops = [];
    private readonly Lock gate = new();

    public void AddPitStop(string carNumber, int lap)
    {
        lock (gate)
        {
            if (!pitStops.TryGetValue(carNumber, out HashSet<int>? value))
            {
                value = [];
                pitStops[carNumber] = value;
            }

            value.Add(lap);
        }
    }

    public void ApplyPitStop(List<CarPosition> carPositions)
    {
        lock (gate)
        {
            foreach (var carPosition in carPositions)
            {
                if (!string.IsNullOrEmpty(carPosition.Number) && pitStops.TryGetValue(carPosition.Number, out HashSet<int>? ps))
                {
                    if (ps.Contains(carPosition.LastLapCompleted))
                    {
                        carPosition.LapIncludedPit = true;
                    }
                }
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            pitStops.Clear();
        }
    }
}
