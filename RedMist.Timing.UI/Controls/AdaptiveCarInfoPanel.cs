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

    public static readonly StyledProperty<int> DriverNameIndexProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, int>(nameof(DriverNameIndex), defaultValue: -1);

    public int DriverNameIndex
    {
        get => GetValue(DriverNameIndexProperty);
        set => SetValue(DriverNameIndexProperty, value);
    }

    public static readonly StyledProperty<Size> PageSizeProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, Size>(nameof(PageSize));

    public Size PageSize
    {
        get => GetValue(PageSizeProperty);
        set => SetValue(PageSizeProperty, value);
    }

    public static readonly StyledProperty<bool> IsDriverNameInlineProperty =
        AvaloniaProperty.Register<AdaptiveCarInfoPanel, bool>(nameof(IsDriverNameInline));

    public bool IsDriverNameInline
    {
        get => GetValue(IsDriverNameInlineProperty);
        set => SetValue(IsDriverNameInlineProperty, value);
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
        if (double.IsNaN(PageSize.Width) || double.IsInfinity(PageSize.Width) || PageSize.Width <= 0)
            return;

        var nameChild = Children[NameIndex];

        // Note the originalNameWidth is only set once, the first time we have a valid DesiredSize.Width
        if (originalNameWidth == null && nameChild.DesiredSize.Width > 0)
            originalNameWidth = nameChild.DesiredSize.Width;

        // Cannot adjust layout until the name has been measured at least once
        if (originalNameWidth == null)
            return;

        double fixedWidth = GetVisibleWidth(Children[NumberIndex])
            + GetVisibleWidth(Children[ClassIndex])
            + GetVisibleWidth(Children[InClassPosGainedIndex])
            + GetVisibleWidth(Children[OverallPosGainedIndex])
            + GetVisibleWidth(Children[InClassFastestAveragePaceIndex])
            + GetVisibleWidth(Children[InCarVideoIndex])
            + GetVisibleWidth(Children[PitStateIndex]);

        // Check if driver name can fit inline without reducing the name field
        bool canFitDriverInline = false;
        if (DriverNameIndex >= 0 && DriverNameIndex < Children.Count)
        {
            var driverChild = Children[DriverNameIndex];

            // Only consider inline if the driver name has meaningful text
            bool hasDriverText = driverChild is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text)
                && tb.Text != "Driver: ";

            if (hasDriverText)
            {
                // Temporarily make visible so we can measure its desired width
                bool wasVisible = driverChild.IsVisible;
                if (!wasVisible)
                    driverChild.IsVisible = true;
                driverChild.Measure(Size.Infinity);
                double driverWidth = driverChild.DesiredSize.Width;
                if (!wasVisible)
                    driverChild.IsVisible = false;

                double availableForNameAndDriver = Math.Max(0, PageSize.Width - fixedWidth - WidthOffset);
                if (driverWidth > 0)
                {
                    canFitDriverInline = (originalNameWidth.Value + driverWidth) <= availableForNameAndDriver;
                }
            }

            driverChild.IsVisible = canFitDriverInline;
        }
        IsDriverNameInline = canFitDriverInline;

        // Recalculate available width for name, now accounting for the inline driver name if shown
        double totalFixedWidth = fixedWidth;
        if (canFitDriverInline && DriverNameIndex >= 0 && DriverNameIndex < Children.Count)
            totalFixedWidth += GetVisibleWidth(Children[DriverNameIndex]);

        double availableWidth = Math.Max(0, PageSize.Width - totalFixedWidth - WidthOffset);

        if (GetVisibleWidth(nameChild) > availableWidth)
            nameChild.Width = availableWidth;
        else
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

        if (DriverNameIndex >= 0 && DriverNameIndex < Children.Count)
        {
            // Re-evaluate layout when driver name text changes (which affects DesiredSize)
            if (Children[DriverNameIndex] is Avalonia.Controls.TextBlock driverTb)
            {
                subscriptions!.Add(
                    driverTb.GetObservable(Avalonia.Controls.TextBlock.TextProperty)
                        .Subscribe(_ => OnPageSizeChanged())
                );
            }
        }

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
