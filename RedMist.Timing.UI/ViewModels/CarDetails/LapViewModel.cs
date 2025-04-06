using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.Timing.UI.Converters;
using RedMist.TimingCommon.Models;
using System;
using System.Globalization;

namespace RedMist.Timing.UI.ViewModels.CarDetails;

public partial class LapViewModel(CarPosition carPosition) : ObservableObject
{
    private readonly CarPosition carPosition = carPosition;
    private static readonly FlagToBrushConverter flagToBrushConverter = new();

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

    [ObservableProperty]
    private bool gainedOverallPosition;
    [ObservableProperty]
    private bool lostOverallPosition;
    [ObservableProperty]
    private bool gainedClassPosition;
    [ObservableProperty]
    private bool lostClassPosition;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeColor))]
    [NotifyPropertyChangedFor(nameof(TimeFontWeight))]
    private bool isBestLap;
    public string FlagStr => carPosition.Flag != Flags.Unknown ? carPosition.Flag.ToString() : string.Empty;
    public Flags Flag => carPosition.Flag;
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
                return (IBrush?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, CarViewModel.CARROWLAPTEXTFOREGROUND_BEST_BRUSH) ?? Brushes.Black;
            return (IBrush?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, CarViewModel.CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH) ?? Brushes.Black;
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

    public string RaceTime
    {
        get
        {
            if (carPosition.TotalTime != null)
            {
                DateTime.TryParseExact(carPosition.TotalTime, "hh:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);
                return dt.ToString("H:mm:ss");
            }
            return string.Empty;
        }
    }

    public IBrush FlagColor
    {
        get
        {
            if (carPosition.Flag != Flags.Unknown)
            {
                return flagToBrushConverter.Convert(carPosition.Flag, typeof(IBrush), null, CultureInfo.InvariantCulture) as IBrush ?? Brushes.Gray;
            }
            return Brushes.Gray;
        }
    }
}
