﻿using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
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

    public static readonly IBrush carNormalLapColor = Brushes.Black;
    public static readonly IBrush carBestLapColor = Brush.Parse("#5da639");
    public static readonly IBrush carOverallBestLapColor = Brushes.Purple;

    #endregion

    #region Car Properties
    private CarPosition? lastCarPosition;

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
    [NotifyPropertyChangedFor(nameof(PositionsGainedLost))]
    private int overallPositionsGained;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionsGainedLost))]
    private int inClassPositionsGained;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMostPositionsGained))]
    private bool isOverallMostPositionsGained;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMostPositionsGained))]
    private bool isClassMostPositionsGained;
    [ObservableProperty]
    private int penalityLaps;
    [ObservableProperty]
    private int penalityWarnings;
    [ObservableProperty]
    private bool isStale;
    [ObservableProperty]
    private PitStates pitState;

    #endregion

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

    public int PositionsGainedLost
    {
        get
        {
            if (CurrentGroupMode == GroupMode.Overall)
            {
                return OverallPositionsGained;
            }
            else
            {
                return InClassPositionsGained;
            }
        }
    }

    public bool IsMostPositionsGained
    {
        get
        {
            if (CurrentGroupMode == GroupMode.Overall)
            {
                return IsOverallMostPositionsGained;
            }
            else
            {
                return IsClassMostPositionsGained;
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
                OnPropertyChanged(nameof(PositionsGainedLost));
                OnPropertyChanged(nameof(IsMostPositionsGained));
            }
        }
    }

    #region Car Details

    private bool isDetailsExpanded = false;
    public bool IsDetailsExpanded
    {
        get { return isDetailsExpanded; }
        set
        {
            if (value != isDetailsExpanded)
            {
                isDetailsExpanded = value;
                OnPropertyChanged(nameof(IsDetailsExpanded));
                UpdateCarDetails(value);
            }
        }
    }

    private readonly int eventId;
    private readonly EventClient serverClient;
    private readonly HubClient hubClient;
    private readonly PitTracking pitTracking;
    [ObservableProperty]
    private DetailsViewModel? carDetailsViewModel;

    #endregion


    public CarViewModel(int eventId, EventClient serverClient, HubClient hubClient, PitTracking pitTracking)
    {
        this.eventId = eventId;
        this.serverClient = serverClient;
        this.hubClient = hubClient;
        this.pitTracking = pitTracking;
    }


    public void ApplyStatus(CarPosition carPosition)
    {
        var prevLap = LastLap;
        var prevPos = OverallPosition;

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
        IsStale = carPosition.IsStale;

        // Pit state
        if (carPosition.IsEnteredPit)
            PitState = PitStates.EnteredPit;
        else if (carPosition.IsPitStartFinish)
            PitState = PitStates.PitSF;
        else if (carPosition.IsExitedPit)
            PitState = PitStates.ExitedPit;
        else if (carPosition.IsInPit)
            PitState = PitStates.InPit;
        else
            PitState = PitStates.None;

        // Record the pit stop
        if (carPosition.IsInPit && !string.IsNullOrEmpty(carPosition.Number))
            pitTracking.AddPitStop(carPosition.Number, carPosition.LastLap);

        // Change to stale color to show car has not updated in a while
        if (IsStale)
        {
            RowBackground = rowStaleColor;
        }
        else if (RowBackground == rowStaleColor)
        {
            RowBackground = rowNormalColor;
        }

        // Flash the row background if the lap has changed
        if (prevLap != LastLap && RowBackground != rowUpdateColor)
        {
            Observable.Timer(TimeSpan.FromMilliseconds(80)).Subscribe(_ => Dispatcher.UIThread.Post(() => RowBackground = rowUpdateColor));
            Observable.Timer(TimeSpan.FromSeconds(0.9)).Subscribe(_ => Dispatcher.UIThread.Post(() => RowBackground = rowNormalColor));
        }

        if (LastLap > 0)
        {
            // Check for best lap overall/in-class and car's fastest lap
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
            else // Reset to normal
            {
                LapDataColor = carNormalLapColor;
                LapDataFontWeight = FontWeight.Normal;
            }
        }
        else // Reset to normal
        {
            LapDataColor = carNormalLapColor;
            LapDataFontWeight = FontWeight.Normal;
        }

        CarDetailsViewModel?.UpdateLaps([carPosition]);
        lastCarPosition = carPosition;
    }

    public void ApplyEntry(EventEntry entry)
    {
        Number = entry.Number;
        var n = entry.Name.ToUpperInvariant();
        if (n.Length > 21)
            n = n[..21];
        Name = n;
        Team = entry.Team;
        Class = entry.Class;

        // Force reset once loaded - prevents color from getting stuck on update color
        Observable.Timer(TimeSpan.FromSeconds(1.5)).Subscribe(_ => Dispatcher.UIThread.Post(() => RowBackground = rowNormalColor, DispatcherPriority.Send));
    }

    private void UpdateCarDetails(bool isEnabled)
    {
        if (isEnabled && CarDetailsViewModel == null)
        {
            CarDetailsViewModel = new DetailsViewModel(eventId, Number, serverClient, hubClient, pitTracking);
            _ = CarDetailsViewModel.Initialize();
        }
        else if (!isEnabled && CarDetailsViewModel != null)
        {
            CarDetailsViewModel.Dispose();
            CarDetailsViewModel = null;
        }
    }
}
