using Avalonia;
using Avalonia.Browser;
using RedMist.Timing.UI;
using System.Threading.Tasks;

internal sealed partial class Program
{
    // Deliberately does not call CrashReporting.Init. The Sentry SDK stays inert until Init,
    // so the assembly rides along in the WebAssembly build without starting a transport that
    // is not supported on browser-wasm. Errors here are still visible in the browser console
    // and the in-app diagnostic display.
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}