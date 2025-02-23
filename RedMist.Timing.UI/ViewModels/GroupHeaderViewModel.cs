using DynamicData;
using DynamicData.Binding;
using System;
using System.Collections;
using System.Collections.ObjectModel;

namespace RedMist.Timing.UI.ViewModels;

public class GroupHeaderViewModel : ObservableCollection<CarViewModel>
{
    public string Name { get; } = string.Empty;

    public GroupHeaderViewModel(string name, IObservableCache<CarViewModel, string> observableCache)
    {
        Name = name;
        observableCache.Connect()
            .AutoRefresh(t => t.OverallPosition)
            .SortAndBind(this, SortExpressionComparer<CarViewModel>.Ascending(t => t.OverallPosition))
            .DisposeMany()
            .Subscribe();
    }

}
