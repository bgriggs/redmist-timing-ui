using Microsoft.Extensions.Configuration;
using Sentry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
                // Heads whose platform needs a different root, or no cache at all, override this
                // through configurePlatform below.
                options.CacheDirectoryPath = BuildCacheDirectory(Environment.SpecialFolder.LocalApplicationData);

                options.SetBeforeSend(static (SentryEvent e) => ApplyNoisePolicy(e));

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

    /// <summary>
    /// The fingerprint every failure to reach the server is filed under, so they group as one issue.
    /// </summary>
    /// <remarks>
    /// Sentry groups on the stack and the message, and these arrive from every call site in the app
    /// with the generic argument baked into the frame - <c>GetAsync&lt;SessionState&gt;</c>,
    /// <c>GetAsync&lt;List&lt;Session&gt;&gt;</c>, and so on - worded differently by each platform:
    /// "Connection failure", "The network connection was lost.", "Unable to resolve host". One race
    /// weekend produced thirty-odd separate issues that all meant the phone had no usable network,
    /// which is what pushed a real layout crash to the fortieth row of the issue list.
    ///
    /// Grouping them costs the ability to see which call site noticed the network was gone first,
    /// which is not something anyone has needed to know. What is deliberately not grouped in with
    /// them is anything the server actually answered - see <see cref="IsConnectivityFailure"/> - so
    /// a 429 or a 502 still raises an issue of its own, which is how the rate-limit storm this
    /// policy was written after was found in the first place.
    /// </remarks>
    private const string ConnectivityFingerprint = "connectivity-failure";

    /// <summary>
    /// The fingerprint for a request that was never answered in time, kept apart from the one above.
    /// </summary>
    /// <remarks>
    /// A timeout says the server did not answer, which is a statement about the server; the rest of
    /// the connectivity bucket says the phone had nothing to ask over, which is a statement about
    /// the paddock. Filing them together would bury a backend that has started timing out under the
    /// ambient signal loss of a race weekend, and the two want opposite responses.
    /// </remarks>
    private const string TimeoutFingerprint = "request-timeout";

    private static readonly Lock NoiseGate = new();
    private static readonly Dictionary<string, (DateTime WindowStart, int Count)> NoiseWindows = [];

    [ThreadStatic]
    private static bool reportingUnhandledFault;

    /// <summary>
    /// Marks whatever is reported inside the returned scope as a fault that reached a global
    /// unhandled handler, which <see cref="ApplyNoisePolicy"/> then declines to classify as noise.
    /// </summary>
    /// <remarks>
    /// Thread-static because Sentry runs BeforeSend synchronously on the thread that captured the
    /// event, so the flag is still set when the policy reads it and cannot leak into another
    /// thread's events. A scope object rather than a pair of calls so an exception thrown by the
    /// logger cannot leave the flag stuck on for everything that thread reports afterwards.
    /// </remarks>
    public static UnhandledFaultScope ReportingUnhandledFault()
    {
        var scope = new UnhandledFaultScope(reportingUnhandledFault);
        reportingUnhandledFault = true;
        return scope;
    }

    /// <summary>Restores the flag <see cref="ReportingUnhandledFault"/> replaced.</summary>
    public readonly struct UnhandledFaultScope(bool previous) : IDisposable
    {
        public void Dispose() => reportingUnhandledFault = previous;
    }

    /// <summary>Clears the throttle windows. Test-only; the app has no reason to reset them.</summary>
    internal static void ResetConnectivityThrottle()
    {
        lock (NoiseGate)
        {
            NoiseWindows.Clear();
        }
    }

    /// <summary>
    /// Decides what an event is worth before it is sent: dropped, grouped and rationed, or left alone.
    /// </summary>
    /// <returns>The event to send, or null to drop it.</returns>
    internal static SentryEvent? ApplyNoisePolicy(SentryEvent e)
    {
        if (IsAlwaysReported(e))
        {
            return e;
        }

        // Cancellation is the app stopping its own work - a car row collapsed while its laps were
        // still loading, or the user left the event - so nothing failed and there is nothing to
        // report. Dropped rather than rationed for that reason: a ration keeps what is worth seeing
        // occasionally, and this never is. It still reaches the on-device log and rides along as a
        // breadcrumb, which is where a cancellation that turns out to be a bug would be read.
        if (IsDeliberateCancellation(e.Exception))
        {
            return null;
        }

        if (NoiseFingerprintFor(e.Exception) is not string fingerprint)
        {
            return e;
        }

        e.SetFingerprint([fingerprint]);
        return WithinWindow(fingerprint) ? e : null;
    }

    /// <summary>
    /// True for the events no amount of noise may suppress.
    /// </summary>
    /// <remarks>
    /// A crash is never noise, whatever its inner exception happens to be. Without this, a fatal
    /// wrapping a TimeoutException arriving after the window's budget is spent would be dropped -
    /// the session would still end as crashed, leaving a fall in the crash-free rate with no issue
    /// to explain it, which is the exact state this policy exists to end. Such an event keeps its
    /// own grouping too: a crash is not one of a kind with a lost connection.
    ///
    /// The third case is a fault that reached one of App's global handlers. Those report through
    /// ILogger, so the event arrives stamped handled and at Error - indistinguishable, by anything
    /// on the event itself, from a fault a catch block chose to log. The distinction matters: a
    /// cancellation caught around an HTTP call is routine, and the same cancellation escaping to
    /// the top of the UI thread is a bug. Without this, turning off
    /// <c>Sentry:CrashOnUnhandledUiException</c> - which is the switch used to keep an app usable
    /// while a crash loop is investigated at an event - would silently take those with it.
    /// </remarks>
    private static bool IsAlwaysReported(SentryEvent e)
        => e.Level == SentryLevel.Fatal
           || reportingUnhandledFault
           || e.SentryExceptions?.Any(x => x.Mechanism?.Handled == false) == true;

    /// <summary>
    /// The fingerprint to file a fault under when it is the sort that arrives in bulk, or null when
    /// it is a fault worth reading on its own terms.
    /// </summary>
    private static string? NoiseFingerprintFor(Exception? exception)
    {
        if (!IsConnectivityFailure(exception))
        {
            return null;
        }

        return Chain(exception).Any(x => x is TimeoutException) ? TimeoutFingerprint : ConnectivityFingerprint;
    }

    /// <summary>Whether this fingerprint still has budget in the current window.</summary>
    private static bool WithinWindow(string fingerprint)
    {
        lock (NoiseGate)
        {
            var now = DateTime.UtcNow;

            // Per fingerprint, not per process. Shared, the ambient signal loss of a race weekend
            // spends the whole budget and a backend that starts timing out adds nothing visible to
            // it - which would make the grouping above a way to hide an outage rather than find it.
            if (!NoiseWindows.TryGetValue(fingerprint, out var window) || now - window.WindowStart > NoiseWindow)
            {
                window = (now, 0);
            }

            window.Count++;
            NoiseWindows[fingerprint] = window;
            return window.Count <= MaxConnectivityEventsPerWindow;
        }
    }

    /// <summary>
    /// True for the failures to reach the server that a flaky trackside network produces in bulk.
    /// </summary>
    /// <remarks>
    /// A status code is the discriminator that matters here, and it is checked first. RestSharp
    /// reports a response the server did send but that was not a success as an HttpRequestException
    /// - "Request failed with status code TooManyRequests" - which is the same type a phone with no
    /// signal produces, and .NET separates them by populating StatusCode only when there was a
    /// response to take it from. Without that check the entire 5xx and 429 surface would be filed as
    /// the phone's fault and rationed away, which is precisely how a rate-limit storm goes unnoticed.
    ///
    /// WebException is named explicitly because it is related to none of the others - it derives
    /// from InvalidOperationException - and it is how Android words the two failures a phone leaving
    /// coverage produces most: "Unable to resolve host" and "Connection reset".
    ///
    /// An AggregateException has to hold nothing but these, rather than merely lead with one:
    /// walking InnerException alone reaches only its first fault, so a real bug travelling beside a
    /// socket error would be filed under a title saying the network dropped.
    /// </remarks>
    internal static bool IsConnectivityFailure(Exception? exception)
    {
        if (exception is null)
        {
            return false;
        }

        if (Chain(exception).Any(x => x is HttpRequestException { StatusCode: not null }))
        {
            return false;
        }

        if (exception is AggregateException aggregate)
        {
            var faults = aggregate.Flatten().InnerExceptions;
            return faults.Count > 0 && faults.All(IsConnectivityFailure);
        }

        return Chain(exception).Any(x => x is HttpRequestException or SocketException or WebSocketException
                                              or TimeoutException or WebException);
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
        return faults.Count > 0 && faults.All(IsCancellationFault);
    }

    /// <summary>
    /// True when a single fault is a cancellation rather than something breaking.
    /// </summary>
    /// <remarks>
    /// The counterpart to the <see cref="AggregateException"/> overload above, for the faults that
    /// arrive one at a time through ILogger rather than through the unobserved-task handler.
    ///
    /// The whole chain is searched rather than the outermost fault, because on the app's own HTTP
    /// stack the cancellation is never outermost: RestSharp reports an aborted request as an
    /// HttpRequestException("Request aborted") wrapping the TaskCanceledException, and iOS as an
    /// OperationCanceledException wrapping NSURLError -999. Judging the outer type alone recognized
    /// the second shape and missed the first, which is most of them.
    ///
    /// A TimeoutException anywhere in the chain settles it the other way, whichever end the
    /// cancellation is at. That covers both the HttpClient timeout .NET expresses as a
    /// TaskCanceledException wrapping one, and RestSharp's TimeoutException("Request timed out")
    /// wrapping the cancellation its own deadline raised. Neither is the app stopping its work.
    /// </remarks>
    internal static bool IsDeliberateCancellation(Exception? exception) => exception switch
    {
        null => false,
        AggregateException aggregate => IsDeliberateCancellation(aggregate),
        _ => IsCancellationFault(exception),
    };

    private static bool IsCancellationFault(Exception exception)
    {
        var chain = Chain(exception).ToList();
        return chain.Any(x => x is OperationCanceledException) && !chain.Any(x => x is TimeoutException);
    }

    /// <summary>An exception and everything it wraps, outermost first.</summary>
    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            yield return ex;
        }
    }

    /// <summary>
    /// Builds the directory Sentry writes undelivered envelopes to, under the given root.
    /// </summary>
    /// <remarks>
    /// Public so a platform head can supply a different root. iOS needs one: there
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/> resolves to Documents, which is
    /// iCloud-backed, user-visible, and reserved by Apple's data storage guidelines for data that
    /// cannot be regenerated - putting a cache there is a documented App Store rejection.
    /// <see cref="Environment.SpecialFolder.InternetCache"/> resolves to Library/Caches, which is
    /// excluded from backup and purgeable, and is the right home for a delivery queue.
    /// </remarks>
    /// <returns>The directory, or null when no writable location is available - which turns caching
    /// off rather than failing initialization.</returns>
    public static string? BuildCacheDirectory(Environment.SpecialFolder root)
    {
        try
        {
            var path = Environment.GetFolderPath(root);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            path = Path.Combine(path, "RedMist", "sentry");
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
