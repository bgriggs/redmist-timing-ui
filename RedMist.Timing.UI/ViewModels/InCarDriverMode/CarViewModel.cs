using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.TimingCommon.Models.InCarDriverMode;
using System;
using System.Globalization;

namespace RedMist.Timing.UI.ViewModels.InCarDriverMode;

public partial class CarViewModel : ObservableObject
{
    private const string GAIN_BACKGROUNDBRUSH = "carRowGainBackgroundBrush";
    private const string LOSS_BACKGROUNDBRUSH = "carRowLossBackgroundBrush";
    private const string OUTOFCLASS_BACKGROUNDBRUSH = "carRowOutOfClassBackgroundBrush";
    private const string DRIVER_BACKGROUNDBRUSH = "carRowDriverBackgroundBrush";

    [ObservableProperty]
    private string number = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TeamShortName))]
    private string teamName = string.Empty;
    public string TeamShortName
    {
        get
        {
            if (!string.IsNullOrEmpty(TeamName) && TeamName.Length > 18)
            {
                return string.Concat(TeamName.AsSpan(0, 16), "..");
            }
            return TeamName;
        }
    }

    [ObservableProperty]
    private string lastLapTime = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GainLossStr))]
    private string gainLoss = string.Empty;
    public string GainLossStr
    {
        get => GainLoss.StartsWith("-") ? "Loss:" : "Gain:";
    }
    [ObservableProperty]
    private string gap = string.Empty;
    [ObservableProperty]
    private bool showGainLoss = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDriverNameOrType))]
    [NotifyPropertyChangedFor(nameof(DriverNameShort))]
    private string driverName = string.Empty;
    public string DriverNameShort
    {
        get
        {
            if (!string.IsNullOrEmpty(DriverName) && DriverName.Length > 14)
            {
                return string.Concat(DriverName.AsSpan(0, 12), "..");
            }
            return DriverName;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDriverNameOrType))]
    [NotifyPropertyChangedFor(nameof(CarTypeShort))]
    private string carType = string.Empty;
    public string CarTypeShort
    {
        get
        {
            if (!string.IsNullOrEmpty(CarType) && CarType.Length > 14)
            {
                return string.Concat(CarType.AsSpan(0, 12), "..");
            }
            return CarType;
        }
    }

    public bool ShowDriverNameOrType => (!string.IsNullOrWhiteSpace(DriverName) || !string.IsNullOrEmpty(CarType)) && ShowNameAndType;
    public bool ShowNameAndType { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    private string rowBackgroundKey = GAIN_BACKGROUNDBRUSH;
    public IBrush RowBackground
    {
        get
        {
            if (!string.IsNullOrEmpty(rowBackgroundOverrideKey))
            {
                return (IBrush?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, rowBackgroundOverrideKey) ?? Brushes.Transparent;
            }
            return (IBrush?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, RowBackgroundKey) ?? Brushes.Transparent;
        }
    }
    private string rowBackgroundOverrideKey = string.Empty;

    public void Update(CarStatus? model)
    {
        if (model == null)
        {
            Number = string.Empty;
            TeamName = string.Empty;
            LastLapTime = string.Empty;
            GainLoss = string.Empty;
            Gap = string.Empty;
            DriverName = string.Empty;
            CarType = string.Empty;
            RowBackgroundKey = OUTOFCLASS_BACKGROUNDBRUSH;
            return;
        }

        Number = model.Number;
        CarType = model.CarType;
        TeamName = model.Team;
        LastLapTime = ToShortTime(model.LastLap);
        GainLoss = model.GainLoss;
        Gap = model.Gap;
        DriverName = model.Driver;

        if (GainLoss.StartsWith('-'))
        {
            RowBackgroundKey = LOSS_BACKGROUNDBRUSH;
        }
        else
        {
            RowBackgroundKey = GAIN_BACKGROUNDBRUSH;
        }
    }

    private string ToShortTime(string time)
    {
        if (DateTime.TryParseExact(time, "hh:mm:ss.fff", null, DateTimeStyles.None, out DateTime t))
        {
            return t.ToString("m:ss.fff");
        }
        return string.Empty;
    }

    public void SetAsDriver()
    {
        ShowGainLoss = false;
        ShowNameAndType = false;
        rowBackgroundOverrideKey = DRIVER_BACKGROUNDBRUSH;
    }

    public void SetAsOutOfClass()
    {
        rowBackgroundOverrideKey = OUTOFCLASS_BACKGROUNDBRUSH;
    }
}
