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
                // Falls through to a full load the first time, when there is nothing on screen to
                // compare against. On any later load the list is already up and must stay up.
                await vm.RefreshIfChangedAsync();
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
                // The pull gesture draws its own spinner, so the list underneath does not have to
                // be taken away to show that something is happening.
                await vm.RefreshIfChangedAsync(userRequested: true);
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