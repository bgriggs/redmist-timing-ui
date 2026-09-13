using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace RedMist.Timing.UI.Tests;

/// <summary>
/// A logger factory that keeps everything logged through it, so a test can say what would and would
/// not have reached crash reporting.
/// </summary>
/// <remarks>
/// Error is the level the Sentry logging provider turns into an event, so "nothing at Error or
/// above" is the practical meaning of "this did not file a bug report".
/// </remarks>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public readonly record struct Entry(string Category, LogLevel Level, string Message, Exception? Exception);

    private readonly ConcurrentQueue<Entry> entries = new();

    public IReadOnlyList<Entry> Entries => [.. entries];

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, entries);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception));
    }
}
