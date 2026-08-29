using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Services;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Extensions;

/// <summary>
/// Extension methods for Avalonia Dispatcher to simplify UI thread marshalling.
/// </summary>
public static class DispatcherExtensions
{
    /// <summary>
    /// Executes an action on the UI thread, automatically handling thread marshalling.
    /// If already on the UI thread, executes immediately. Otherwise, posts to the UI thread.
    /// </summary>
    /// <param name="dispatcher">The dispatcher instance</param>
    /// <param name="action">The action to execute</param>
    public static void InvokeOnUIThread(this Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Post(Guard(action));
        }
    }

    /// <summary>
    /// Executes an action on the UI thread, automatically handling thread marshalling.
    /// If already on the UI thread, executes immediately. Otherwise, posts to the UI thread.
    /// </summary>
    /// <param name="dispatcher">The dispatcher instance</param>
    /// <param name="action">The action to execute</param>
    /// <param name="priority">The dispatcher priority</param>
    public static void InvokeOnUIThread(this Dispatcher dispatcher, Action action, DispatcherPriority priority)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Post(Guard(action), priority);
        }
    }

    /// <summary>
    /// Executes an action on the UI thread asynchronously, automatically handling thread marshalling.
    /// If already on the UI thread, executes immediately. Otherwise, invokes asynchronously on the UI thread.
    /// </summary>
    /// <param name="dispatcher">The dispatcher instance</param>
    /// <param name="action">The action to execute</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public static Task InvokeOnUIThreadAsync(this Dispatcher dispatcher, Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        else
        {
            var operation = dispatcher.InvokeAsync(action);
            return operation.GetTask();
        }
    }

    /// <summary>
    /// Executes an action on the UI thread asynchronously, automatically handling thread marshalling.
    /// If already on the UI thread, executes immediately. Otherwise, invokes asynchronously on the UI thread.
    /// </summary>
    /// <param name="dispatcher">The dispatcher instance</param>
    /// <param name="action">The action to execute</param>
    /// <param name="priority">The dispatcher priority</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public static Task InvokeOnUIThreadAsync(this Dispatcher dispatcher, Action action, DispatcherPriority priority)
    {
        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        else
        {
            var operation = dispatcher.InvokeAsync(action, priority);
            return operation.GetTask();
        }
    }

    /// <summary>
    /// Posts an action to the UI thread with a catch around it, reporting any fault instead of
    /// letting it reach the dispatcher's unhandled-exception handler.
    /// </summary>
    /// <remarks>
    /// A callback handed to <see cref="Dispatcher.Post(Action)"/> has no caller to propagate to, so
    /// a fault inside it goes straight to <c>Dispatcher.UIThread.UnhandledException</c> - which the
    /// app treats as fatal. That is the right default for a genuinely unexpected fault, but a
    /// marshalling callback that touches wire data or raises a binding is an ordinary place for one
    /// to happen, and taking the app down mid-session is the wrong answer there.
    ///
    /// Reporting goes through <see cref="ILogger"/>, so these reach Sentry as events on the same
    /// path as every other handled fault in the app.
    /// </remarks>
    /// <param name="origin">Filled in by the compiler with the calling member, so a report says
    /// which post the fault came from rather than just naming this helper.</param>
    public static void PostSafe(this Dispatcher dispatcher, Action action, ILogger logger,
        [CallerMemberName] string? origin = null)
        => dispatcher.Post(Guard(action, logger, origin));

    /// <inheritdoc cref="PostSafe(Dispatcher, Action, ILogger, string?)"/>
    public static void PostSafe(this Dispatcher dispatcher, Action action, ILogger logger,
        DispatcherPriority priority, [CallerMemberName] string? origin = null)
        => dispatcher.Post(Guard(action, logger, origin), priority);

    /// <summary>
    /// Posts asynchronous work to the UI thread with a catch around it.
    /// </summary>
    /// <remarks>
    /// Separate from the <see cref="Action"/> overloads because wrapping an async lambda in a
    /// synchronous try/catch catches only what throws before its first await - the rest is handed
    /// to the synchronization context, which is exactly the route this helper exists to close.
    /// Overload resolution prefers this one for an <c>async</c> lambda, so the safe overload is the
    /// one a caller gets by default.
    /// </remarks>
    public static void PostSafe(this Dispatcher dispatcher, Func<Task> action, ILogger logger,
        [CallerMemberName] string? origin = null)
        => dispatcher.Post(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Report(ex, logger, origin);
            }
        });

    /// <inheritdoc cref="PostSafe(Dispatcher, Func{Task}, ILogger, string?)"/>
    public static void PostSafe(this Dispatcher dispatcher, Func<Task> action, ILogger logger,
        DispatcherPriority priority, [CallerMemberName] string? origin = null)
        => dispatcher.Post(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Report(ex, logger, origin);
            }
        }, priority);

    /// <summary>
    /// Guards a callback that is being posted rather than run inline.
    /// </summary>
    /// <remarks>
    /// Only the posted path is guarded. When the caller is already on the dispatcher the action
    /// runs inline and a fault propagates to that caller, which has a stack and can decide - that
    /// is the existing behavior and worth keeping. A posted callback has no caller, so its fault
    /// goes to the dispatcher's unhandled handler, which the app now treats as fatal. Marshalling a
    /// wire-data update onto the UI thread is an ordinary place for a fault, and taking the app
    /// down mid-session is the wrong answer to it.
    ///
    /// Reports without an ILogger because these are static extensions with no injected state. The
    /// fault reaches Sentry but not the on-device display; callers that have a logger should prefer
    /// <c>PostSafe</c>.
    /// </remarks>
    private static Action Guard(Action action) => () =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CrashReporting.CaptureHandled(ex, "Dispatcher.InvokeOnUIThread");
        }
    };

    private static Action Guard(Action action, ILogger logger, string? origin) => () =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Report(ex, logger, origin);
        }
    };

    private static void Report(Exception ex, ILogger logger, string? origin)
        => logger.LogError(ex, "Unhandled exception in a dispatcher callback posted from {Origin}", origin ?? "unknown");
}
