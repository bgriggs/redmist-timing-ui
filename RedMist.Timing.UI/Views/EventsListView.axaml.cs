using Avalonia.Controls;
using Avalonia.Interactivity;
using RedMist.Timing.UI.ViewModels;

namespace RedMist.Timing.UI.Views;

public partial class EventsListView : UserControl
{
    public EventsListView()
    {
        InitializeComponent();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is EventsListViewModel vm)
        {
            await vm.Initialize();
        }
    }

    private async void EventsRefreshContainer_RefreshRequested(object? sender, RefreshRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();

        // Refresh List Box Items
        if (DataContext is EventsListViewModel vm)
        {
            await vm.Initialize();
        }

        // Notify the Refresh Container that the refresh is complete.
        deferral.Complete();
    }
}