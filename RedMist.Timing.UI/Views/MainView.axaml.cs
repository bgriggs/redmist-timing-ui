using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using System;
using System.Reactive.Linq;

namespace RedMist.Timing.UI.Views;

public partial class MainView : UserControl, IRecipient<LauncherEvent>
{
    private readonly Debouncer debouncer = new(TimeSpan.FromMilliseconds(25));

    private static ILogger Logger => App.GetLogger(nameof(MainView));

    public MainView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        try
        {
            // Set the app to display edge-to-edge specifically for Android Pixel devices
            // http://github.com/AvaloniaUI/Avalonia/issues/18544
            var insetsManager = TopLevel.GetTopLevel(this)?.InsetsManager;
            if (insetsManager is not null)
            {
                // Same effect as before, said once. This went through dynamic to set
                // DisplayEdgeToEdgePreference to 1, but that property is a bool, so the assignment
                // threw every time and the catch below it did the actual work. DisplayEdgeToEdge
                // sets the same preference, and dropping the dynamic also drops a dependency on the
                // C# runtime binder, which is the kind of thing trimming removes.
#pragma warning disable CS0618 // Obsolete in favor of the preference property, which is not on the interface.
                insetsManager.DisplayEdgeToEdge = true;
#pragma warning restore CS0618
            }

            if (DataContext is MainViewModel vm)
            {
                await vm.Initialize();
                vm.IsTimingTabStripVisibleChanged += isVisible => Observable.Timer(TimeSpan.FromMilliseconds(100)).Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => UpdateTabBarVisibility(Bounds.Size)));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in MainView.OnLoaded");
        }
    }

    public void Receive(LauncherEvent message)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null && !string.IsNullOrEmpty(message.Uri))
            {
                launcher.LaunchUriAsync(new(message.Uri));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to launch URI {Uri}", message.Uri);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        WeakReferenceMessenger.Default.Send(new SizeChangedNotification(e.NewSize));
        debouncer.ExecuteAsync(async () => Dispatcher.UIThread.InvokeOnUIThread(() => UpdateTabBarVisibility(e.NewSize)));
        //UpdateTabBarVisibility(e.NewSize);
    }

    private void UpdateTabBarVisibility(Size size)
    {
        const double WidthMargin = 20;

        if (double.IsNaN(size.Width) || double.IsInfinity(size.Width))
            return;

        var liveTimingWidth = LiveTimingTab.IsVisible ? LiveTimingTab.DesiredSize.Width : 0;
        var resultsWidth = ResultsTab.IsVisible ? ResultsTab.DesiredSize.Width : 0;
        var informationWidth = InformationTab.IsVisible ? InformationTab.DesiredSize.Width : 0;
        var settingsWidth = SettingsTab.IsVisible ? SettingsTab.DesiredSize.Width : 0;

        double fixedWith = liveTimingWidth + resultsWidth + informationWidth + settingsWidth + WidthMargin;

        if (DataContext is MainViewModel vm && vm.IsControlLogAvailable)
        {
            if (fixedWith + ControlLogTab.DesiredSize.Width < size.Width)
            {
                fixedWith += ControlLogTab.DesiredSize.Width;
                ControlLogTab.IsVisible = true;
            }
            else
            {
                ControlLogTab.IsVisible = false;
            }
        }
        else
        {
            ControlLogTab.IsVisible = false;
        }

        FlagsTab.IsVisible = fixedWith + FlagsTab.DesiredSize.Width < size.Width;
    }
}
