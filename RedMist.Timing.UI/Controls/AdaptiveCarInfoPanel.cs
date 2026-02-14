using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;
using System;
using System.Reactive.Linq;

namespace RedMist.Timing.UI.Controls;

/// <summary>
/// Provides width adjustment for the car info panel/row based on available page size to ensure the width does not exceed the page width.
/// </summary>
public class AdaptiveCarInfoPanel : Grid
{
    const double WidthOffset = 56;
    //private readonly Debouncer debouncer = new(TimeSpan.FromMilliseconds(400));

    public static readonly StyledProperty<int> NumberIndexProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(NumberIndex));

    public int NumberIndex
    {
        get => GetValue(NumberIndexProperty);
        set => SetValue(NumberIndexProperty, value);
    }

    public static readonly StyledProperty<int> NameIndexProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(NameIndex));

    public int NameIndex
    {
        get => GetValue(NameIndexProperty);
        set => SetValue(NameIndexProperty, value);
    }

    public static readonly StyledProperty<int> ClassIndexProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(ClassIndex));

    public int ClassIndex
    {
        get => GetValue(ClassIndexProperty);
        set => SetValue(ClassIndexProperty, value);
    }

    public static readonly StyledProperty<int> InClassPosGainedIndexProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(InClassPosGainedIndex));

    public int InClassPosGainedIndex
    {
        get => GetValue(InClassPosGainedIndexProperty);
        set => SetValue(InClassPosGainedIndexProperty, value);
    }

    public static readonly StyledProperty<int> OverallPosGainedIndexProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(OverallPosGainedIndex));

    public int OverallPosGainedIndex
    {
        get => GetValue(OverallPosGainedIndexProperty);
        set => SetValue(OverallPosGainedIndexProperty, value);
    }

    public static readonly StyledProperty<int> InClassFastestAveragePaceIndexProperty =
       AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(InClassFastestAveragePaceIndex));

    public int InClassFastestAveragePaceIndex
    {
        get => GetValue(InClassFastestAveragePaceIndexProperty);
        set => SetValue(InClassFastestAveragePaceIndexProperty, value);
    }

    public static readonly StyledProperty<int> InCarVideoIndexProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(InCarVideoIndex));

    public int InCarVideoIndex
    {
        get => GetValue(InCarVideoIndexProperty);
        set => SetValue(InCarVideoIndexProperty, value);
    }

    public static readonly StyledProperty<int> PitStateIndexProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(PitStateIndex));

    public int PitStateIndex
    {
        get => GetValue(PitStateIndexProperty);
        set => SetValue(PitStateIndexProperty, value);
    }

    public static readonly StyledProperty<Size> PageSizeProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, Size>(nameof(PageSize));

    public Size PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    private double? originalNameWidth;


    static AdaptiveCarInfoPanel()
    {
        PageSizeProperty.Changed.AddClassHandler<AdaptiveCarInfoPanel>((panel, e) => panel.OnPageSizeChanged(e));
    }


    protected virtual void OnPageSizeChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (double.IsNaN(PageSize.Width) || double.IsInfinity(PageSize.Width))
            return;

        var numberWidth = Children[NumberIndex].IsVisible ? Children[NumberIndex].DesiredSize.Width : 0;

        // Note the originalNameWidth is only set once, the first time we have a valid DesiredSize.Width
        if (originalNameWidth == null && Children[NameIndex].DesiredSize.Width > 0)
            originalNameWidth = Children[NameIndex].DesiredSize.Width;

        var nameWidth = Children[NameIndex].IsVisible ? Children[NameIndex].DesiredSize.Width : 0;
        var classWidth = Children[ClassIndex].IsVisible ? Children[ClassIndex].DesiredSize.Width : 0;
        var inClassPosGainedWidth = Children[InClassPosGainedIndex].IsVisible ? Children[InClassPosGainedIndex].DesiredSize.Width : 0;
        var overallPosGainedWidth = Children[OverallPosGainedIndex].IsVisible ? Children[OverallPosGainedIndex].DesiredSize.Width : 0;
        var inClassFastestAveragePaceWidth = Children[InClassFastestAveragePaceIndex].IsVisible ? Children[InClassFastestAveragePaceIndex].DesiredSize.Width : 0;
        var inCarVideoWidth = Children[InCarVideoIndex].IsVisible ? Children[InCarVideoIndex].DesiredSize.Width : 0;
        var pitStateWidth = Children[PitStateIndex].IsVisible ? Children[PitStateIndex].DesiredSize.Width : 0;

        double fixedWith = numberWidth + classWidth + inClassPosGainedWidth + overallPosGainedWidth + inClassFastestAveragePaceWidth + inCarVideoWidth + pitStateWidth;
        double availableWidth = PageSize.Width - fixedWith - WidthOffset;
        if (availableWidth < 0)
            availableWidth = 0;

        if (nameWidth > availableWidth)
            Children[NameIndex].Width = availableWidth;
        else if (originalNameWidth != null)
            Children[NameIndex].Width = originalNameWidth.Value;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (originalNameWidth == null && Children[NameIndex].DesiredSize.Width > 0)
            originalNameWidth = Children[NameIndex].DesiredSize.Width;

        if (InClassPosGainedIndex >= 0 && InClassPosGainedIndex < Children.Count)
            Children[InClassPosGainedIndex].GetObservable(IsVisibleProperty).Subscribe(_ => OnPageSizeChanged(null!));

        if (OverallPosGainedIndex >= 0 && OverallPosGainedIndex < Children.Count)
            Children[OverallPosGainedIndex].GetObservable(IsVisibleProperty).Subscribe(_ => OnPageSizeChanged(null!));

        if (InClassFastestAveragePaceIndex >= 0 && InClassFastestAveragePaceIndex < Children.Count)
            Children[InClassFastestAveragePaceIndex].GetObservable(IsVisibleProperty).Subscribe(_ => OnPageSizeChanged(null!));

        if (InCarVideoIndex >= 0 && InCarVideoIndex < Children.Count)
            Children[InCarVideoIndex].GetObservable(IsVisibleProperty).Subscribe(_ => OnPageSizeChanged(null!));

        if (PitStateIndex >= 0 && PitStateIndex < Children.Count)
            Children[PitStateIndex].GetObservable(IsVisibleProperty).Subscribe(_ => OnPageSizeChanged(null!));

        Observable.Timer(TimeSpan.FromMilliseconds(50)).Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => OnPageSizeChanged(null!)));
    }
}
