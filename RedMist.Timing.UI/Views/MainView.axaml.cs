using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;

namespace RedMist.Timing.UI.Views;

public partial class MainView : UserControl, IRecipient<LauncherEvent>
{
    public MainView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Set the app to display edge-to-edge specifically for Android Pixel devices
        // http://github.com/AvaloniaUI/Avalonia/issues/18544
        var insetsManager = TopLevel.GetTopLevel(this)?.InsetsManager;
        if (insetsManager is not null)
        {
            // Use dynamic to set DisplayEdgeToEdgePreference (enum value not directly accessible)
            // This property replaces the obsolete DisplayEdgeToEdge boolean
            try
            {
                dynamic manager = insetsManager;
                manager.DisplayEdgeToEdgePreference = 1; // 1 = Always
            }
            catch
            {
                // Fallback for versions without the new property
#pragma warning disable CS0618 // Type or member is obsolete
                insetsManager.DisplayEdgeToEdge = true;
#pragma warning restore CS0618 // Type or member is obsolete
            }
        }

        if (DataContext is MainViewModel vm)
        {
            await vm.Initialize();
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
        catch //(Exception ex)
        {
            // Logger.LogError(ex, "Failed to launch URI");
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        WeakReferenceMessenger.Default.Send(new SizeChangedNotification(e.NewSize));
    }
}
