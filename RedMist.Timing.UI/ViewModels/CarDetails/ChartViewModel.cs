﻿using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
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
    private readonly SortedDictionary<int, LapViewModel> laps = [];

    // Putting chart in here to be able to manage the cleanup and prevent exception when it has no axes.
    public CartesianChart Chart => new()
    {
        Padding = new Avalonia.Thickness(0),
        MinWidth = 90,
        Height = 240,
        ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.X,
        Series = Series,
        XAxes = XAxes,
        YAxes = YAxes
    };

    public ObservableCollection<ISeries> Series { get; } =
    [
        new ColumnSeries<LapViewModel>
        {
            Name = "Lap/Race Time",
            MaxBarWidth = BarWidth,
            ScalesYAt = 0,
            Mapping = (vm, lap) => new LiveChartsCore.Kernel.Coordinate(vm.LapNumber, vm.LapTimeDt.TimeOfDay.TotalSeconds),
            DataLabelsFormatter = (point) => point.Model?.LapTime ?? string.Empty,
            YToolTipLabelFormatter = (point) => $"{point.Model?.LapNumber ?? 0} @ {point.Model?.RaceTime ?? string.Empty}",
        },
        new LineSeries<LapViewModel>
        {
            Name = "Position Overall",
            Mapping = (vm, lap) => new LiveChartsCore.Kernel.Coordinate(vm.LapNumber, vm.OverallPosition),
            ScalesYAt = 1,
            Stroke = new SolidColorPaint(SKColors.Coral, 2),
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
            Stroke = new SolidColorPaint(SKColors.Cyan, 2),
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            Fill = null,
        },
        //new ScatterSeries<LapViewModel, >
        //{
        //    Name = "Pit",
        //    Mapping = (vm, lap) => new LiveChartsCore.Kernel.Coordinate(vm.LapNumber, 1),
        //    YToolTipLabelFormatter = (point) => $"{point.Model?.InPit}",
        //    ScalesYAt = 1,
        //    GeometrySize = 0,
        //    Fill = null,
        //    Stroke = new SolidColorPaint(SKColors.Cyan, 2),
            
        //}
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
            Position = LiveChartsCore.Measure.AxisPosition.End
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
            MaxLimit = 23,
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
    }


    private void ChartViewModel_PointMeasured(LiveChartsCore.Kernel.ChartPoint<LapViewModel, LiveChartsCore.SkiaSharpView.Drawing.Geometries.RoundedRectangleGeometry, LiveChartsCore.SkiaSharpView.Drawing.Geometries.LabelGeometry> obj)
    {
        if (obj.Model != null && obj.Visual != null)
        {
            var brush = (ImmutableSolidColorBrush)obj.Model.FlagColor;
            var skColor = new SKColor(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A);
            obj.Visual.Fill = new SolidColorPaint(skColor);
        }
    }

    public void UpdateLaps(List<CarPosition> carPositions)
    {
        foreach (var carUpdate in carPositions)
        {
            if (carUpdate.Number == null || carUpdate.LastLap <= 0)
                continue;
            laps[carUpdate.LastLap] = new LapViewModel(carUpdate);
        }

        // Fill in missing laps
        if (laps.Count > 0)
        {
            var maxLap = laps.Keys.Max();
            for (int i = 1; i <= maxLap; i++)
            {
                if (!laps.ContainsKey(i))
                {
                    laps[i] = new LapViewModel(new CarPosition { LastLap = i });
                }
            }
        }

        if (Series[0].Values?.Cast<object>().Count() != laps.Count)
        {
            Series[0].Values = laps.Values;
            Series[1].Values = laps.Values;
            Series[2].Values = laps.Values;
            //Series[3].Values = laps.Values;
        }
    }
}
