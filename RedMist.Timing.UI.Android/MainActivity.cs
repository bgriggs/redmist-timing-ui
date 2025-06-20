using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Microsoft.Maui.ApplicationModel;
using RedMist.Timing.UI.ViewModels;

namespace RedMist.Timing.UI.Android;

[Activity(
    Label = "Red Mist Timing",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Platform.Init(this, savedInstanceState);
        //OnBackPressedDispatcher.AddCallback(this, new MyBackPressedCallback(true));
    }

    public override void OnBackPressed()
    {
        if (App.Current is App app)
        {
            var mainVm = app.GetService<MainViewModel>();
            bool handled = mainVm.HandleDeviceBackButton();
            if (!handled)
            {
                base.OnBackPressed();
            }
        }
        else
        {
            base.OnBackPressed();
        }
    }
}
