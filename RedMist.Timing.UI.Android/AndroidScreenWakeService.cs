using Android.App;
using Android.Views;
using RedMist.Timing.UI.Services;
using System;

namespace RedMist.Timing.UI.Android;

/// <summary>
/// Android implementation of screen wake service using WindowManager flags.
/// </summary>
/// <remarks>
/// The activity is resolved per call rather than captured in the constructor.
/// FLAG_KEEP_SCREEN_ON belongs to a window, not to the process, and this service is a DI singleton
/// built once during Avalonia's startup - so capturing the activity that happened to be current
/// then left every later call setting the flag on the first window the app ever had. That fails
/// silently: the call still succeeds, it just lands on a window nobody is looking at.
///
/// The requested state is static so <see cref="ReapplyTo"/> can be a static entry point the activity
/// calls directly, rather than having to resolve this service out of the container from a lifecycle
/// callback.
/// </remarks>
public class AndroidScreenWakeService(Func<Activity?> currentActivity) : IScreenWakeService
{
    private static volatile bool keepScreenOn;
    private static bool applyFailureReported;

    public void SetKeepScreenOn(bool keepOn)
    {
        // Recorded before the lookup below, so a call that arrives while there is no usable window
        // still takes effect as soon as one appears.
        keepScreenOn = keepOn;

        // Null while the process is starting, through an activity handover, and for as long as the
        // app has no live activity at all. The first two are picked up by the resume hook; in the
        // third there is no window to keep awake in the first place.
        if (currentActivity() is { } activity)
        {
            Apply(activity, keepOn);
        }
    }

    /// <summary>
    /// Puts the requested state back onto <paramref name="activity"/>'s window.
    /// </summary>
    /// <remarks>
    /// Called when an activity takes over, because the flag does not travel with it. Without this a
    /// driver in in-car mode would hold the screen awake right up until the activity was replaced
    /// and then quietly stop - the worst moment for it to happen, and nothing in the log to say so.
    /// It also covers a configuration change the activity does not declare, such as a locale or font
    /// scale change, which rebuilds the window the same way.
    /// </remarks>
    internal static void ReapplyTo(Activity activity) => Apply(activity, keepScreenOn);

    private static void Apply(Activity activity, bool keepOn)
    {
        try
        {
            // RunOnUiThread runs this inline when the caller is already on the UI thread, which both
            // the resume hook and the view models are - so the catch below is load-bearing rather
            // than decorative: without it a throw here leaves through a lifecycle callback.
            activity.RunOnUiThread(() =>
            {
                if (keepOn)
                {
                    activity.Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
                }
                else
                {
                    activity.Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
                }
            });
        }
        catch (Exception ex)
        {
            // Losing this costs a screen that dims, which is worth far less than the app staying up.
            // Reported once because resume runs it constantly and a structural failure would repeat
            // for as long as the app is open.
            if (!applyFailureReported)
            {
                applyFailureReported = true;
                CrashReporting.CaptureHandled(ex, "AndroidScreenWakeService.Apply");
            }
        }
    }
}
