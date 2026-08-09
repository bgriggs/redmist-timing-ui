using Avalonia.Controls;
using Avalonia.Interactivity;
using RedMist.Timing.UI.ViewModels;
using System;

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
        try
        {
            if (DataContext is EventsListViewModel vm)
            {
                await vm.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading events list: {ex}");
        }
    }

    private async void EventsRefreshContainer_RefreshRequested(object? sender, RefreshRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();

        try
        {
            // Refresh List Box Items
            if (DataContext is EventsListViewModel vm)
            {
                await vm.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error refreshing events list: {ex}");
        }
        finally
        {
            // Notify the Refresh Container that the refresh is complete. Skipping this on failure
            // leaves the pull-to-refresh spinner stuck open.
            deferral.Complete();
        }
    }
}