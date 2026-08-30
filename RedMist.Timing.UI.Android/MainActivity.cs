using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using Avalonia;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using Sentry;
using System;

namespace RedMist.Timing.UI.Android;

[Activity(
    Label = "Red Mist Timing",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    /// <summary>
    /// The most recently created activity, so <see cref="DetachMainViewFromPreviousActivity"/> can
    /// finish a stale one. Weak because Android owns the activity's lifetime, not this class.
    /// </summary>
    private static WeakReference<MainActivity>? _liveActivity;

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

        // base.OnCreate is where Avalonia hands the shared MainView to a brand new AvaloniaView, and
        // it throws outright if the view still has a parent. Under the default launch mode a second
        // launch intent can put a second MainActivity alongside a live one, so this is the whole
        // defense against that.
        //
        // Gated on having seen an activity before, and deliberately not on Avalonia's own state:
        // reading Application.Current here reaches AvaloniaLocator before base.OnCreate has set the
        // framework up, and that left the app running but never loading its view - a blank window,
        // no crash, no log. On the first activity in a process there is nothing to detach anyway.
        if (_liveActivity is not null)
        {
            DetachMainViewFromPreviousActivity();
        }

        base.OnCreate(savedInstanceState);

        // Use the modern OnBackPressedDispatcher API for Android 13+
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            _backPressedCallback = new BackPressedCallback(this);
            OnBackPressedDispatcher.AddCallback(this, _backPressedCallback);
        }

        _liveActivity = new WeakReference<MainActivity>(this);
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

    /// <summary>
    /// Releases the process-wide MainView from an earlier activity's Avalonia view, if one still holds it.
    /// </summary>
    /// <remarks>
    /// Avalonia stores its <c>SingleViewLifetime</c> in a static, so a second OnCreate in the same
    /// process reuses the MainView instance rather than building a new one. That is fine when the
    /// previous activity has already been destroyed - Avalonia clears its own view's Content in
    /// OnDestroy - but not when the two activities overlap, which is where
    /// "already has a visual parent ... EmbeddableControlRoot" came from. Clearing the old host's
    /// Content is what actually detaches the view; the old activity is on its way out regardless.
    /// </remarks>
    private void DetachMainViewFromPreviousActivity()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime
                is not ISingleViewApplicationLifetime { MainView: { } mainView })
            {
                return;
            }

            if (mainView.GetVisualParent() is null)
            {
                return;
            }

            // Normally the root is reachable through the visual tree. A presenter that was orphaned
            // from its root still reports a visual parent but no visual root, so fall back to the
            // logical parent, which is the same EmbeddableControlRoot either way.
            var host = mainView.GetVisualRoot() as ContentControl ?? mainView.Parent as ContentControl;
            if (host is null || !ReferenceEquals(host.Content, mainView))
            {
                return;
            }

            host.Content = null;

            // A breadcrumb alone would only ever surface attached to some later crash, and the whole
            // point of this path is that no crash follows. Reported as its own event so a quiet
            // REDMIST-APP-3 can be told apart from a guard that is quietly load-bearing.
            SentrySdk.CaptureMessage(
                "MainView was still parented to a previous activity at startup",
                scope => scope.SetTag("handler", "MainActivity.OnCreate"),
                SentryLevel.Warning);

            // The previous activity is now showing an empty window and has no way to repopulate it,
            // so send it on its way rather than leaving a blank task behind in Recents.
            if (_liveActivity is not null
                && _liveActivity.TryGetTarget(out var previous)
                && !ReferenceEquals(previous, this)
                && !previous.IsFinishing)
            {
                previous.Finish();
            }
        }
        catch (Exception ex)
        {
            // Never let the guard be the thing that stops the app from starting.
            CrashReporting.CaptureHandled(ex, "MainActivity.DetachMainViewFromPreviousActivity");
        }
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
