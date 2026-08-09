using Avalonia.Controls;

namespace RedMist.Timing.UI.Views.InCarDriverMode;

public partial class InCarPositions : UserControl
{
    public InCarPositions()
    {
        InitializeComponent();
    }

    // Keeping the screen awake is driven by InCarSettingsViewModel rather than this view's
    // loaded/unloaded events: driver mode is only toggled with IsVisible, so the view is never
    // detached from the visual tree and OnUnloaded would not fire to release the wake lock.
}
