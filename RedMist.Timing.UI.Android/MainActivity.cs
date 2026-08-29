using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using Avalonia;
using Avalonia.Android;
using RedMist.Timing.UI.Services;
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
    private OnBackPressedCallback? _backPressedCallback;

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        App.ScreenWakeServiceFactory = () => new AndroidScreenWakeService(this);
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Before base.OnCreate, which is where Avalonia starts: initializing here means the Android
        // SDK's native crash and tombstone capture is armed for the whole of startup, which is
        // where the unattributable libmonosgen crashes were happening.
        CrashReporting.Init("android", o => o.DisableAppDomainUnhandledExceptionCapture());

        base.OnCreate(savedInstanceState);

        // Use the modern OnBackPressedDispatcher API for Android 13+
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            _backPressedCallback = new BackPressedCallback(this);
            OnBackPressedDispatcher.AddCallback(this, _backPressedCallback);
        }
    }

#pragma warning disable CS0672 // Member overrides obsolete member
#pragma warning disable CA1422 // Validate platform compatibility
    public override void OnBackPressed()
    {
        // This method is still called on Android < 13
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            HandleBackPress();
        }
        else
        {
            base.OnBackPressed();
        }
    }
#pragma warning restore CA1422 // Validate platform compatibility
#pragma warning restore CS0672 // Member overrides obsolete member

    private void HandleBackPress()
    {
        if (App.Current is App app
            && app.GetService<MainViewModel>().HandleDeviceBackButton())
        {
            return;
        }

        MoveTaskToBack(true);
    }

    protected override void OnDestroy()
    {
        _backPressedCallback?.Remove();
        base.OnDestroy();
    }

    private class BackPressedCallback : OnBackPressedCallback
    {
        private readonly MainActivity _activity;

        public BackPressedCallback(MainActivity activity) : base(true)
        {
            _activity = activity;
        }

        public override void HandleOnBackPressed()
        {
            if (App.Current is App app
                && app.GetService<MainViewModel>().HandleDeviceBackButton())
            {
                return;
            }

            // At the root with nothing to navigate back to. Don't re-invoke the
            // dispatcher: that falls through ComponentActivity's default lambda
            // into Activity.onBackPressed/onBackInvoked while this callback is
            // still registered, which crashes on some Android 14/15 ROMs.
            // Instead, mimic the typical launcher-app back behavior directly.
            _activity.MoveTaskToBack(true);
        }
    }
}
