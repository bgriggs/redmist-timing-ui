using BigMission.Avalonia.Utilities;
using RedMist.Timing.UI.ViewModels.DataCollections;

namespace RedMist.Timing.UI.ViewModels;

public class GroupHeaderViewModel
{
    public string Name { get; set; } = string.Empty;

    public LargeObservableCollection<CarViewModel> Cars { get; set; } = [];

    public void SortCars()
    {
        Cars.Sort();
    }
}
