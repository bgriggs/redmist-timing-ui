using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Extensions
{
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
                dispatcher.Post(action);
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
                dispatcher.Post(action, priority);
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
    }
}