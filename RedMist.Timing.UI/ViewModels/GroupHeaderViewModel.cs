using DynamicData;
using DynamicData.Binding;
using System;
using System.Collections.ObjectModel;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// View model for a group header in the live timing view when grouped by class.
/// </summary>
public class GroupHeaderViewModel : ObservableCollection<CarViewModel>
{
    public string Name { get; }

    public GroupHeaderViewModel(string name, IObservableCache<CarViewModel, string> observableCache)
    {
        Name = name;
        observableCache.Connect()
            .AutoRefresh(t => t.OverallPosition)
            .SortAndBind(this, SortExpressionComparer<CarViewModel>.Ascending(t => t.SortablePosition))
            .DisposeMany()
            .Subscribe();
    }
}
