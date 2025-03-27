using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.TimingCommon.Models;
using System;
using System.Globalization;

namespace RedMist.Timing.UI.ViewModels.CarDetails;

public partial class LapViewModel(CarPosition carPosition) : ObservableObject
{
    private readonly CarPosition carPosition = carPosition;

    public int LapNumber => carPosition.LastLap;
    public int OverallPosition => carPosition.OverallPosition;
    public int ClassPosition => carPosition.ClassPosition;
    public string LapTime
    {
        get
        {
            if (LapTimeDt != default)
            {
                return LapTimeDt.ToString("m:ss.fff");
            }
            return string.Empty;
        }
    }
    public bool GainedPosition { get; set; }
    public bool LostPosition { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeColor))]
    [NotifyPropertyChangedFor(nameof(TimeFontWeight))]
    private bool isBestLap;
    public string Flag => carPosition.Flag != Flags.Unknown ? carPosition.Flag.ToString() : string.Empty;
    public string InPit => carPosition.LapIncludedPit ? "YES" : string.Empty;

    private DateTime? lapTimeDt;
    public DateTime LapTimeDt
    {
        get
        {
            if (lapTimeDt == null)
            {
                DateTime.TryParseExact(carPosition.LastTime, "hh:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);
                lapTimeDt = dt;
            }
            return lapTimeDt ?? default;
        }
    }

    public IBrush TimeColor
    {
        get
        {
            if (IsBestLap)
                return CarViewModel.carBestLapColor;
            return CarViewModel.carNormalLapColor;
        }
    }

    public FontWeight TimeFontWeight
    {
        get
        {
            if (IsBestLap)
                return FontWeight.Bold;
            return FontWeight.Normal;
        }
    }
}
