using Avalonia;
using Avalonia.Browser;
using RedMist.Timing.UI;
using RedMist.Timing.UI.Services;
using System.Threading.Tasks;

internal sealed partial class Program
{
    // Deliberately does not call CrashReporting.Init. The Sentry SDK stays inert until Init,
    // so the assembly rides along in the WebAssembly build without starting a transport that
    // is not supported on browser-wasm. Errors here are still visible in the browser console
    // and the in-app diagnostic display.
    private static Task Main(string[] args)
    {
        // The browser's own share sheet. Set before the app starts, the same way the other heads set
        // their platform services; the sheet itself does not touch JavaScript until it is asked to,
        // which is well after main.js has been imported.
        App.ShareSheetFactory = () => new BrowserShareSheet();

        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
