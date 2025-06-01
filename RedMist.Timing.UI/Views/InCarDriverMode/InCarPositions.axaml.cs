using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Maui.Devices;

namespace RedMist.Timing.UI.Views.InCarDriverMode;

public partial class InCarPositions : UserControl
{
    public InCarPositions()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        DeviceDisplay.KeepScreenOn = true;
    }
    
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        DeviceDisplay.KeepScreenOn = false;
    }
}