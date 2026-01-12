using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Log provider that stores log messages in memory for display in the UI.
/// </summary>
public class InMemoryLogProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _logEntries = new();
    private readonly int _maxEntries;

    public event EventHandler<LogEntry>? LogAdded;

    public InMemoryLogProvider(int maxEntries = 1000)
    {
        _maxEntries = maxEntries;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new InMemoryLogger(this, categoryName);
    }

    public void AddLogEntry(LogEntry entry)
    {
        _logEntries.Enqueue(entry);
        
        // Trim old entries if we exceed max
        while (_logEntries.Count > _maxEntries)
        {
            _logEntries.TryDequeue(out _);
        }

        LogAdded?.Invoke(this, entry);
    }

    public IEnumerable<LogEntry> GetLogEntries()
    {
        return _logEntries.Reverse();
    }

    public void Dispose()
    {
        _logEntries.Clear();
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
    public DateTime Timestamp { get; set; }
    public LogLevel LogLevel { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Exception? Exception { get; set; }

    public string FormattedMessage => $"[{Timestamp:HH:mm:ss.fff}] [{LogLevel}] {Category}: {Message}{(Exception != null ? $"\n{Exception}" : "")}";
}
