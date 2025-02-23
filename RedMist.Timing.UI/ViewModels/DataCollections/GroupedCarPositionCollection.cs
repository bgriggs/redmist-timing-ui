using BigMission.Avalonia.Utilities;
using System.Collections.Specialized;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels.DataCollections;

public class GroupedCarPositionCollection
{
    private LargeObservableCollection<CarViewModel> cars;
    public LargeObservableCollection<GroupHeaderViewModel> GroupedCars { get; } = [];


    public GroupedCarPositionCollection(LargeObservableCollection<CarViewModel> cars)
    {
        this.cars = cars;
        cars.CollectionChanged += Cars_CollectionChanged;

    }


    private void Cars_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace || e.Action == NotifyCollectionChangedAction.Reset)
        {
            //ApplyGroups(cars);
        }
    }

    private void ApplyGroups(LargeObservableCollection<CarViewModel> cars)
    {
        var groups = cars.GroupBy(c => c.Class);
        bool sortGroups = false;
        foreach (var group in groups)
        {
            var groupHeader = GroupedCars.FirstOrDefault(g => g.Name == group.Key);
            if (groupHeader == null)
            {
                groupHeader = new GroupHeaderViewModel { Name = group.Key };
                GroupedCars.Add(groupHeader);
                sortGroups = true;
            }
            groupHeader.Cars.SetRange(group.OrderBy(c => c.OverallPosition));
        }

        if (sortGroups)
        {
            GroupedCars.Sort();
        }

        // Remove groups not in cars
        foreach (var groupHeader in GroupedCars.ToList())
        {
            if (!groups.Any(g => g.Key == groupHeader.Name))
            {
                GroupedCars.Remove(groupHeader);
            }
        }
    }

}
