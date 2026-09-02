using Avalonia.Media;
using DynamicData;
using DynamicData.Binding;
using System;
using System.Collections.ObjectModel;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// View model for a group header in the live timing view when grouped by class.
/// </summary>
public class GroupHeaderViewModel : ObservableCollection<CarViewModel>, IDisposable
{
    public string Name { get; }
    public Brush ClassColor { get; }

    public GroupHeaderViewModel(string name, Brush classColor, IObservableCache<CarViewModel, string> observableCache)
    {
        Name = name;
        ClassColor = classColor;
        // No DisposeMany here, deliberately. This is one class's slice of the field, so it drops a
        // car whenever that car changes class or the group goes away - neither of which means the
        // car is finished with. Rows are owned by the source cache in LiveTimingViewModel, which is
        // the only thing that knows a car has actually left the event.
        subscription = observableCache.Connect()
            .AutoRefresh(t => t.OverallPosition)
            .AutoRefresh(t => t.SortablePosition)
            .AutoRefresh(t => t.BestTime)
            .SortAndBind(this, SortExpressionComparer<CarViewModel>.Ascending(t => t.SortablePosition))
            .Subscribe();
    }

    private readonly IDisposable subscription;

    /// <summary>
    /// Releases this group's subscription to its slice of the field.
    /// </summary>
    /// <remarks>
    /// Groups come and go as classes appear and empty out, and the transform that builds them is
    /// followed by a DisposeMany - which, before this type implemented the interface, did nothing.
    /// Every group ever built kept its subscription to the cache alive.
    /// </remarks>
    public void Dispose()
    {
        subscription.Dispose();
        GC.SuppressFinalize(this);
    }
}
