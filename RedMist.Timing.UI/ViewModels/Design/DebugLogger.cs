using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DebugLogger : ILogger
{
    public List<string> LogMessages { get; private set; } = [];

    public bool IsTraceEnabled => true;

    public bool IsDebugEnabled => true;

    public bool IsErrorEnabled => true;

    public bool IsFatalEnabled => true;

    public bool IsInfoEnabled => true;

    public bool IsWarnEnabled => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        throw new NotImplementedException();
    }

    public ILogger CreateChildLogger(string loggerName)
    {
        return new DebugLogger();
    }

    public void Debug(string message)
    {
        LogMessages.Add(message);
        System.Diagnostics.Debug.WriteLine(message);
    }

    public void Debug(Func<string> messageFactory)
    {
        LogMessages.Add(messageFactory.Invoke());
        System.Diagnostics.Debug.WriteLine(messageFactory.Invoke());
    }

    public void Debug(string message, Exception exception)
    {
        Debug($"{message} {exception}");
    }

    public void DebugFormat(string format, params object[] args)
    {
        Debug(string.Format(format, args));
    }

    public void DebugFormat(Exception exception, string format, params object[] args)
    {
        Debug($"{string.Format(format, args)} {exception}");
    }

    public void DebugFormat(IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void DebugFormat(Exception exception, IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void Error(string message)
    {
        Debug(message);
    }

    public void Error(Func<string> messageFactory)
    {
        Debug(messageFactory.Invoke());
    }

    public void Error(string message, Exception exception)
    {
        Debug(message, exception);
    }

    public void ErrorFormat(string format, params object[] args)
    {
        LogMessages.Add(string.Format(format, args));
        System.Diagnostics.Debug.WriteLine(string.Format(format, args));
    }

    public void ErrorFormat(Exception exception, string format, params object[] args)
    {
        LogMessages.Add($"{string.Format(format, args)} {exception}");
        System.Diagnostics.Debug.WriteLine($"{string.Format(format, args)} {exception}");
    }

    public void ErrorFormat(IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void ErrorFormat(Exception exception, IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void Fatal(string message)
    {
        throw new NotImplementedException();
    }

    public void Fatal(Func<string> messageFactory)
    {
        throw new NotImplementedException();
    }

    public void Fatal(string message, Exception exception)
    {
        throw new NotImplementedException();
    }

    public void FatalFormat(string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void FatalFormat(Exception exception, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void FatalFormat(IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void FatalFormat(Exception exception, IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void Info(string message)
    {
        Debug(message);
    }

    public void Info(Func<string> messageFactory)
    {
        Debug(messageFactory);
    }

    public void Info(string message, Exception exception)
    {
        Debug(message, exception);
    }

    public void InfoFormat(string format, params object[] args)
    {
        Debug(string.Format(format, args));
    }

    public void InfoFormat(Exception exception, string format, params object[] args)
    {
        Debug(string.Format(format, args));
    }

    public void InfoFormat(IFormatProvider formatProvider, string format, params object[] args)
    {
        Debug(string.Format(format, args));
    }

    public void InfoFormat(Exception exception, IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        throw new NotImplementedException();
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        LogMessages.Add(formatter(state, exception));
        System.Diagnostics.Debug.WriteLine(formatter(state, exception));
    }

    public void Trace(string message)
    {
        Debug(message);
    }

    public void Trace(Func<string> messageFactory)
    {
        Debug(messageFactory);
    }

    public void Trace(string message, Exception exception)
    {
        Debug(message, exception);
    }

    public void TraceFormat(string format, params object[] args)
    {
        Debug(string.Format(format, args));
    }

    public void TraceFormat(Exception exception, string format, params object[] args)
    {
        Debug(string.Format(format, args));
    }

    public void TraceFormat(IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void TraceFormat(Exception exception, IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void Warn(string message)
    {
        Debug(message);
    }

    public void Warn(Func<string> messageFactory)
    {
        Debug(messageFactory);
    }

    public void Warn(string message, Exception exception)
    {
        Debug(message);
    }

    public void WarnFormat(string format, params object[] args)
    {
        Debug(string.Format(format, args));
    }

    public void WarnFormat(Exception exception, string format, params object[] args)
    {
        Debug(string.Format(format, args));
    }

    public void WarnFormat(IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }

    public void WarnFormat(Exception exception, IFormatProvider formatProvider, string format, params object[] args)
    {
        throw new NotImplementedException();
    }
}