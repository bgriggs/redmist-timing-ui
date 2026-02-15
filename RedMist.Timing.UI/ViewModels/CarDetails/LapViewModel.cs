using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.Timing.UI.Converters;
using RedMist.TimingCommon.Models;
using System;
using System.Globalization;

namespace RedMist.Timing.UI.ViewModels.CarDetails;

public partial class LapViewModel : ObservableObject
{
    private static readonly FlagToBrushConverter flagToBrushConverter = new();

    public CarPosition CarPosition { get; }
    public int LapNumber { get; }
    public int OverallPosition { get; }
    public int ClassPosition { get; }
    public string LapTime { get; }
    public DateTime LapTimeDt { get; }
    public string DriverName { get; }
    public string FlagStr { get; }
    public Flags Flag { get; }
    public string InPit { get; }
    public string RaceTime { get; }
    public IBrush FlagColor { get; }

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

    public string MinutesSinceLastPit { get; set; } = string.Empty;
    public string LapsSinceLastPit { get; set; } = string.Empty;

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

    public LapViewModel(CarPosition carPosition)
    {
        CarPosition = carPosition;
        LapNumber = carPosition.LastLapCompleted;
        OverallPosition = carPosition.OverallPosition;
        ClassPosition = carPosition.ClassPosition;
        DriverName = carPosition.DriverName;
        Flag = carPosition.TrackFlag;
        FlagStr = carPosition.TrackFlag != Flags.Unknown ? carPosition.TrackFlag.ToString() : string.Empty;
        InPit = carPosition.LapIncludedPit ? "YES" : string.Empty;

        // Parse lap time once
        if (DateTime.TryParseExact(carPosition.LastLapTime, "hh:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            LapTimeDt = dt;
            LapTime = dt.ToString("m:ss.fff");
        }
        else
        {
            LapTimeDt = default;
            LapTime = string.Empty;
        }

        // Parse race time once
        if (TryParseExtendedTime(carPosition.TotalTime, out var timeSpan))
        {
            RaceTime = $"{(int)timeSpan.TotalHours:0}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}";
        }
        else
        {
            RaceTime = string.Empty;
        }

        // Compute flag color once
        if (carPosition.TrackFlag != Flags.Unknown)
        {
            FlagColor = flagToBrushConverter.Convert(carPosition.TrackFlag, typeof(IBrush), null, CultureInfo.InvariantCulture) as IBrush ?? Brushes.Gray;
        }
        else
        {
            FlagColor = Brushes.Gray;
        }
    }

    /// <summary>
    /// Parses times greater than 24 hours in the format "HH:MM:SS.fff".
    /// </summary>
    private static bool TryParseExtendedTime(string? input, out TimeSpan result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var span = input.AsSpan();

        var firstColon = span.IndexOf(':');
        if (firstColon < 0)
            return false;

        var rest = span[(firstColon + 1)..];
        var secondColon = rest.IndexOf(':');
        if (secondColon < 0)
            return false;

        if (!int.TryParse(span[..firstColon], out int hours))
            return false;

        if (!int.TryParse(rest[..secondColon], out int minutes))
            return false;

        if (!double.TryParse(rest[(secondColon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
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
