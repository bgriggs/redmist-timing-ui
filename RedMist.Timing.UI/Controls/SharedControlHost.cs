using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace RedMist.Timing.UI.Controls;

/// <summary>
/// Shows a control that more than one place in the visual tree may ask for, in whichever of those
/// places is being laid out.
/// </summary>
/// <remarks>
/// A control can have only one parent. Put the same instance in two ContentControls and the second
/// one throws from inside its measure pass, which reaches the dispatcher's unhandled handler and
/// takes the app down. That is what a car's position chart did: the chart is built once per car and
/// held by its details view model, and the same car can be realized in more than one row at once -
/// the timing table keeps both its flat list and its class-grouped list in the tree and only hides
/// one of them - so opening the car's row in the other layout parented the chart a second time.
///
/// This host takes the child from whichever other host has it at the moment it is measured, unless
/// that host is itself on screen. Avalonia measures only what is visible, so the child goes to the
/// host being shown and comes back when that changes. The host it was taken from is invalidated, so
/// it asks again the next time it is shown.
///
/// Why not declare the chart in the template instead, so every row gets its own: the chart's series
/// and axes live on the view model, and LiveCharts keeps state on them per chart instance - the
/// data factory keys its points by canvas, an axis keys its separators by chart - while unloading a
/// chart does not remove that state again. A chart declared in the row is rebuilt whenever the row
/// is, and the flat list rebuilds a car's row every time the car changes position, so over a race
/// those entries would pile up against series that last as long as the car is expanded. One chart
/// per car keeps the lifetime it has always had.
///
/// Two limits, both deliberate. If two hosts are on screen at once, the one that has the child keeps
/// it and the other shows nothing: taking it back and forth would invalidate each in turn, and layout
/// would never settle. And a child held by something that is not a host is left where it is, with
/// this host showing nothing - an empty area is recoverable, an exception from layout is not.
/// </remarks>
public class SharedControlHost : Control
{
    public static readonly StyledProperty<Control?> ChildProperty =
        AvaloniaProperty.Register<SharedControlHost, Control?>(nameof(Child));

    /// <summary>
    /// The control actually parented here, which trails <see cref="Child"/> until the next measure
    /// and stays null while another host has it.
    /// </summary>
    private Control? hosted;

    static SharedControlHost()
    {
        AffectsMeasure<SharedControlHost>(ChildProperty);
    }

    public Control? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Let go straight away rather than at the next measure. A row losing its data context sets
        // this to null, and a host that is being thrown away may never be measured again - holding on
        // would keep the dead row alive for as long as the chart lives.
        if (change.Property == ChildProperty && !ReferenceEquals(hosted, change.NewValue))
        {
            Release();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Adopt();

        if (hosted is null)
        {
            return default;
        }

        hosted.Measure(availableSize);
        return hosted.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        hosted?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    private void Adopt()
    {
        var child = Child;
        if (child is null || ReferenceEquals(hosted, child))
        {
            return;
        }

        var holder = child.GetVisualParent() ?? child.Parent as Visual;
        if (holder is SharedControlHost other)
        {
            // A host that is out of the tree, or hidden, is not using it. One that is on screen is,
            // and two taking it from each other would never finish laying out.
            if (other.GetVisualRoot() is not null && other.IsEffectivelyVisible)
            {
                return;
            }

            other.Release();
        }
        else if (holder is not null)
        {
            return;
        }

        hosted = child;
        LogicalChildren.Add(child);
        VisualChildren.Add(child);
    }

    private void Release()
    {
        if (hosted is not { } child)
        {
            return;
        }

        hosted = null;
        VisualChildren.Remove(child);
        LogicalChildren.Remove(child);
        InvalidateMeasure();
    }
}
