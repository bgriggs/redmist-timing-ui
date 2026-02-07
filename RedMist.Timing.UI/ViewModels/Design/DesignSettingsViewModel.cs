using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignSettingsViewModel : SettingsViewModel
{
    public DesignSettingsViewModel() : base(new MockPreferencesService())
    {
        SelectedTheme = "Dark";
    }
}
