using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;

namespace RedMist.Timing.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string selectedTheme = "System Default";

    public List<string> ThemeOptions { get; } = new()
    {
        "System Default",
        "Light",
        "Dark"
    };

    [RelayCommand]
    private void ApplyTheme()
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = SelectedTheme switch
            {
                "Light" => ThemeVariant.Light,
                "Dark" => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
    }

    partial void OnSelectedThemeChanged(string value)
    {
        ApplyTheme();
    }
}
