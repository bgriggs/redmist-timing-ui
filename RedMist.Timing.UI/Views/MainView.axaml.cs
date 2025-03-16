using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;

namespace RedMist.Timing.UI.Views;

public partial class MainView : UserControl, IRecipient<LauncherEvent>
{
    public MainView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.RegisterAll(this);
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
}
