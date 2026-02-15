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
    private const string KEEP_SCREEN_ON_KEY = "KeepScreenOn";
    private readonly IPreferencesService _preferencesService;
    private readonly IScreenWakeService _screenWakeService;

    [ObservableProperty]
    private string selectedTheme = "System Default";

    [ObservableProperty]
    private bool keepScreenOn;

    /// <summary>
    /// Indicates whether the current platform is a mobile device (Android or iOS).
    /// Used to control visibility of mobile-only settings.
    /// </summary>
    [ObservableProperty]
    private bool isMobileDevice;

    public List<string> ThemeOptions { get; } =
    [
        "System Default",
        "Light",
        "Dark"
    ];

    public SettingsViewModel(IPreferencesService preferencesService, IScreenWakeService screenWakeService, bool isMobileDevice)
    {
        _preferencesService = preferencesService;
        _screenWakeService = screenWakeService;
        IsMobileDevice = isMobileDevice;
        LoadTheme();
        LoadKeepScreenOn();
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

    partial void OnKeepScreenOnChanged(bool value)
    {
        _screenWakeService.SetKeepScreenOn(value);
        SaveKeepScreenOn();
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

    private void LoadKeepScreenOn()
    {
        try
        {
            KeepScreenOn = _preferencesService.Get(KEEP_SCREEN_ON_KEY, false);
        }
        catch
        {
            // Ignore errors in loading keep screen on preference
        }
    }

    private void SaveKeepScreenOn()
    {
        try
        {
            _preferencesService.Set(KEEP_SCREEN_ON_KEY, KeepScreenOn);
        }
        catch
        {
            // Ignore errors in saving keep screen on preference
        }
    }
}
