using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.ViewModels;
using System;

namespace RedMist.Timing.UI.Views;

public partial class EventsListView : UserControl
{
    private static ILogger Logger => App.GetLogger(nameof(EventsListView));

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
            Logger.LogError(ex, "Error loading events list");
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
            Logger.LogError(ex, "Error refreshing events list");
        }
        finally
        {
            // Notify the Refresh Container that the refresh is complete. Skipping this on failure
            // leaves the pull-to-refresh spinner stuck open.
            deferral.Complete();
        }
    }
}