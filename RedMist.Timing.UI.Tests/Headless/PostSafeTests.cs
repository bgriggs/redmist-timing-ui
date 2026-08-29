using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Extensions;
using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers <c>Dispatcher.PostSafe</c>, which exists so that a fault inside a marshalling callback is
/// reported rather than reaching the dispatcher's unhandled-exception handler, which the app treats
/// as fatal.
/// </summary>
/// <remarks>
/// <see cref="AsyncVoidCaptureTests"/> establishes the behavior these guards are defending against:
/// a posted callback has no caller, so its fault goes straight to the dispatcher handler.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class PostSafeTests
{
    /// <summary>
    /// Runs <paramref name="post"/> and returns what was logged, plus anything that escaped to the
    /// dispatcher's handler. Both are awaited on rather than polled for, so the test does not race
    /// the dispatcher it shares a thread with.
    /// </summary>
    private static async Task<(LogEntry? Logged, Exception? Escaped)> Run(Action<ILogger> post)
    {
        var provider = new InMemoryLogProvider();
        var logged = new TaskCompletionSource<LogEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? escaped = null;

        void OnLogAdded(object? sender, LogEntry entry)
        {
            if (entry.LogLevel >= LogLevel.Error)
            {
                logged.TrySetResult(entry);
            }
        }

        void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            escaped = e.Exception;
            e.Handled = true;
        }

        provider.LogAdded += OnLogAdded;
        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        try
        {
            post(provider.CreateLogger("PostSafeTests"));

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!logged.Task.IsCompleted && escaped is null && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                if (logged.Task.IsCompleted || escaped is not null)
                {
                    break;
                }

                await Task.Yield();
            }
        }
        finally
        {
            provider.LogAdded -= OnLogAdded;
            Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        }

        return (logged.Task.IsCompleted ? await logged.Task : null, escaped);
    }

    [TestMethod]
    public Task PostSafe_CatchesASynchronousFault() => HeadlessTest.OnDispatcher(async () =>
    {
        var (logged, escaped) = await Run(logger =>
            Dispatcher.UIThread.PostSafe(() => throw new InvalidOperationException("boom"), logger));

        Assert.IsNull(escaped, "The fault must not reach the dispatcher handler, which the app treats as fatal.");
        Assert.IsNotNull(logged, "The fault must be reported.");
        Assert.AreEqual("boom", logged.Exception?.Message);
    });

    [TestMethod]
    public Task PostSafe_WithPriority_CatchesASynchronousFault() => HeadlessTest.OnDispatcher(async () =>
    {
        var (logged, escaped) = await Run(logger =>
            Dispatcher.UIThread.PostSafe(
                () => throw new InvalidOperationException("boom-priority"),
                logger,
                DispatcherPriority.Background));

        Assert.IsNull(escaped);
        Assert.IsNotNull(logged);
        Assert.AreEqual("boom-priority", logged.Exception?.Message);
    });

    [TestMethod]
    public Task PostSafe_CatchesAFaultAfterAnAwait() => HeadlessTest.OnDispatcher(async () =>
    {
        // The case the Action overload cannot cover: an async lambda returns at its first await, so
        // a synchronous try/catch around it sees nothing and the fault goes to the synchronization
        // context instead. Passing an async lambda has to select the Func<Task> overload for this
        // to hold - if overload resolution ever changed, this test is what catches it.
        var (logged, escaped) = await Run(logger =>
            Dispatcher.UIThread.PostSafe(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom-async");
            }, logger));

        Assert.IsNull(escaped, "A post-await fault must not reach the dispatcher handler either.");
        Assert.IsNotNull(logged);
        Assert.AreEqual("boom-async", logged.Exception?.Message);
    });

    [TestMethod]
    public Task PostSafe_NamesTheCallingMemberInTheReport() => HeadlessTest.OnDispatcher(async () =>
    {
        var (logged, _) = await Run(logger =>
            Dispatcher.UIThread.PostSafe(() => throw new InvalidOperationException("named"), logger));

        // Without this the report only ever names the helper, which is the same for every post in
        // the app and tells you nothing about where the fault came from.
        Assert.IsNotNull(logged);
        StringAssert.Contains(logged.Message, nameof(PostSafe_NamesTheCallingMemberInTheReport));
    });

    [TestMethod]
    public Task PostSafe_DoesNotInterfereWithASucceedingCallback() => HeadlessTest.OnDispatcher(async () =>
    {
        var ran = false;

        Dispatcher.UIThread.PostSafe(() => ran = true, new InMemoryLogProvider().CreateLogger("t"));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ran && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Assert.IsTrue(ran);
    });
}
