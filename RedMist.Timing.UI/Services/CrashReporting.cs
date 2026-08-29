using Microsoft.Extensions.Configuration;
using Sentry;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Initializes Sentry crash and error reporting.
/// </summary>
/// <remarks>
/// Called from each platform head as early as it can be, which is well before the DI host exists,
/// so this reads the embedded configuration itself rather than waiting for
/// <see cref="App.OnFrameworkInitializationCompleted"/>. Startup faults are exactly the ones that
/// were previously invisible, so initialization cannot be deferred to the point where the app is
/// already running.
///
/// Nothing here throws. A reporting layer that can take the app down is worse than no reporting
/// layer, and this runs before any of the normal exception handling is in place.
///
/// The browser head deliberately does not call <see cref="Init"/>: the SDK is inert until then, so
/// the WebAssembly build carries the assembly but never starts the transport.
/// </remarks>
public static class CrashReporting
{
    private const string ConfigResourceName = "RedMist.Timing.UI.appsettings.json";
    private const string SecretsResourceName = "RedMist.Timing.UI.secrets.release.json";
    private static readonly object ReportedMarker = new();
    private static readonly ConditionalWeakTable<Exception, object> ReportedFatals = new();

    /// <summary>
    /// True once Sentry has been initialized with a DSN. False when reporting is switched off,
    /// which is the state for any build whose configuration leaves Sentry:Dsn empty.
    /// </summary>
    public static bool IsEnabled { get; private set; }

    /// <summary>
    /// Whether an unhandled exception on the UI thread should be allowed to terminate the process
    /// rather than being marked handled. Defaults to true; see the remarks on
    /// <c>App.OnUIThreadUnhandledException</c> for why.
    /// </summary>
    public static bool CrashOnUnhandledUiException { get; private set; } = true;

    /// <summary>
    /// Starts Sentry for the given platform head. Safe to call more than once; only the first call
    /// takes effect.
    /// </summary>
    /// <param name="platform">Value for the "platform" tag, e.g. "android". Used to split issues
    /// per head, since the same managed bug presents differently on each.</param>
    /// <param name="configurePlatform">Options only the calling head can set. The SDK ships a
    /// different assembly per target framework and their surfaces are not identical - the iOS build
    /// has no <c>DisableAppDomainUnhandledExceptionCapture</c>, offering
    /// <c>DisableRuntimeMarshalManagedExceptionCapture</c> instead - and this project compiles
    /// against the net10.0 one. Calling a method that only exists there would throw
    /// MissingMethodException on device, be swallowed by the catch below, and leave reporting
    /// silently off on that platform. Each head therefore supplies what only it can resolve.</param>
    public static void Init(string platform, Action<SentryOptions>? configurePlatform = null)
    {
        if (IsEnabled)
        {
            return;
        }

        try
        {
            var settings = ReadSettings(LoadConfiguration());
            CrashOnUnhandledUiException = settings.CrashOnUnhandledUiException;

            // No DSN means reporting is intentionally off - a developer build, or a release that
            // has not had the DSN provisioned yet. Leave the SDK uninitialized so every
            // SentrySdk.* call downstream is a no-op.
            if (!settings.ReportingEnabled)
            {
                return;
            }

            SentrySdk.Init(options =>
            {
                options.Dsn = settings.Dsn;
                options.Environment = settings.Environment;
                options.Release = GetRelease();
                options.Debug = settings.Debug;

                // Errors reach Sentry through ILogger, which is the path every catch block in the
                // app already uses. Sentry's own hook here would report the same fault a second
                // time, since the global handlers in App log everything they receive. The
                // equivalent hook for unhandled exceptions is per-platform, hence configurePlatform.
                options.DisableUnobservedTaskExceptionCapture();

                // The tombstones behind the unattributable crashes only ever showed libmonosgen
                // frames. ANR detection and the native/tombstone capture in the Android SDK are
                // the part that managed handlers structurally cannot see.
                options.AutoSessionTracking = true;

                // The telemetry surface is deliberately narrow: errors, breadcrumbs and release
                // health, nothing else. Each of the products below bills against its own quota, and
                // the app's job here is crash attribution. Stated explicitly rather than left to
                // defaults so that turning one on is a decision someone made rather than a default
                // that changed under us in an SDK upgrade.
                options.TracesSampleRate = null;   // no tracing, and no profiling with it
                options.EnableLogs = false;        // distinct from breadcrumbs; would be flooded by
                                                   // HubClient's per-second session-patch line
                options.EnableMetrics = false;
                options.DisableSystemDiagnosticsMetricsIntegration();

                // Without a cache directory an envelope lives only in memory, so a crash in the
                // paddock with no signal is queued, fails to flush, and dies with the process -
                // losing precisely the crashes that correlate with the reported bugs. With one, the
                // envelope is written to disk and delivered on a later run.
                //
                // InitCacheFlushTimeout is deliberately left at its default: making startup block
                // on delivering last run's cache would trade app-start latency on a phone at a
                // track for something the background transport does anyway.
                options.CacheDirectoryPath = GetCacheDirectory();

                options.SetBeforeSend(static (SentryEvent e) => ThrottleConnectivityNoise(e));

                configurePlatform?.Invoke(options);
            });

            // Set before configuring the scope: once Init returns the SDK is live, and a failure
            // tagging the scope must not leave Flush believing there is nothing to send.
            IsEnabled = true;
            SentrySdk.ConfigureScope(scope => scope.SetTag("platform", platform));
        }
        catch
        {
            // Reporting is best-effort. Losing it must never prevent the app from starting.
        }
    }

    /// <summary>
    /// The configured reporting settings, with defaults applied.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="Init"/> so the decisions can be tested against a supplied
    /// configuration. Testing them through Init would only ever assert whatever the developer
    /// machine's own appsettings and user secrets happen to hold.
    /// </remarks>
    internal readonly record struct Settings(
        string? Dsn,
        string Environment,
        bool Debug,
        bool CrashOnUnhandledUiException)
    {
        /// <summary>A blank or absent DSN means reporting is deliberately off.</summary>
        public bool ReportingEnabled => !string.IsNullOrWhiteSpace(Dsn);
    }

    internal static Settings ReadSettings(IConfiguration? config)
    {
        // Defaults to true: suppressing a UI-thread fault is what made the native crashes
        // unattributable, so it takes an explicit setting to go back to that.
        var crashOnUi = true;
        if (bool.TryParse(config?["Sentry:CrashOnUnhandledUiException"], out var parsed))
        {
            crashOnUi = parsed;
        }

        return new Settings(
            config?["Sentry:Dsn"],
            config?["Sentry:Environment"] ?? "production",
            bool.TryParse(config?["Sentry:Debug"], out var debug) && debug,
            crashOnUi);
    }

    /// <summary>
    /// Caps how many connectivity failures a single window may report.
    /// </summary>
    /// <remarks>
    /// HubClient reconnects on an infinite retry policy and logs an error per attempt, so a session
    /// on bad cell service - the normal state of a phone in a paddock - can produce hundreds of
    /// identical events. On the free tier that is a month's allowance in one weekend, and it buries
    /// the faults worth reading.
    ///
    /// Windowed rather than capped per session: a race day is one long session, and a cap would let
    /// an early blip consume the budget and hide a genuine outage hours later. The first few
    /// failures in any window still report, so a real backend outage is still visible.
    /// </remarks>
    private static readonly TimeSpan NoiseWindow = TimeSpan.FromMinutes(10);
    private const int MaxConnectivityEventsPerWindow = 3;
    private static readonly Lock NoiseGate = new();
    private static DateTime noiseWindowStart;
    private static int noiseInWindow;

    /// <summary>Clears the throttle window. Test-only; the app has no reason to reset it.</summary>
    internal static void ResetConnectivityThrottle()
    {
        lock (NoiseGate)
        {
            noiseWindowStart = default;
            noiseInWindow = 0;
        }
    }

    internal static SentryEvent? ThrottleConnectivityNoise(SentryEvent e)
    {
        // A crash is never noise, whatever its inner exception happens to be. Without this, a fatal
        // wrapping a TimeoutException that arrives after the window's budget is spent would be
        // dropped - the session would still end as crashed, leaving a fall in the crash-free rate
        // with no issue to explain it, which is the exact state this change exists to end.
        if (e.Level == SentryLevel.Fatal || e.SentryExceptions?.Any(x => x.Mechanism?.Handled == false) == true)
        {
            return e;
        }

        if (!IsConnectivityFailure(e.Exception))
        {
            return e;
        }

        lock (NoiseGate)
        {
            var now = DateTime.UtcNow;
            if (now - noiseWindowStart > NoiseWindow)
            {
                noiseWindowStart = now;
                noiseInWindow = 0;
            }

            noiseInWindow++;
            return noiseInWindow <= MaxConnectivityEventsPerWindow ? e : null;
        }
    }

    /// <summary>
    /// True for the transport-level failures that a flaky trackside network produces in bulk.
    /// </summary>
    internal static bool IsConnectivityFailure(Exception? exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is HttpRequestException or SocketException or WebSocketException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when every fault in the tree is a deliberate cancellation, i.e. nothing actually broke.
    /// </summary>
    /// <remarks>
    /// Cancellation is how the app stops background work, so reporting it as an error would bury
    /// the real faults. An HttpClient timeout is the trap here: it surfaces as a
    /// TaskCanceledException, which makes a naive "is this an OperationCanceledException" check
    /// discard the app's single most common genuine failure. .NET distinguishes the two by putting
    /// a TimeoutException inside the cancellation.
    /// </remarks>
    internal static bool IsDeliberateCancellation(AggregateException exception)
    {
        var faults = exception.Flatten().InnerExceptions;
        return faults.Count > 0 && faults.All(IsDeliberateCancellation);
    }

    private static bool IsDeliberateCancellation(Exception exception)
        => exception is OperationCanceledException
           && exception is not TaskCanceledException { InnerException: TimeoutException };

    /// <summary>
    /// Directory Sentry writes undelivered envelopes to. Null when no writable location is
    /// available, which turns caching off rather than failing initialization.
    /// </summary>
    private static string? GetCacheDirectory()
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var path = Path.Combine(root, "RedMist", "sentry");
            Directory.CreateDirectory(path);
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reports an exception that is about to end the process, marked as unhandled and terminal.
    /// </summary>
    /// <remarks>
    /// The ordinary reporting path is <see cref="ILogger"/>, but everything arriving that way is
    /// stamped handled - <c>SentryLogger</c> sets the mechanism itself. That leaves nothing marked
    /// fatal and no session ever ending as crashed, so the crash-free rate would read close to 100%
    /// while the app was crash-looping. Terminal paths call this instead, which is the only way to
    /// get an accurate release-health number.
    /// </remarks>
    /// <param name="mechanism">Where the fault surfaced, e.g. the dispatcher or the AppDomain. Kept
    /// distinct so the two are separable in Sentry.</param>
    public static void CaptureFatal(Exception exception, string mechanism)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            // A UI-thread fault that is allowed to terminate carries on to the AppDomain handler,
            // so without this the same crash reports twice and ends two sessions. Held outside the
            // exception rather than in its Data: Data is virtual, and a type that returns null or a
            // fixed dictionary would throw here and lose the report entirely - the one failure mode
            // this whole change exists to remove.
            if (!ReportedFatals.TryAdd(exception, ReportedMarker))
            {
                return;
            }

            // Names the mechanism; the handled flag comes from the capture call below, which
            // documents itself as overriding whatever the mechanism set.
            exception.SetSentryMechanism(mechanism, handled: false);

            // terminal: true is what ends the session as crashed.
            SentrySdk.CaptureException(exception, handled: false, terminal: true);
        }
        catch
        {
            // The caller is on its way to terminating; there is nothing useful to do here.
        }
    }

    /// <summary>
    /// Reports a fault that was caught and handled, for callers with no ILogger to hand.
    /// </summary>
    /// <remarks>
    /// Used by the dispatcher marshalling helpers, which are static extension methods and predate
    /// the logging pipeline. Everything else should report through ILogger so the fault also
    /// reaches the on-device display.
    /// </remarks>
    public static void CaptureHandled(Exception exception, string mechanism)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            exception.SetSentryMechanism(mechanism, handled: true);
            SentrySdk.CaptureException(exception);
        }
        catch
        {
            // Reporting is best-effort and must never replace the fault it is reporting on.
        }
    }

    /// <summary>
    /// Blocks until queued events have been sent, or the timeout expires.
    /// </summary>
    /// <remarks>
    /// Only worth calling on a path that is about to end the process. Everywhere else the
    /// background queue delivers on its own and this would just stall the caller.
    /// </remarks>
    public static void Flush(TimeSpan timeout)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            SentrySdk.Flush(timeout);
        }
        catch
        {
            // Nothing useful to do: the caller is on its way to terminating.
        }
    }

    /// <summary>
    /// Reads the same configuration the DI host later loads, layered in the same order, so the DSN
    /// can live in appsettings.json, in user secrets for local work, or in the CI-generated secrets
    /// file alongside the Keycloak credentials.
    /// </summary>
    private static IConfigurationRoot? LoadConfiguration()
    {
        var assembly = typeof(CrashReporting).Assembly;
        var builder = new ConfigurationBuilder();

        using var stream = assembly.GetManifestResourceStream(ConfigResourceName);
        if (stream is null)
        {
            return null;
        }

        builder.AddJsonStream(stream);

        // Layered in the same order App uses. Without this a DSN set with `dotnet user-secrets`
        // reaches the DI container but never the SDK, so a developer sees it in IConfiguration and
        // no events in Sentry.
        builder.AddUserSecrets(assembly);

#if RELEASE
        using var secretsStream = assembly.GetManifestResourceStream(SecretsResourceName);
        if (secretsStream is not null)
        {
            builder.AddJsonStream(secretsStream);
        }
#endif

        return builder.Build();
    }

    /// <summary>
    /// Release identifier used to group issues by shipped build. Matches SharedVersion from
    /// Directory.Build.props, which is what the store listings and the version check report.
    /// </summary>
    private static string GetRelease()
    {
        var assembly = typeof(CrashReporting).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString() ?? "unknown";

        // Strip the source-revision suffix the SDK appends (e.g. "1.0.98+abc1234") so a rebuild of
        // the same version does not fragment into separate releases.
        var plus = version.IndexOf('+');
        if (plus > 0)
        {
            version = version[..plus];
        }

        return $"redmist-timing@{version}";
    }
}
