using BigMission.Avalonia.Utilities;
using System.Linq;

namespace RedMist.Timing.UI.ViewModels.DataCollections;

public static class ObservableCollectionExtensions
{
    public static void Sort(this LargeObservableCollection<CarViewModel> collection)
    {
        //if (collection.Count < 2) return;

        ////collection.SetRange(collection.OrderBy(c => c.OverallPosition));
        //collection.Clear();
        ////collection.AddRange(collection.OrderBy(c => c.OverallPosition));
        //foreach (var car in collection)
        //{
        //    collection.Add(car);
        //}
    }

    public static void Sort(this LargeObservableCollection<GroupHeaderViewModel> collection)
    {
        if (collection.Count < 2) return;

        collection.SetRange(collection.OrderBy(c => c.Name));
    }
}
