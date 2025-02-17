using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.TimingCommon.Models;
using System;
using System.Reactive.Linq;

namespace RedMist.Timing.UI.ViewModels;

public partial class CarViewModel : ObservableObject
{
    #region Colors

    private static readonly Color rowNormalColor = Colors.Transparent;
    private static readonly Color rowUpdateColor = Color.Parse("#fce3cf");
    private static readonly Color rowStaleColor = Color.Parse("#f7d2bb");

    private static readonly IBrush carNormalLapColor = Brushes.Black;
    private static readonly IBrush carBestLapColor = Brush.Parse("#5da639");
    private static readonly IBrush carOverallBestLapColor = Brushes.Purple;

    #endregion

    [ObservableProperty]
    private string number = string.Empty;
    [ObservableProperty]
    private string name = string.Empty;
    [ObservableProperty]
    private string team = string.Empty;
    [ObservableProperty]
    private string _class = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BestTimeShort))]
    private string? bestTime;
    public string BestTimeShort
    {
        get
        {
            if (DateTime.TryParseExact(BestTime, "hh:mm:ss.fff", null, System.Globalization.DateTimeStyles.None, out DateTime time))
            {
                return time.ToString("m:ss.fff");
            }
            return string.Empty;
        }
    }

    [ObservableProperty]
    private int bestLap;
    [ObservableProperty]
    private bool isBestTimeOverall;
    [ObservableProperty]
    private bool isBestTimeClass;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gap))]
    private string overallGap = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Difference))]
    private string overallDifference = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Gap))]
    private string inClassGap = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Difference))]
    private string inClassDifference = string.Empty;
    [ObservableProperty]
    private string totalTime = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastTimeShort))]
    private string lastTime = string.Empty;
    public string LastTimeShort
    {
        get
        {
            if (DateTime.TryParseExact(LastTime, "hh:mm:ss.fff", null, System.Globalization.DateTimeStyles.None, out DateTime time))
            {
                return time.ToString("m:ss.fff");
            }
            return string.Empty;
        }
    }

    [ObservableProperty]
    private int lastLap;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Position))]
    private int overallPosition;
    [ObservableProperty]
    private int classPosition;
    [ObservableProperty]
    private int overallPositionsGained;
    [ObservableProperty]
    private int inClassPositionsGained;
    [ObservableProperty]
    private bool isOverallMostPositionsGained;
    [ObservableProperty]
    private bool isClassMostPositionsGained;
    [ObservableProperty]
    private int penalityLaps;
    [ObservableProperty]
    private int penalityWarnings;
    [ObservableProperty]
    private bool isEnteredPit;
    [ObservableProperty]
    private bool isExistedPit;
    [ObservableProperty]
    private bool isInPit;
    [ObservableProperty]
    private bool isStale;

    /// <summary>
    /// Used when sorting cars to put the car with no position at the end of the list.
    /// </summary>
    public int SortablePosition
    {
        get
        {
            if (OverallPosition == 0)
            {
                return 9999;
            }
            return OverallPosition;
        }
    }

    public int Position
    {
        get
        {
            if (CurrentGroupMode == GroupMode.Overall)
            {
                return OverallPosition;
            }
            else
            {
                return ClassPosition;
            }
        }
    }

    public string Gap
    {
        get
        {
            if (CurrentGroupMode == GroupMode.Overall)
            {
                return OverallGap;
            }
            else
            {
                return InClassGap;
            }
        }
    }

    public string Difference
    {
        get
        {
            if (CurrentGroupMode == GroupMode.Overall)
            {
                return OverallDifference;
            }
            else
            {
                return InClassDifference;
            }
        }
    }

    public bool IsBestTime
    {
        get
        {
            if (CurrentGroupMode == GroupMode.Overall)
            {
                return IsBestTimeOverall;
            }
            else
            {
                return IsBestTimeClass;
            }
        }
    }

    private Color _rowBackground = rowNormalColor;
    public Color RowBackground
    {
        get => _rowBackground;
        set
        {
            SetProperty(ref _rowBackground, value);
        }
    }

    [ObservableProperty]
    private IBrush lapDataColor = carNormalLapColor;
    [ObservableProperty]
    private FontWeight lapDataFontWeight = FontWeight.Normal;

    private GroupMode currentGroupMode = GroupMode.Overall;
    public GroupMode CurrentGroupMode
    {
        get { return currentGroupMode; }
        set
        {
            if (value != currentGroupMode)
            {
                currentGroupMode = value;
                OnPropertyChanged(nameof(Position));
                OnPropertyChanged(nameof(Gap));
                OnPropertyChanged(nameof(Difference));
                OnPropertyChanged(nameof(IsBestTime));
            }
        }
    }

    //private bool updateChanged = false;


    public void ApplyStatus(CarPosition carPosition, out bool positionChanged)
    {
        //PropertyChanged += CarViewModel_PropertyChanged;
        var prevLap = LastLap;
        var prevPos = OverallPosition;
        positionChanged = false;

        Number = carPosition.Number ?? string.Empty;
        BestTime = carPosition.BestTime ?? string.Empty;
        BestLap = carPosition.BestLap;
        IsBestTimeOverall = carPosition.IsBestTime;
        IsBestTimeClass = carPosition.IsBestTimeClass;
        OverallGap = carPosition.OverallGap ?? string.Empty;
        OverallDifference = carPosition.OverallDifference ?? string.Empty;
        InClassGap = carPosition.InClassGap ?? string.Empty;
        InClassDifference = carPosition.InClassDifference ?? string.Empty;
        TotalTime = carPosition.TotalTime ?? string.Empty;
        LastTime = carPosition.LastTime ?? string.Empty;
        LastLap = carPosition.LastLap;
        OverallPosition = carPosition.OverallPosition;
        ClassPosition = carPosition.ClassPosition;
        OverallPositionsGained = carPosition.OverallPositionsGained;
        InClassPositionsGained = carPosition.InClassPositionsGained;
        IsOverallMostPositionsGained = carPosition.IsOverallMostPositionsGained;
        IsClassMostPositionsGained = carPosition.IsClassMostPositionsGained;
        PenalityLaps = carPosition.PenalityLaps;
        PenalityWarnings = carPosition.PenalityWarnings;
        IsEnteredPit = carPosition.IsEnteredPit;
        IsExistedPit = carPosition.IsExistedPit;
        IsInPit = carPosition.IsInPit;
        IsStale = carPosition.IsStale;

        // Change to stale color to show car has not updated in a while
        if (IsStale)
        {
            RowBackground = rowStaleColor;
        }
        else if (RowBackground == rowStaleColor)
        {
            RowBackground = rowNormalColor;
        }

        // Flash the background if the lap has changed
        if (prevLap != LastLap && RowBackground != rowUpdateColor)
        {
            Observable.Timer(TimeSpan.FromMilliseconds(80)).Subscribe(_ => RowBackground = rowUpdateColor);
            Observable.Timer(TimeSpan.FromSeconds(0.9)).Subscribe(_ => RowBackground = rowNormalColor);
        }

        if (LastLap > 0)
        {
            if (IsBestTime)
            {
                LapDataColor = carOverallBestLapColor;
                LapDataFontWeight = FontWeight.Bold;
            }
            else if (BestLap == LastLap)
            {
                LapDataColor = carBestLapColor;
                LapDataFontWeight = FontWeight.Bold;
            }
            else
            {
                LapDataColor = carNormalLapColor;
                LapDataFontWeight = FontWeight.Normal;
            }
        }
        else
        {
            LapDataColor = carNormalLapColor;
            LapDataFontWeight = FontWeight.Normal;
        }

        if (prevPos != OverallPosition)
        {
            positionChanged = true;
        }

        //PropertyChanged -= CarViewModel_PropertyChanged;
        //var changed = updateChanged;
        //updateChanged = false;
        //return changed;
    }

    public void ApplyEntry(EventEntry entry)
    {
        //PropertyChanged += CarViewModel_PropertyChanged;

        Number = entry.Number;
        Name = entry.Name.ToUpperInvariant();
        Team = entry.Team;
        Class = entry.Class;

        //PropertyChanged -= CarViewModel_PropertyChanged;
        //var changed = updateChanged;
        //updateChanged = false;
        //return changed;
    }

    //private void CarViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    //{
    //    updateChanged = true;
    //}
}
