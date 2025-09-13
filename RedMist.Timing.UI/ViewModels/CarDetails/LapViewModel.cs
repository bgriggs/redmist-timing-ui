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
    public CarPosition CarPosition => carPosition;

    public int LapNumber => carPosition.LastLapCompleted;
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
    public string FlagStr => carPosition.TrackFlag != Flags.Unknown ? carPosition.TrackFlag.ToString() : string.Empty;
    public Flags Flag => carPosition.TrackFlag;
    public string InPit => carPosition.LapIncludedPit ? "YES" : string.Empty;

    public string MinutesSinceLastPit { get; set; } = string.Empty;
    public string LapsSinceLastPit { get; set; } = string.Empty;

    private DateTime? lapTimeDt;
    public DateTime LapTimeDt
    {
        get
        {
            if (lapTimeDt == null)
            {
                DateTime.TryParseExact(carPosition.LastLapTime, "hh:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);
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
            if (TryParseExtendedTime(carPosition.TotalTime, out var timeSpan))
            {
                return $"{(int)timeSpan.TotalHours:0}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
            }
            return string.Empty;
        }
    }

    public IBrush FlagColor
    {
        get
        {
            if (carPosition.TrackFlag != Flags.Unknown)
            {
                return flagToBrushConverter.Convert(carPosition.TrackFlag, typeof(IBrush), null, CultureInfo.InvariantCulture) as IBrush ?? Brushes.Gray;
            }
            return Brushes.Gray;
        }
    }

    /// <summary>
    /// Parses times greater than 24 hours in the format "HH:MM:SS.fff".
    /// </summary>
    /// <param name="input"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    private static bool TryParseExtendedTime(string? input, out TimeSpan result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var parts = input.Split(':');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out int hours))
            return false;

        if (!int.TryParse(parts[1], out int minutes))
            return false;

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            return false;

        int wholeSeconds = (int)Math.Floor(seconds);
        int milliseconds = (int)Math.Round((seconds - wholeSeconds) * 1000);

        try
        {
            result = new TimeSpan(0, hours, minutes, wholeSeconds, milliseconds);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
