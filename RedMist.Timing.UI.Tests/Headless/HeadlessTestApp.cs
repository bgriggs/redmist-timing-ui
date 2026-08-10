using Avalonia;
using Avalonia.Headless;
using RedMist.Timing.UI.Tests.Headless;

[assembly: AvaloniaTestApplication(typeof(HeadlessTestApp))]

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// The application the headless dispatcher runs under.
/// </summary>
/// <remarks>
/// Deliberately not the real <c>App</c>: that one builds the whole DI host, reads embedded
/// configuration and starts background services on framework initialization. None of that is
/// needed to get a working dispatcher, and all of it would make these tests slow and fragile.
///
/// It also loads no styles or resource dictionaries, which is deliberate rather than an oversight.
/// Every themed lookup therefore misses, which is exactly the condition
/// <see cref="ResourceFallbackTests"/> needs. The trade is that a test which renders a real car row
/// gets fallback colors rather than the app's - fine for asserting behavior, not for asserting
/// appearance. If a future test needs the real brushes, give it its own application type rather
/// than adding resources here, or the fallback tests stop testing anything.
/// </remarks>
public class HeadlessTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>()
            // Drawing is on by default; stated explicitly because tests that capture a rendered
            // frame depend on it and would fail obscurely if it were ever turned off.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
