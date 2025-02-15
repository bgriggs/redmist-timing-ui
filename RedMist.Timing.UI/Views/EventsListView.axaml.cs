using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
}