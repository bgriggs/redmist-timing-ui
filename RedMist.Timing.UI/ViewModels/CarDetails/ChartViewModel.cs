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

    // Putting chart in here to be able to manage the cleanup and prevent exception when it has no axes.
    public CartesianChart Chart
    {
        get
        {
            // Get a new chart each request as a workaround for it being rendered again when changed to grouped or back
            return new CartesianChart
            {
                Padding = new Avalonia.Thickness(0),
                MinWidth = 90,
                Height = 240,
                ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.X,
                Series = Series,
                XAxes = XAxes,
                YAxes = YAxes
            };
        }
    }
    private readonly SortedDictionary<int, LapViewModel> laps = [];

    public ObservableCollection<ISeries> Series { get; } =
    [
        new ColumnSeries<double>
        {
            Name = "Lap",
            MaxBarWidth = BarWidth,
            ScalesYAt = 0
            //DataLabelsFormatter = (point) => GetDataLabel(point),
            //ScalesYAt = 0,
            //GeometrySize = 0,
            //GeometryFill = null,
            //GeometryStroke = null,
            //LineSmoothness = 0,
            //Fill = null,
            //Stroke = new SolidColorPaint(s_blue) { StrokeThickness = 2 },
            //YToolTipLabelFormatter = (point) => $"{point.Coordinate.PrimaryValue:0.#} @ {point.Coordinate.SecondaryValue:0.####}",
        },
        new LineSeries<int>
        {
            Name = "Position Overall",
            ScalesYAt = 1,
            Stroke = new SolidColorPaint(SKColors.Coral, 2),
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            Fill = null,
        },
        new LineSeries<int>
        {
            Name = "Position Class",
            ScalesYAt = 1,
            Stroke = new SolidColorPaint(SKColors.Cyan, 2),
            GeometrySize = 0,
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 0,
            Fill = null,
        }
    ];

    public ObservableCollection<ICartesianAxis> YAxes { get; } =
    [
        new Axis
        {
            //Name = "Time",
            //TicksPaint = new SolidColorPaint(s_blue),
            //SubticksPaint = new SolidColorPaint(s_blue),
            DrawTicksPath = true,
            //ForceStepToMin = true,
            IsVisible = false
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
            //Name = "Lap",
            ShowSeparatorLines = true,
            LabelsRotation = 90,
            LabelsDensity = 0,
            MinStep = 1,
            ForceStepToMin = true,
            TextSize = 12,
            SeparatorsPaint = new SolidColorPaint(new SKColor(220, 220, 220)),
            MinLimit = 0,
            MaxLimit = 23
        }
    ];

    [ObservableProperty]
    private double width = 250;


    public void UpdateLaps(List<CarPosition> carPositions)
    {
        foreach (var carUpdate in carPositions)
        {
            if (carUpdate.Number == null || carUpdate.LastLap <= 0)
                continue;
            laps[carUpdate.LastLap] = new LapViewModel(carUpdate);
        }

        var lapSeconds = laps.Values.Select(l => l.LapTimeDt.TimeOfDay.TotalSeconds).ToList();
        if (lapSeconds.Count == 0)
            lapSeconds.Add(0);

        var lapLabels = laps.Values.Select(l => l.LapTime).ToList();
        if (lapLabels.Count == 0)
            lapLabels.Add(" ");

        var overallPositions = laps.Values.Select(l => l.OverallPosition).ToList();
        if (overallPositions.Count == 0)
            overallPositions.Add(0);

        var classPositions = laps.Values.Select(l => l.ClassPosition).ToList();
        if (classPositions.Count == 0)
            classPositions.Add(0);

        Series[0].Values = lapSeconds;
        Series[1].Values = overallPositions;
        Series[2].Values = classPositions;
        XAxes[0].Labels = lapLabels;
    }
}
