using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;
using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace RedMist.Timing.UI.Controls;

/// <summary>
/// Provides width adjustment for the car info panel/row based on available page size to ensure the width does not exceed the page width.
/// </summary>
public class AdaptiveCarInfoPanel : Grid
{
    const double WidthOffset = 56;

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
    private CompositeDisposable? subscriptions;


    static AdaptiveCarInfoPanel()
    {
        PageSizeProperty.Changed.AddClassHandler<AdaptiveCarInfoPanel>((panel, e) => panel.OnPageSizeChanged());
    }

    private static double GetVisibleWidth(Control child)
    {
        return child.IsVisible ? child.DesiredSize.Width : 0;
    }

    protected virtual void OnPageSizeChanged()
    {
        if (double.IsNaN(PageSize.Width) || double.IsInfinity(PageSize.Width))
            return;

        var nameChild = Children[NameIndex];

        // Note the originalNameWidth is only set once, the first time we have a valid DesiredSize.Width
        if (originalNameWidth == null && nameChild.DesiredSize.Width > 0)
            originalNameWidth = nameChild.DesiredSize.Width;

        var nameWidth = GetVisibleWidth(nameChild);

        double fixedWidth = GetVisibleWidth(Children[NumberIndex])
            + GetVisibleWidth(Children[ClassIndex])
            + GetVisibleWidth(Children[InClassPosGainedIndex])
            + GetVisibleWidth(Children[OverallPosGainedIndex])
            + GetVisibleWidth(Children[InClassFastestAveragePaceIndex])
            + GetVisibleWidth(Children[InCarVideoIndex])
            + GetVisibleWidth(Children[PitStateIndex]);

        double availableWidth = Math.Max(0, PageSize.Width - fixedWidth - WidthOffset);

        if (nameWidth > availableWidth)
            nameChild.Width = availableWidth;
        else if (originalNameWidth != null)
            nameChild.Width = originalNameWidth.Value;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        subscriptions?.Dispose();
        subscriptions = [];

        if (originalNameWidth == null && Children[NameIndex].DesiredSize.Width > 0)
            originalNameWidth = Children[NameIndex].DesiredSize.Width;

        SubscribeToVisibility(InClassPosGainedIndex);
        SubscribeToVisibility(OverallPosGainedIndex);
        SubscribeToVisibility(InClassFastestAveragePaceIndex);
        SubscribeToVisibility(InCarVideoIndex);
        SubscribeToVisibility(PitStateIndex);

        subscriptions.Add(
            Observable.Timer(TimeSpan.FromMilliseconds(50))
                .Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => OnPageSizeChanged()))
        );
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        subscriptions?.Dispose();
        subscriptions = null;
        base.OnUnloaded(e);
    }

    private void SubscribeToVisibility(int childIndex)
    {
        if (childIndex >= 0 && childIndex < Children.Count)
        {
            subscriptions!.Add(
                Children[childIndex].GetObservable(IsVisibleProperty)
                    .Subscribe(_ => OnPageSizeChanged())
            );
        }
    }
}
