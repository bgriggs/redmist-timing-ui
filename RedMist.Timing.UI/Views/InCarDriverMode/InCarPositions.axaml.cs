using Avalonia.Controls;

namespace RedMist.Timing.UI.Views.InCarDriverMode;

public partial class InCarPositions : UserControl
{
    public InCarPositions()
    {
        InitializeComponent();
    }

    // Keeping the screen awake is driven by InCarSettingsViewModel rather than this view's
    // loaded/unloaded events. That used to be because driver mode was only toggled with IsVisible,
    // leaving the view attached so OnUnloaded would never fire; it is now built from its view model
    // and so is genuinely destroyed on leaving driver mode. The wake lock still belongs to the view
    // model either way - it is released in InCarSettingsViewModel.Dispose, which the router calls -
    // and tying it to view lifetime would only make it depend on how the view happens to be hosted.
}
