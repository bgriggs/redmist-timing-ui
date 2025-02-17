using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RedMist.Timing.UI.ViewModels;
using System.Collections.Generic;

namespace RedMist.Timing.UI.Views;

public partial class EventStatusView : UserControl
{
    public EventStatusView()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is EventStatusViewModel vm)
        {
            await vm.Initialize(1);
        }
        //dataGrid.GetVisualChildren();
        //dataGrid.GetControl<ToggleButton>();
        //dataGrid.
        //dataGrid.RowG
    }

    //static List<T> GetVisualTreeObjects<T>(this Visual obj) where T : Visual
    //{
    //    var objects = new List<T>();
    //    foreach (var child in obj.GetVisualChildren())
    //    {
    //        if (child is T requestedType)
    //            objects.Add(requestedType);
    //        objects.AddRange(child.GetVisualTreeObjects<T>());
    //    }
    //    return objects;
    //}
    //public static void ToggleExpander(this DataGrid datagrid, bool expand)
    //{
    //    List<DataGridRowGroupHeader> groupHeaderList = GetVisualTreeObjects<DataGridRowGroupHeader>(datagrid);
    //    if (groupHeaderList.Count == 0) return;
    //    foreach (DataGridRowGroupHeader header in groupHeaderList)
    //    {
    //        foreach (var e in GetVisualTreeObjects<ToggleButton>(header))
    //            e.IsChecked = expand;
    //    }
    //}
}