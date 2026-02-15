using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon.Extensions;
using RedMist.TimingCommon.Models;
using RedMist.TimingCommon.Models.InCarVideo;
using RedMist.TimingCommon.Models.Mappers;
using System;
using System.Globalization;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading;

namespace RedMist.Timing.UI.ViewModels;

public partial class CarViewModel : ObservableObject, IRecipient<SizeChangedNotification>
{
    #region Resources

    private const string CARROW_NORMAL_BACKGROUNDBRUSH = "carRowNormalBackgroundBrush";
    private const string CARROW_UPDATED_BACKGROUNDBRUSH = "carRowUpdatedBackgroundBrush";
    private const string CARROW_STALE_BACKGROUNDBRUSH = "carRowStaleBackgroundBrush";

    public const string CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH = "carRowLapTextForegroundNormalBrush";
    public const string CARROWLAPTEXTFOREGROUND_BEST_BRUSH = "carRowLapTextForegroundBestBrush";
    public const string CARROWLAPTEXTFOREGROUND_OVERALLBEST_BRUSH = "carRowLapTextForegroundOverallBestBrush";

    public const string SENTINEL_IMAGE = "sentinelImage";
    public const string MRL_IMAGE = "mrlImage";

    #endregion

    private ILogger Logger { get; }

    #region Car Properties

    public CarPosition? LastCarPosition { get; private set; }

    [ObservableProperty]
    private string number = string.Empty;

    private string cachedName = string.Empty;
    public string Name => cachedName;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    private string originalName = string.Empty;
    partial void OnOriginalNameChanged(string value) => cachedName = value.ToUpperInvariant();
    [ObservableProperty]
    private string team = string.Empty;
    [ObservableProperty]
    private string _class = string.Empty;
    [ObservableProperty]
    private IBrush classColor = Brushes.Gray;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BestTimeShort))]
    private string? bestTime;
    private int cachedBestTimeMs = int.MaxValue;
    private string cachedBestTimeShort = string.Empty;
    partial void OnBestTimeChanged(string? value)
    {
        if (TimeSpan.TryParseExact(value, "hh\\:mm\\:ss\\.fff", CultureInfo.InvariantCulture, out TimeSpan ts))
        {
            var ms = (int)ts.TotalMilliseconds;
            cachedBestTimeMs = ms > 0 ? ms : int.MaxValue;
        }
        else
        {
            cachedBestTimeMs = int.MaxValue;
        }
        cachedBestTimeShort = DateTime.TryParseExact(value, "hh:mm:ss.fff", null, DateTimeStyles.None, out DateTime dt)
            ? dt.ToString("m:ss.fff")
            : string.Empty;
    }
    public string BestTimeShort => cachedBestTimeShort;
    public int BestTimeMs => cachedBestTimeMs;

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
            if (DateTime.TryParseExact(LastTime, "hh:mm:ss.fff", null, DateTimeStyles.None, out DateTime time))
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
    private bool inClassFastestAveragePace;
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
    [ObservableProperty]
    private Size pageSize;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LapProgressNormalFraction))]
    [NotifyPropertyChangedFor(nameof(LapProgressOverrunFraction))]
    [NotifyPropertyChangedFor(nameof(IsLapProgressOverrun))]
    private double projectedLapTimePercent;

    /// <summary>
    /// Fraction of total bar width (1.3) representing the normal portion (0 to 1.0).
    /// </summary>
    public double LapProgressNormalFraction => Math.Min(Math.Max(ProjectedLapTimePercent, 0), 1.0) / 1.3;

    /// <summary>
    /// Fraction of total bar width (1.3) representing the overrun portion (past 1.0).
    /// </summary>
    public double LapProgressOverrunFraction => Math.Max(0, Math.Min(ProjectedLapTimePercent, 1.3) - 1.0) / 1.3;

    /// <summary>
    /// Whether the projected lap time has exceeded the expected lap time.
    /// </summary>
    public bool IsLapProgressOverrun => ProjectedLapTimePercent > 1.0;

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

    private int? uiResetPosition = null;

    public int Position
    {
        get
        {
            if (uiResetPosition != null)
            {
                return uiResetPosition.Value;
            }
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

    private PositionChange? uiResetPositionsGainedLost = null;
    private readonly PositionChange positionsGainedLost = new();
    public PositionChange PositionsGainedLost
    {
        get
        {
            if (uiResetPositionsGainedLost != null)
            {
                return uiResetPositionsGainedLost;
            }
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BestLapDataColor))]
    private string bestLapDataBrushKey = CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH;
    public IBrush BestLapDataColor => (IBrush?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, BestLapDataBrushKey) ?? Brushes.Black;
    [ObservableProperty]
    private FontWeight bestLapDataFontWeight = FontWeight.Normal;

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
    private IDisposable? flashStartTimer;
    private IDisposable? flashEndTimer;
    private IDisposable? forcePropertyTimer;
    private static readonly Lock s_imageLock = new();
    private static IImage? s_sentinelImage;
    private static IImage? s_mrlImage;
    private static IImage? s_defaultImage;

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

    private readonly Event evt;
    private readonly EventClient serverClient;
    private readonly HubClient hubClient;
    private readonly PitTracking pitTracking;
    private readonly ViewSizeService viewSizeService;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;
    [ObservableProperty]
    private DetailsViewModel? carDetailsViewModel;

    #endregion

    #region In-Car Video

    [ObservableProperty]
    private bool isCarStreaming;

    [ObservableProperty]
    private IImage? carStreamImage;

    [ObservableProperty]
    private string? carStreamDestinationUrl;

    #endregion

    [ObservableProperty]
    private bool hasDriverName;

    [ObservableProperty]
    private string driverName = string.Empty;


    public CarViewModel(Event evt, EventClient serverClient, HubClient hubClient, PitTracking pitTracking, ViewSizeService viewSizeService, 
        IHttpClientFactory httpClientFactory, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.evt = evt;
        this.serverClient = serverClient;
        this.hubClient = hubClient;
        this.pitTracking = pitTracking;
        this.viewSizeService = viewSizeService;
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        WeakReferenceMessenger.Default.RegisterAll(this);
        //Receive(new SizeChangedNotification(Size.Infinity));
    }


    public void ApplyPatch(CarPositionPatch p)
    {
        var prevLap = LastLap;
        var prevPos = OverallPosition;

        if (p.BestTime != null)
            BestTime = p.BestTime;
        if (p.BestLap != null)
            BestLap = p.BestLap.Value;
        if (p.IsBestTime != null)
            IsBestTimeOverall = p.IsBestTime.Value;
        if (p.IsBestTimeClass != null)
            IsBestTimeClass = p.IsBestTimeClass.Value;
        if (p.OverallGap != null)
            OverallGap = p.OverallGap;
        if (p.OverallDifference != null)
            OverallDifference = p.OverallDifference;
        if (p.InClassGap != null)
            InClassGap = p.InClassGap;
        if (p.InClassDifference != null)
            InClassDifference = p.InClassDifference;
        if (p.TotalTime != null)
            TotalTime = p.TotalTime;
        if (p.LastLapTime != null)
            LastTime = p.LastLapTime;
        if (p.LastLapCompleted != null)
            LastLap = p.LastLapCompleted.Value;
        if (p.OverallPosition != null)
            OverallPosition = p.OverallPosition.Value;
        if (p.ClassPosition != null)
            ClassPosition = p.ClassPosition.Value;
        if (p.OverallPositionsGained != null)
            OverallPositionsGained = p.OverallPositionsGained.Value;
        if (p.InClassPositionsGained != null)
            InClassPositionsGained = p.InClassPositionsGained.Value;
        if (p.IsOverallMostPositionsGained != null)
            IsOverallMostPositionsGained = p.IsOverallMostPositionsGained.Value;
        if (p.IsClassMostPositionsGained != null)
            IsClassMostPositionsGained = p.IsClassMostPositionsGained.Value;
        if (p.InClassFastestAveragePace != null)
            InClassFastestAveragePace = p.InClassFastestAveragePace.Value;
        if (p.PenalityLaps != null)
            PenaltyLaps = p.PenalityLaps.Value;
        if (p.PenalityWarnings != null)
            PenaltyWarnings = p.PenalityWarnings.Value;
        if (p.IsStale != null)
            IsStale = p.IsStale.Value;

        // Pit state
        if (p.IsEnteredPit ?? false)
            PitState = PitStates.EnteredPit;
        else if (p.IsPitStartFinish ?? false)
            PitState = PitStates.PitSF;
        else if (p.IsExitedPit ?? false)
            PitState = PitStates.ExitedPit;
        else if (p.IsInPit != null)
        {
            if (p.IsInPit.Value)
                PitState = PitStates.InPit;
            else
                PitState = PitStates.None;
        }

        // Driver Name
        if (p.DriverName != null)
        {
            var driver = p.DriverName.Trim();
            if (!string.IsNullOrWhiteSpace(driver))
            {
                HasDriverName = true;
                DriverName = driver;
            }
            else
            {
                HasDriverName = false;
                DriverName = string.Empty;
            }
        }

        // Video Stream
        if (p.InCarVideo != null)
        {
            if (p.InCarVideo.VideoDestination.Type != VideoDestinationType.None)
            {
                IsCarStreaming = true;
                CarStreamImage = GetCarSourceImage(p.InCarVideo.VideoSystemType);
                CarStreamDestinationUrl = p.InCarVideo.VideoDestination.Url;
            }
            else
            {
                IsCarStreaming = false;
                CarStreamImage = null;
                CarStreamDestinationUrl = null;
            }
        }

        // Record the pit stop
        if (p.IsInPit ?? false && !string.IsNullOrEmpty(p.Number))
            pitTracking.AddPitStop(Number, LastLap);

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
            flashStartTimer?.Dispose();
            flashEndTimer?.Dispose();
            flashStartTimer = Observable.Timer(TimeSpan.FromMilliseconds(80)).Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => RowBackgroundKey = CARROW_UPDATED_BACKGROUNDBRUSH));
            flashEndTimer = Observable.Timer(TimeSpan.FromSeconds(0.9)).Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => RowBackgroundKey = CARROW_NORMAL_BACKGROUNDBRUSH));
        }

        if (LastLap > 0)
        {
            // Best time styling (overall/class best, then car's personal best)
            if (IsBestTime)
            {
                BestLapDataBrushKey = CARROWLAPTEXTFOREGROUND_OVERALLBEST_BRUSH;
                BestLapDataFontWeight = FontWeight.Bold;
            }
            else if (BestLap == LastLap)
            {
                BestLapDataBrushKey = CARROWLAPTEXTFOREGROUND_BEST_BRUSH;
                BestLapDataFontWeight = FontWeight.Bold;
            }
            else
            {
                BestLapDataBrushKey = CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH;
                BestLapDataFontWeight = FontWeight.Normal;
            }

            // Last lap styling (car's personal best)
            if (BestLap == LastLap)
            {
                LapDataBrushKey = CARROWLAPTEXTFOREGROUND_BEST_BRUSH;
                LapDataFontWeight = FontWeight.Bold;
            }
            else
            {
                LapDataBrushKey = CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH;
                LapDataFontWeight = FontWeight.Normal;
            }
        }
        else // Reset to normal
        {
            LapDataBrushKey = CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH;
            LapDataFontWeight = FontWeight.Normal;
            BestLapDataBrushKey = CARROWLAPTEXTFOREGROUND_NORMAL_BRUSH;
            BestLapDataFontWeight = FontWeight.Normal;
        }

        // Force update of the position as these are getting dropped at times such as 
        // changing from overall to class mode. Simply firing a property changed does not work.
        ForcePropertyChange();
        forcePropertyTimer?.Dispose();
        forcePropertyTimer = Observable.Timer(TimeSpan.FromMilliseconds(500)).Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => ForcePropertyChange()));

        if (LastCarPosition != null)
        {
            CarPositionMapper.ApplyPatch(p, LastCarPosition);

            // Check that the lap has changed to avoid updating the previous lap data since lap number 
            // change and times are not guaranteed to be in the same patch
            if (prevLap != LastCarPosition.LastLapCompleted && CarDetailsViewModel != null)
            {
                var dc = LastCarPosition.DeepCopy();
                CarDetailsViewModel.UpdateLaps([dc]);
            }
        }
        else
        {
            LastCarPosition = CarPositionMapper.PatchToEntity(p);
        }
    }

    private void ForcePropertyChange()
    {
        uiResetPosition = 0;
        OnPropertyChanged(nameof(Position));
        uiResetPosition = null;
        OnPropertyChanged(nameof(Position));
        uiResetPositionsGainedLost = new PositionChange { RawPositionChange = 0 };
        OnPropertyChanged(nameof(PositionsGainedLost));
        uiResetPositionsGainedLost = null;
        OnPropertyChanged(nameof(PositionsGainedLost));
    }

    public void ApplyEntry(EventEntry entry, IBrush classColor)
    {
        Number = entry.Number;
        OriginalName = entry.Name;
        Team = entry.Team;
        Class = entry.Class;
        ClassColor = classColor;

        // Removing to prevent stale color reset
        //// Force reset once loaded - prevents color from getting stuck on update color
        //Observable.Timer(TimeSpan.FromSeconds(1.5)).Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => RowBackgroundKey = CARROW_NORMAL_BACKGROUNDBRUSH, DispatcherPriority.Send));
        // Initialize the car row width
        Receive(new SizeChangedNotification(viewSizeService.CurrentSize));
    }

    private void UpdateCarDetails(bool isEnabled)
    {
        if (isEnabled && CarDetailsViewModel == null)
        {
            _ = int.TryParse(LastCarPosition?.SessionId ?? "0", out int sessionId);
            CarDetailsViewModel = new DetailsViewModel(evt, sessionId, Number, serverClient, hubClient, pitTracking, httpClientFactory, configuration);
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

    public void UpdateProjectedLapTimeProgression(TimeSpan raceTime)
    {
        if (LastCarPosition == null || LastCarPosition.LastLapCompleted <= 0 || string.IsNullOrEmpty(LastCarPosition.TotalTime))
        {
            ProjectedLapTimePercent = 0;
            return;
        }

        double projectedMs = LastCarPosition.ProjectedLapTimeMs;

        // Fill missing projected time from LastLapTime or BestTime
        if (projectedMs <= 0)
        {
            projectedMs = ParseTimeToMs(LastCarPosition.LastLapTime);
            if (projectedMs <= 0)
            {
                projectedMs = ParseTimeToMs(LastCarPosition.BestTime);
                if (projectedMs > 0)
                {
                    // Add 5% buffer to best time for projection if no last lap time
                    projectedMs *= 1.05;
                }
            }
            if (projectedMs <= 0)
            {
                ProjectedLapTimePercent = 0;
                return;
            }
        }

        var lapStart = LiveTimingViewModel.ParseRMTime(LastCarPosition.TotalTime);
        var projectedEndTime = lapStart.Add(TimeSpan.FromMilliseconds(projectedMs));
        var msToGo = projectedEndTime.TotalMilliseconds - raceTime.TotalMilliseconds;
        var percent = 1 - (msToGo / projectedMs);
        ProjectedLapTimePercent = Math.Clamp(percent, 0, 1.3);
    }

    private static double ParseTimeToMs(string? time)
    {
        if (TimeSpan.TryParseExact(time, "hh\\:mm\\:ss\\.fff", CultureInfo.InvariantCulture, out var ts))
            return ts.TotalMilliseconds;
        return 0;
    }

    /// <summary>
    /// Handles notifications related to size changes.
    /// </summary>
    public void Receive(SizeChangedNotification message)
    {
        PageSize = message.Size;
        ShowPenaltyColumn = evt.HasControlLog && viewSizeService.CurrentSize.Width > LiveTimingViewModel.PenaltyColumnWidth;
        //FireNamePropertyChangedDebounced();
    }

    //private void FireNamePropertyChangedDebounced()
    //{
    //    _ = viewSizeDebouncer.ExecuteAsync(() =>
    //    {
    //        Dispatcher.UIThread.InvokeOnUIThread(() => OnPropertyChanged(nameof(Name)), DispatcherPriority.Background);
    //        return Task.CompletedTask;
    //    });
    //}

    private static IImage GetCarSourceImage(VideoSystemType type)
    {
        if (type == VideoSystemType.Sentinel)
        {
            if (s_sentinelImage != null)
                return s_sentinelImage;
            lock (s_imageLock)
            {
                if (s_sentinelImage != null)
                    return s_sentinelImage;
                if (Application.Current?.FindResource(Application.Current.ActualThemeVariant, SENTINEL_IMAGE) is string image)
                {
                    s_sentinelImage = new Bitmap(AssetLoader.Open(new Uri(image)));
                    return s_sentinelImage;
                }
            }
        }
        else if (type == VideoSystemType.MyRacesLive)
        {
            if (s_mrlImage != null)
                return s_mrlImage;
            lock (s_imageLock)
            {
                if (s_mrlImage != null)
                    return s_mrlImage;
                if (Application.Current?.FindResource(Application.Current.ActualThemeVariant, MRL_IMAGE) is string image)
                {
                    s_mrlImage = new Bitmap(AssetLoader.Open(new Uri(image)));
                    return s_mrlImage;
                }
            }
        }

        if (s_defaultImage != null)
            return s_defaultImage;
        lock (s_imageLock)
        {
            s_defaultImage ??= new Bitmap(AssetLoader.Open(new Uri("avares://RedMist.Timing.UI/Assets/BootstrapIcons-CameraVideo.png")));
            return s_defaultImage;
        }
    }

    [RelayCommand]
    public void LaunchInCarVideo()
    {
        var url = CarStreamDestinationUrl;
        if (!string.IsNullOrEmpty(url))
        {
            WeakReferenceMessenger.Default.Send(new LauncherEvent(url));
        }
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