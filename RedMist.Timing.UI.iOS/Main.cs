using Sentry;
using UIKit;
using RedMist.Timing.UI.iOS;

// This is the main entry point of the application.
// If you want to use a different Application Delegate class from "AppDelegate"
// you can specify it here.
// Before UIApplication.Main so startup faults are reported too.
// The iOS build of the SDK has no DisableAppDomainUnhandledExceptionCapture; this is its
// equivalent. Set here because only this head compiles against the iOS Sentry assembly.
RedMist.Timing.UI.Services.CrashReporting.Init("ios", o =>
{
    o.DisableRuntimeMarshalManagedExceptionCapture();

    // LocalApplicationData resolves to Documents on iOS - iCloud-backed, user-visible, and
    // reserved for data that cannot be regenerated. A delivery queue there is a documented App
    // Store rejection. InternetCache resolves to Library/Caches, which is excluded from backup.
    o.CacheDirectoryPath =
        RedMist.Timing.UI.Services.CrashReporting.BuildCacheDirectory(
            System.Environment.SpecialFolder.InternetCache);
});

UIApplication.Main(args, null, typeof(AppDelegate));
