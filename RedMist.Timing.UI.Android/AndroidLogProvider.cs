using Microsoft.Extensions.Logging;
using System;
using AndroidLog = Android.Util.Log;

namespace RedMist.Timing.UI.Android;

/// <summary>
/// Forwards the app's ILogger output to logcat, so a release build on a phone can be read with
/// <c>adb logcat -s RedMist</c>.
/// </summary>
/// <remarks>
/// AddDebug() looks like it already covers this, and does not. DebugLogger.IsEnabled returns false
/// unless a debugger is attached, so it writes nothing on any build of this app - release on a
/// phone included, and a debug build sideloaded onto one as well. The [Conditional("DEBUG")] on
/// Debug.WriteLine is not what stops it, which is the easy wrong conclusion to reach: that package
/// defines DEBUG in its own source precisely so the call survives a release compile. Of the other
/// two sinks, Sentry only takes warnings and above, and the in-app viewer is behind five taps on a
/// logo that on some phones is only visible in landscape. Neither is readable from a cable, which
/// left a release build on a device logging nowhere at all.
///
/// That gap is the reason this exists rather than tidiness. A hub whose first invoke throws leaves
/// the app polling REST every five seconds and looking perfectly healthy, and the evidence that
/// says otherwise is the per-patch line HubClient was already writing to nowhere.
///
/// Logcat stamps every line with its own time, so the message carries none.
/// </remarks>
public sealed class AndroidLogProvider : ILoggerProvider
{
    /// <summary>
    /// The logcat tag. Fixed and short so that <c>adb logcat -s RedMist</c> selects the app's own
    /// logging and nothing else - the runtime's native chatter arrives under DOTNET.
    /// </summary>
    public const string Tag = "RedMist";

    public ILogger CreateLogger(string categoryName) => new AndroidLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class AndroidLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            // Nothing may throw back through a logging call. Most of these are written from inside
            // a catch block, and ILogger wraps a faulting provider in an AggregateException, which
            // would replace the original exception in the handler that was reporting it. Writing to
            // logcat is a JNI call and can fail on a thread the runtime is tearing down.
            // InMemoryLogProvider guards its own fan-out for the same reason.
            try
            {
                // The category is carried in the message rather than the tag. A per-category tag
                // would read better, but it would also mean the one filter that catches everything
                // the app says no longer exists, and that filter is the point of this class.
                Write(logLevel, category + ": " + formatter(state, exception));

                // Separately, not appended. Android truncates a log entry at about 4 KB and says
                // nothing about it, and reading a release build's stack traces over a cable is
                // most of why this class exists - appending would cut the tail off the one thing
                // worth having.
                if (exception is not null)
                {
                    Write(logLevel, category + ": " + exception);
                }
            }
            catch
            {
                // Deliberately swallowed. There is nowhere left to report it to.
            }
        }

        private static void Write(LogLevel logLevel, string message)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    AndroidLog.Debug(Tag, message);
                    break;
                case LogLevel.Information:
                    AndroidLog.Info(Tag, message);
                    break;
                case LogLevel.Warning:
                    AndroidLog.Warn(Tag, message);
                    break;
                default:
                    AndroidLog.Error(Tag, message);
                    break;
            }
        }
    }
}
