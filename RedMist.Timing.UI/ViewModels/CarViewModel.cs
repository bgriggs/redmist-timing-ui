using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Models;
using System;
using System.Globalization;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

public partial class CarViewModel : ObservableObject, IRecipient<SizeChangedNotification>
{
    #region Colors

    private const string CARROW_NORMAL_BACKGROUNDBRUSH = "carRowNormalBackgroundBrush";
    private const string CARROW_UPDATED_BACKGROUNDBRUSH = "carRowUpdatedBackgroundBrush";
    private const string CARROW_STALE_BACKGROUNDBRUSH = "carRowStaleBackgroundBrush";

    public const string CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH = "carRowLapTextForegroundNormalBrush";
    public const string CARROWLAPTEXTFOREGROUND_BEST_BRUSH = "carRowLapTextForegroundBestBrush";
    public const string CARROWLAPTEXTFOREGROUND_OVERALLBEST_BRUSH = "carRowLapTextForegroundOverallBestBrush";

    #endregion

    #region Car Properties

    public CarPosition? LastCarPosition { get; private set; }

    [ObservableProperty]
    private string number = string.Empty;

    public string Name
    {
        get
        {
            var upper = OriginalName.ToUpperInvariant();
            var size = viewSizeService.CurrentSize;
            if (size.Width < 400)
            {
                return upper[..Math.Min(upper.Length, 18)];
            }
            else if (size.Width < 525)
            {
                return upper[..Math.Min(upper.Length, 28)];
            }
            else if (size.Width < 600)
            {
                return upper[..Math.Min(upper.Length, 35)];
            }
            else if (size.Width < 700)
            {
                return upper[..Math.Min(upper.Length, 42)];
            }
            return upper;
        }
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    private string originalName = string.Empty;
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
            if (DateTime.TryParseExact(BestTime, "hh:mm:ss.fff", null, DateTimeStyles.None, out DateTime time))
            {
                return time.ToString("m:ss.fff");
            }
            return string.Empty;
        }
    }
    public int BestTimeMs
    {
        get
        {
            if (TimeSpan.TryParseExact(BestTime, "hh\\:mm\\:ss\\.fff", CultureInfo.InvariantCulture, out TimeSpan time))
            {
                var ms = (int)time.TotalMilliseconds;
                if (ms <= 0)
                    return int.MaxValue;
                return ms;
            }
            return int.MaxValue;
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
    [NotifyPropertyChangedFor(nameof(PositionsGainedLost))]
    private int overallPosition;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Position))]
    [NotifyPropertyChangedFor(nameof(PositionsGainedLost))]
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
    [NotifyPropertyChangedFor(nameof(HasPenaltyLaps))]
    [NotifyPropertyChangedFor(nameof(PenaltyLapTerm))]
    private int penaltyLaps;
    public bool HasPenaltyLaps => PenaltyLaps > 0;
    public string PenaltyLapTerm => PenaltyLaps == 1 ? "Lap" : "Laps";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPenaltyWarnings))]
    private int penaltyWarnings;
    public bool HasPenaltyWarnings => PenaltyWarnings > 0;
    [ObservableProperty]
    private bool showPenaltyColumn = false;
    [ObservableProperty]
    private bool isStale;
    [ObservableProperty]
    private PitStates pitState;

    #endregion

    private int? overridePosition;

    /// <summary>
    /// Used when sorting cars to put the car with no position at the end of the list.
    /// </summary>
    public int SortablePosition
    {
        get
        {
            if (overridePosition != null)
            {
                return overridePosition.Value;
            }
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
            if (overridePosition != null)
            {
                return overridePosition.Value;
            }
            if (CurrentGroupMode == GroupMode.Overall)
            {
                return OverallPosition;
            }
            return ClassPosition;
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

    private PositionChange positionsGainedLost = new();
    public PositionChange PositionsGainedLost
    {
        get
        {
            if (CurrentGroupMode == GroupMode.Overall)
            {
                positionsGainedLost.RawPositionChange = OverallPositionsGained;
            }
            else
            {
                positionsGainedLost.RawPositionChange = InClassPositionsGained;
            }

            return positionsGainedLost;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowBackground))]
    private string rowBackgroundKey = CARROW_NORMAL_BACKGROUNDBRUSH;
    public Color RowBackground => (Color?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, RowBackgroundKey) ?? Colors.Transparent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LapDataColor))]
    private string lapDataBrushKey = CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH;
    public IBrush LapDataColor => (IBrush?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, LapDataBrushKey) ?? Brushes.Black;
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

    private readonly Debouncer viewSizeDebouncer = new(TimeSpan.FromMilliseconds(50));

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
    private readonly ViewSizeService viewSizeService;
    [ObservableProperty]
    private DetailsViewModel? carDetailsViewModel;

    #endregion


    public CarViewModel(int eventId, EventClient serverClient, HubClient hubClient, PitTracking pitTracking, ViewSizeService viewSizeService)
    {
        this.eventId = eventId;
        this.serverClient = serverClient;
        this.hubClient = hubClient;
        this.pitTracking = pitTracking;
        this.viewSizeService = viewSizeService;
        WeakReferenceMessenger.Default.RegisterAll(this);
        Receive(new SizeChangedNotification(Size.Infinity));
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
        PenaltyLaps = carPosition.PenalityLaps;
        PenaltyWarnings = carPosition.PenalityWarnings;
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
            RowBackgroundKey = CARROW_STALE_BACKGROUNDBRUSH;
        }
        else if (RowBackgroundKey == CARROW_STALE_BACKGROUNDBRUSH)
        {
            RowBackgroundKey = CARROW_NORMAL_BACKGROUNDBRUSH;
        }

        // Flash the row background if the lap has changed
        if (prevLap != LastLap && RowBackgroundKey != CARROW_UPDATED_BACKGROUNDBRUSH)
        {
            Observable.Timer(TimeSpan.FromMilliseconds(80)).Subscribe(_ => Dispatcher.UIThread.Post(() => RowBackgroundKey = CARROW_UPDATED_BACKGROUNDBRUSH));
            Observable.Timer(TimeSpan.FromSeconds(0.9)).Subscribe(_ => Dispatcher.UIThread.Post(() => RowBackgroundKey = CARROW_NORMAL_BACKGROUNDBRUSH));
        }

        if (LastLap > 0)
        {
            // Check for best lap overall/in-class and car's fastest lap
            if (IsBestTime)
            {
                LapDataBrushKey = CARROWLAPTEXTFOREGROUND_OVERALLBEST_BRUSH;
                LapDataFontWeight = FontWeight.Bold;
            }
            else if (BestLap == LastLap)
            {
                LapDataBrushKey = CARROWLAPTEXTFOREGROUND_BEST_BRUSH;
                LapDataFontWeight = FontWeight.Bold;
            }
            else // Reset to normal
            {
                LapDataBrushKey = CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH;
                LapDataFontWeight = FontWeight.Normal;
            }
        }
        else // Reset to normal
        {
            LapDataBrushKey = CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH;
            LapDataFontWeight = FontWeight.Normal;
        }

        CarDetailsViewModel?.UpdateLaps([carPosition]);
        LastCarPosition = carPosition;
    }

    public void ApplyEntry(EventEntry entry)
    {
        Number = entry.Number;
        OriginalName = entry.Name;
        Team = entry.Team;
        Class = entry.Class;

        // Force reset once loaded - prevents color from getting stuck on update color
        Observable.Timer(TimeSpan.FromSeconds(1.5)).Subscribe(_ => Dispatcher.UIThread.Post(() => RowBackgroundKey = CARROW_NORMAL_BACKGROUNDBRUSH, DispatcherPriority.Send));
    }

    private void UpdateCarDetails(bool isEnabled)
    {
        if (isEnabled && CarDetailsViewModel == null)
        {
            _ = int.TryParse(LastCarPosition?.SessionId ?? "0", out int sessionId);
            CarDetailsViewModel = new DetailsViewModel(eventId, sessionId, Number, serverClient, hubClient, pitTracking);
            _ = CarDetailsViewModel.Initialize();
        }
        else if (!isEnabled && CarDetailsViewModel != null)
        {
            CarDetailsViewModel.Dispose();
            CarDetailsViewModel = null;
        }
    }

    /// <summary>
    /// Manually set the position such as for fastest time rather than overall position.
    /// </summary>
    /// <param name="position">1-based position, or null to reset back to position</param>
    public void OverridePosition(int? position)
    {
        var lastPos = overridePosition;
        overridePosition = position;

        if (lastPos != position)
        {
            OnPropertyChanged(nameof(SortablePosition));
            OnPropertyChanged(nameof(Position));
        }
    }

    /// <summary>
    /// Handles notifications related to size changes.
    /// </summary>
    public void Receive(SizeChangedNotification message)
    {
        ShowPenaltyColumn = viewSizeService.CurrentSize.Width > LiveTimingViewModel.PenaltyColumnWidth;
        FireNamePropertyChangedDebounced();
    }

    private void FireNamePropertyChangedDebounced()
    {
        _ = viewSizeDebouncer.ExecuteAsync(() =>
        {
            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(Name)), DispatcherPriority.Normal);
            return Task.CompletedTask;
        });
    }
}

public partial class PositionChange : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionChangeText))]
    [NotifyPropertyChangedFor(nameof(IsGain))]
    [NotifyPropertyChangedFor(nameof(IsLoss))]
    [NotifyPropertyChangedFor(nameof(IsNoChange))]
    private int rawPositionChange;
    public string PositionChangeText { get => Math.Abs(RawPositionChange).ToString(); }
    public bool IsGain => RawPositionChange > 0 && RawPositionChange != CarPosition.InvalidPosition;
    public bool IsLoss => RawPositionChange < 0 && RawPositionChange != CarPosition.InvalidPosition;
    public bool IsNoChange => RawPositionChange == 0 && RawPositionChange != CarPosition.InvalidPosition;
}