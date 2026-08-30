using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using AndroidX.Activity;
using AndroidX.Core.View;
using Avalonia;
using Avalonia.Android;
using Avalonia.Styling;
using Avalonia.Media;
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
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App>
{
    /// <summary>
    /// The most recently created activity, so <see cref="DetachMainViewFromPreviousActivity"/> can
    /// finish a stale one. Weak because Android owns the activity's lifetime, not this class.
    /// </summary>
    private static WeakReference<MainActivity>? _liveActivity;

    private OnBackPressedCallback? _backPressedCallback;
    private bool _systemBarFailureReported;

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
        // it throws outright if the view still has a parent. SingleTop above is what now keeps a
        // second launch intent from putting a second MainActivity on a live one, so this is the
        // backstop rather than the whole defense. What it still covers is a second instance the
        // launch mode cannot dedupe: FLAG_ACTIVITY_MULTIPLE_TASK, a second display, a second task.
        // Explicitly not recreation - that destroys the old activity first and Avalonia clears its
        // own view's Content in OnDestroy, so the check below finds nothing to do and returns.
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

        // The in-app Light/Dark setting changes the theme without any Android configuration
        // change, so the bars have to follow Avalonia's notion of the theme, not the system's.
        if (Avalonia.Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        }

        ApplySystemBarColors();
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

    protected override void OnResume()
    {
        base.OnResume();

        // Reapplied rather than set once: Avalonia re-runs its edge-to-edge setup whenever the
        // insets preference is set, which puts the scrim back. Resume is guaranteed and comes
        // first; the focus hook below covers a window that is recreated without being focused,
        // such as the unfocused half of a split screen.
        ApplySystemBarColors();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);

        if (hasFocus)
        {
            ApplySystemBarColors();
        }
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);

        // The activity handles UiMode itself, so a switch to the system's dark theme arrives here
        // rather than as a recreation, and nothing else would repaint the bars.
        ApplySystemBarColors();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => ApplySystemBarColors();

    /// <summary>
    /// Paints the status and navigation bars in the app's own chrome color.
    /// </summary>
    /// <remarks>
    /// Avalonia asks for edge to edge by adding FLAG_TRANSLUCENT_STATUS and
    /// FLAG_TRANSLUCENT_NAVIGATION, which has the system dim both bars with its own scrim - the
    /// grey bands above the header and below the tab strip. Its SystemBarColor property cannot
    /// undo that, because Avalonia ignores that property whenever edge to edge is on.
    ///
    /// Painting the bars rather than drawing under them is deliberate. Measured on this app, the
    /// Avalonia surface is 720x1471 on a 720x1600 screen, so the window stops at the bars whatever
    /// the edge-to-edge preference says and nothing the app draws can reach them. Matching their
    /// color to the chrome beside them is what closes the seam.
    ///
    /// Only tested below API 35. From 35 the system forces edge to edge, ignores both setters
    /// below, and keeps the bars transparent, so the strips show whatever ends up behind the
    /// window instead - which is a different problem than the one this solves.
    /// </remarks>
    private void ApplySystemBarColors()
    {
        try
        {
            if (Window is not { } window || ResolveChrome() is not { } chrome)
            {
                return;
            }

            window.ClearFlags(WindowManagerFlags.TranslucentStatus);
            window.ClearFlags(WindowManagerFlags.TranslucentNavigation);
            window.AddFlags(WindowManagerFlags.DrawsSystemBarBackgrounds);

#pragma warning disable CA1422 // Disabled from API 35, where the system owns the bars - see the remarks.
            window.SetStatusBarColor(chrome.Color);

            // The navigation bar's icons can only be darkened from API 26, so below that a light
            // bar would be light glyphs on light paint. Leaving it alone is the lesser evil.
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                window.SetNavigationBarColor(chrome.Color);
            }
#pragma warning restore CA1422

            // Otherwise the system puts its own scrim back over the navigation bar.
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                window.NavigationBarContrastEnforced = false;
            }

            // The bars now carry the chrome color, so their icons have to contrast with that.
            if (WindowCompat.GetInsetsController(window, window.DecorView) is { } controller)
            {
                controller.AppearanceLightStatusBars = !chrome.IsDark;
                controller.AppearanceLightNavigationBars = !chrome.IsDark;
            }
        }
        catch (Exception ex)
        {
            // Cosmetic: a failure here costs the blended bars, not the app. Reported once, because
            // this runs on every resume and focus gain and a structural failure would repeat for
            // as long as the app is open.
            if (!_systemBarFailureReported)
            {
                _systemBarFailureReported = true;
                CrashReporting.CaptureHandled(ex, "MainActivity.ApplySystemBarColors");
            }
        }
    }

    /// <summary>
    /// Reads the brush the header and tab strip are painted with, in whichever theme is currently resolved.
    /// </summary>
    /// <remarks>
    /// Taken from Avalonia rather than from an Android night-qualified resource, because the two
    /// disagree: the app carries its own Light/Dark/System setting, so a user on a light phone can
    /// be running the dark theme. Reading the same brush the chrome reads means the bars cannot
    /// drift from it, and there is no second copy of the color to keep in step.
    /// </remarks>
    private static (global::Android.Graphics.Color Color, bool IsDark)? ResolveChrome()
    {
        if (Avalonia.Application.Current is not { } app)
        {
            return null;
        }

        var variant = app.ActualThemeVariant;
        if (!app.TryGetResource("medAppBackground", variant, out var value) || value is not ISolidColorBrush brush)
        {
            return null;
        }

        var c = brush.Color;
        return (global::Android.Graphics.Color.Argb(c.A, c.R, c.G, c.B), variant == ThemeVariant.Dark);
    }

    protected override void OnDestroy()
    {
        if (Avalonia.Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        }

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

            // This was its own event for one release, to settle whether the guard was load-bearing or
            // dead code. It was load-bearing, and the answer came back narrower than expected: every
            // report was an automated store-review emulator, where an explicit component start puts a
            // second MainActivity on top of a live one. Reproduced on a device that way, and never
            // once from an ordinary relaunch, which reuses the existing activity. SingleTop now stops
            // that at the source - verified against the same repro - so this should go quiet, and it
            // drops out of the issue feed rather than opening a fresh issue every release.
            SentrySdk.AddBreadcrumb(
                "MainView was still parented to a previous activity at startup",
                category: "app.lifecycle",
                level: BreadcrumbLevel.Warning);

            // The breadcrumb alone would not be enough to check that claim later: breadcrumbs cannot
            // be searched or counted, and the launch-mode fix and this downgrade ship together, so a
            // quiet REDMIST-APP-F on its own cannot tell a working fix from having stopped looking.
            // A tag can be aggregated, and rides on any later event from this process.
            SentrySdk.ConfigureScope(scope => scope.SetTag("mainview-detach", "fired"));

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
