using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
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
