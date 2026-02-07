using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RedMist.Timing.UI.Services;
using System.Collections.Generic;

namespace RedMist.Timing.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string THEME_KEY = "AppTheme";
    private readonly IPreferencesService _preferencesService;

    [ObservableProperty]
    private string selectedTheme = "System Default";

    public List<string> ThemeOptions { get; } = new()
    {
        "System Default",
        "Light",
        "Dark"
    };

    public SettingsViewModel(IPreferencesService preferencesService)
    {
        _preferencesService = preferencesService;
        LoadTheme();
    }

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
        SaveTheme();
    }

    private void LoadTheme()
    {
        try
        {
            var savedTheme = _preferencesService.Get(THEME_KEY, "System Default");
            if (ThemeOptions.Contains(savedTheme))
            {
                SelectedTheme = savedTheme;
            }
        }
        catch
        {
            // Ignore errors in loading theme preference
        }
    }

    private void SaveTheme()
    {
        try
        {
            _preferencesService.Set(THEME_KEY, SelectedTheme);
        }
        catch
        {
            // Ignore errors in saving theme preference
        }
    }
}
