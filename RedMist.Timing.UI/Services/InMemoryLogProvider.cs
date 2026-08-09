using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Log provider that stores log messages in memory for display in the UI.
/// </summary>
/// <remarks>
/// Warnings and errors are also kept in a separate buffer. Routine traffic (a session patch is
/// logged roughly once a second) would otherwise push a failure out of the rolling buffer within a
/// minute, which is no use to someone trying to read the in-app log after something went wrong.
/// </remarks>
public class InMemoryLogProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _logEntries = new();
    private readonly ConcurrentQueue<LogEntry> _problemEntries = new();
    private readonly int _maxEntries;
    private readonly int _maxProblemEntries;

    public event EventHandler<LogEntry>? LogAdded;

    public InMemoryLogProvider(int maxEntries = 1000, int maxProblemEntries = 25)
    {
        _maxEntries = maxEntries;
        _maxProblemEntries = maxProblemEntries;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(this, categoryName);
    }

    public void AddLogEntry(LogEntry entry)
    {
        Enqueue(_logEntries, entry, _maxEntries);

        if (entry.LogLevel >= LogLevel.Warning)
        {
            Enqueue(_problemEntries, entry, _maxProblemEntries);
        }

        try
        {
            LogAdded?.Invoke(this, entry);
        }
        catch
        {
            // A subscriber must never throw back through the logging call. Most of these logs are
            // written from inside a catch block, and ILogger wraps a faulting provider in an
            // AggregateException - which would escape the very handler that was reporting the
            // original failure. There is nowhere useful to report this, so drop it.
        }
    }

    private static void Enqueue(ConcurrentQueue<LogEntry> queue, LogEntry entry, int maxEntries)
    {
        queue.Enqueue(entry);

        // Trim old entries if we exceed max
        while (queue.Count > maxEntries && queue.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// All retained log entries, newest first.
    /// </summary>
    public IEnumerable<LogEntry> GetLogEntries()
    {
        return _logEntries.Reverse();
    }

    /// <summary>
    /// Retained warnings and errors, newest first.
    /// </summary>
    public IEnumerable<LogEntry> GetProblemEntries()
    {
        return _problemEntries.Reverse();
    }

    public void Dispose()
    {
        _logEntries.Clear();
        _problemEntries.Clear();
        GC.SuppressFinalize(this);
    }

    private class InMemoryLogger : ILogger
    {
        private readonly InMemoryLogProvider _provider;
        private readonly string _categoryName;

        public InMemoryLogger(InMemoryLogProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                LogLevel = logLevel,
                Category = _categoryName,
                Message = message,
                Exception = exception
            };

            _provider.AddLogEntry(entry);
        }
    }
}

public class LogEntry
{
    private string? _formattedMessage;

    public DateTime Timestamp { get; init; }
    public LogLevel LogLevel { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }

    /// <summary>
    /// Formatted once and cached. Interpolating an exception calls Exception.ToString(), which
    /// re-walks and re-resolves the stack trace every time; retained entries are re-rendered on
    /// every refresh of the in-app log, so formatting on each read would be a per-second cost that
    /// only switches on once something has already gone wrong.
    /// </summary>
    public string FormattedMessage => _formattedMessage ??=
        $"[{Timestamp:HH:mm:ss.fff}] [{LogLevel}] {Category}: {Message}{(Exception != null ? $"\n{Exception}" : "")}";
}
