using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using RedMist.TimingCommon.Models;
using SkiaSharp;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels.CarDetails;

public partial class ChartViewModel : ObservableObject
{
    private static readonly double BarWidth = 13;
    private const int VisibleLapWindow = 23;
    private readonly SortedDictionary<int, LapViewModel> laps = [];
    private int lastSeriesValueCount;

    private CartesianChart? chart;
    public CartesianChart Chart => chart ??= new()
    {
        Padding = new Thickness(0),
        MinWidth = 90,
        Height = 240,
        ZoomMode = ZoomAndPanMode.X,
        Series = Series,
        XAxes = XAxes,
        YAxes = YAxes,
        TooltipTextSize = 10,
        FindingStrategy = FindingStrategy.CompareOnlyX
    };

    public ObservableCollection<ISeries> Series { get; } =
    [
        new ColumnSeries<LapViewModel>
        {
            Name = "Lap@RaceTime/Time(Laps)Since Pit",
            MaxBarWidth = BarWidth,
            ScalesYAt = 0,
            Mapping = (vm, lap) => new LiveChartsCore.Kernel.Coordinate(vm.LapNumber, vm.LapTimeDt.TimeOfDay.TotalSeconds),
            DataLabelsFormatter = (point) => point.Model?.LapTime ?? string.Empty,
            YToolTipLabelFormatter = (point) =>
            {
                if (string.IsNullOrWhiteSpace(point.Model?.MinutesSinceLastPit))
                {
                    return $"{point.Model?.LapNumber ?? 0}@{point.Model?.RaceTime ?? string.Empty}";
                }
                return $"{point.Model?.LapNumber ?? 0}@{point.Model?.RaceTime ?? string.Empty}/{point.Model?.MinutesSinceLastPit}m({point.Model?.LapsSinceLastPit})";
            }
        },
        new LineSeries<LapViewModel>
        {
            Name = "Position Overall",
            Mapping = (vm, lap) => new LiveChartsCore.Kernel.Coordinate(vm.LapNumber, vm.OverallPosition),
            ScalesYAt = 1,
            Stroke = new SolidColorPaint(GetThemeChartColor("ChartOverallPositionBrush"), 2),
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            Fill = null,
        },
        new LineSeries<LapViewModel>
        {
            Name = "Position Class",
            Mapping = (vm, lap) => new LiveChartsCore.Kernel.Coordinate(vm.LapNumber, vm.ClassPosition),
            ScalesYAt = 1,
            Stroke = new SolidColorPaint(GetThemeChartColor("ChartClassPositionBrush"), 2),
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            Fill = null,
        },
        new ScatterSeries<LapViewModel>
        {
            Name = "Pit",
            Mapping = (vm, lap) =>
            {
                return new LiveChartsCore.Kernel.Coordinate(vm.LapNumber, vm.LapTimeDt.TimeOfDay.TotalSeconds);
            },
            DataLabelsPaint = new SolidColorPaint(SKColors.Gray),
            DataLabelsSize = 10,
            DataLabelsPosition = DataLabelsPosition.Middle,
            DataLabelsFormatter = point => "PIT",
            GeometrySize = 0 // Hide the marker
        }
    ];

    public ObservableCollection<ICartesianAxis> YAxes { get; } =
    [
        new Axis
        {
            DrawTicksPath = true,
            IsVisible = false,
            MinLimit = 0,
        },
        new Axis
        {
            MinLimit = 1,
            MinStep = 1,
            Position = AxisPosition.End
        },
    ];

    public ObservableCollection<Axis> XAxes { get; } =
    [
        new Axis
        {
            ShowSeparatorLines = true,
            LabelsRotation = 90,
            LabelsDensity = 0,
            MinStep = 1,
            ForceStepToMin = true,
            TextSize = 12,
            SeparatorsPaint = new SolidColorPaint(new SKColor(220, 220, 220)),
            MinLimit = 0,
            MaxLimit = VisibleLapWindow,
        }
    ];

    [ObservableProperty]
    private double width = 250;


    public ChartViewModel()
    {
        ((ColumnSeries<LapViewModel>)Series[0]).PointMeasured += ChartViewModel_PointMeasured;
        XAxes[0].Labeler = (value) =>
        {
            if (laps.TryGetValue((int)value, out var lap))
            {
                return lap.LapTime;
            }
            return string.Empty;
        };

        // Apply theme-aware text colors
        ApplyThemeColors();
    }


    private void ChartViewModel_PointMeasured(LiveChartsCore.Kernel.ChartPoint<LapViewModel,
        LiveChartsCore.SkiaSharpView.Drawing.Geometries.RoundedRectangleGeometry,
        LiveChartsCore.SkiaSharpView.Drawing.Geometries.LabelGeometry> obj)
    {
        if (obj.Model != null && obj.Visual != null)
        {
            var brush = (ImmutableSolidColorBrush)obj.Model.FlagColor;
            var skColor = new SKColor(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A);
            obj.Visual.Fill = new SolidColorPaint(skColor);
        }
    }

    private void ApplyThemeColors()
    {
        // Get theme-aware text color
        var textColor = GetThemeForegroundColor();

        // Apply to X axis
        XAxes[0].LabelsPaint = new SolidColorPaint(textColor);

        // Apply to Y axes
        foreach (var axis in YAxes)
        {
            axis.LabelsPaint = new SolidColorPaint(textColor);
        }

        // Apply to series tooltips and data labels
        ((ScatterSeries<LapViewModel>)Series[3]).DataLabelsPaint = new SolidColorPaint(textColor);
    }

    private static SKColor GetThemeForegroundColor()
    {
        // Try to get the theme-aware text color from Avalonia resources
        if (Application.Current?.TryGetResource("TextMedForegroundBrush", Application.Current.ActualThemeVariant, out var resource) == true)
        {
            if (resource is IBrush brush && brush is ISolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                return new SKColor(color.R, color.G, color.B, color.A);
            }
        }

        // Fallback to white if resource not found
        return SKColors.White;
    }

    private static SKColor GetThemeChartColor(string resourceKey)
    {
        // Try to get the theme-aware chart color from Avalonia resources
        if (Application.Current?.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out var resource) == true)
        {
            if (resource is IBrush brush && brush is ISolidColorBrush solidBrush)
            {
                var color = solidBrush.Color;
                return new SKColor(color.R, color.G, color.B, color.A);
            }
        }

        // Fallback colors
        return resourceKey switch
        {
            "ChartOverallPositionBrush" => new SKColor(255, 153, 102), // #FF9966
            "ChartClassPositionBrush" => new SKColor(74, 157, 255),    // #4A9DFF
            _ => SKColors.White
        };
    }

    public void UpdateLaps(List<CarPosition> carPositions)
    {
        foreach (var carUpdate in carPositions)
        {
            if (carUpdate.Number == null || carUpdate.LastLapCompleted <= 0)
                continue;
            laps[carUpdate.LastLapCompleted] = new LapViewModel(carUpdate);
        }

        // Fill in missing laps
        if (laps.Count > 0)
        {
            var maxLap = laps.Keys.Last();
            for (int i = 1; i <= maxLap; i++)
            {
                laps.TryAdd(i, new LapViewModel(new CarPosition { LastLapCompleted = i }));
            }
        }

        // Pit laps
        var pitLaps = laps.Where(c => c.Value.CarPosition.LapIncludedPit).Select(k => k.Value).ToList();

        // Testing
        //foreach (var pl in laps.Values)
        //{
        //    if (pl.Flag == Flags.Yellow)
        //    {
        //        pitLaps.Add(pl);
        //    }
        //}

        // Update the time and laps since last pit
        int runningPitLaps = 0;
        double runningPitMinutes = 0;
        foreach (var l in laps.Values)
        {
            if (l.CarPosition.LapIncludedPit)
            {
                runningPitLaps = 0;
                runningPitMinutes = 0;
            }
            else
            {
                runningPitLaps++;
                runningPitMinutes += l.LapTimeDt.TimeOfDay.TotalMinutes;
                l.MinutesSinceLastPit = ((int)runningPitMinutes).ToString();
                l.LapsSinceLastPit = runningPitLaps.ToString();
            }
        }

        if (lastSeriesValueCount != laps.Count)
        {
            lastSeriesValueCount = laps.Count;
            Series[0].Values = laps.Values;
            Series[1].Values = laps.Values;
            Series[2].Values = laps.Values;
            Series[3].Values = pitLaps;

            // Scroll the visible area to keep the latest lap in view
            var maxLap = laps.Keys.Last();
            if (maxLap > VisibleLapWindow)
            {
                XAxes[0].MinLimit = maxLap - VisibleLapWindow;
                XAxes[0].MaxLimit = maxLap + 1;
            }
        }
    }
}
