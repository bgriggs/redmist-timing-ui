using System;

using Avalonia;
using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        CrashReporting.Init("desktop", o =>
        {
            o.DisableAppDomainUnhandledExceptionCapture();

            // No offline cache here. Sentry's cache is a file-backed queue with no documented
            // multi-process guarantee, and this head is launched once per event (see the event id
            // read from Args below), so two instances would race the same envelope files. Desktop
            // is a development target on a reliable network, which is where the cache buys least.
            o.CacheDirectoryPath = null;
        });
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

}
