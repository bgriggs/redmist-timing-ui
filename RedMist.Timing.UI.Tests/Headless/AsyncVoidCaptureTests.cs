using Avalonia.Threading;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Pins the routes by which a fault reaches <c>Dispatcher.UIThread.UnhandledException</c>, which is
/// the hook App uses to report crashes.
/// </summary>
/// <remarks>
/// This matters because the app has nine <c>async void</c> methods that cannot be anything else -
/// five are <c>IRecipient&lt;T&gt;.Receive</c> implementations, whose signature the interface fixes,
/// and two are <c>OnLoaded</c> overrides. A fault in any of them has no caller to propagate to; it
/// is handed to the synchronization context instead. If Avalonia ever stopped routing that to the
/// dispatcher's handler, those call sites would silently stop being reported and nothing else in
/// the suite would notice.
///
/// Verified against Avalonia 11.3.12. The routing lives in Avalonia.Base rather than in a platform
/// backend, so it should hold on the device heads too, but this only proves it for headless.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class AsyncVoidCaptureTests
{
    private static async void ThrowsAfterAwait(TaskCompletionSource started)
    {
        started.SetResult();
        await Task.Yield();
        throw new InvalidOperationException("after-await");
    }

    private static async void ThrowsBeforeAwait()
    {
        throw new InvalidOperationException("before-await");
#pragma warning disable CS0162 // Unreachable - present to make the method genuinely async.
        await Task.Yield();
#pragma warning restore CS0162
    }

    /// <summary>
    /// Runs <paramref name="trigger"/> and returns the exception the dispatcher handler saw, or
    /// null if it never fired. Always marks the fault handled so it cannot tear down the test host.
    /// </summary>
    private static async Task<Exception?> CaptureFromDispatcher(Action trigger)
    {
        // Signalled by the handler rather than polled for. A fixed number of pump-and-sleep rounds
        // is racy: Task.Delay's continuation needs the dispatcher it is competing with, so under
        // load the budget can expire before the posted fault is ever run.
        var captured = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            captured.TrySetResult(e.Exception);
            e.Handled = true;
        }

        Dispatcher.UIThread.UnhandledException += Handler;
        try
        {
            trigger();

            // The fault is posted, so it lands on a later turn of the loop rather than inline.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!captured.Task.IsCompleted && DateTime.UtcNow < deadline)
            {
                Dispatcher.UIThread.RunJobs();
                if (captured.Task.IsCompleted)
                {
                    break;
                }

                await Task.Yield();
            }

            return captured.Task.IsCompleted ? await captured.Task : null;
        }
        finally
        {
            Dispatcher.UIThread.UnhandledException -= Handler;
        }
    }

    [TestMethod]
    public Task AsyncVoid_FaultingAfterAnAwait_ReachesTheDispatcherHandler() => HeadlessTest.OnDispatcher(async () =>
    {
        var started = new TaskCompletionSource();

        var seen = await CaptureFromDispatcher(() => ThrowsAfterAwait(started));

        Assert.IsNotNull(seen, "An async void fault after an await must reach the handler App reports from.");
        Assert.AreEqual("after-await", seen.Message);
    });

    [TestMethod]
    public Task AsyncVoid_FaultingBeforeAnyAwait_ReachesTheDispatcherHandler() => HeadlessTest.OnDispatcher(async () =>
    {
        Exception? threwToCaller = null;

        var seen = await CaptureFromDispatcher(() =>
        {
            try
            {
                ThrowsBeforeAwait();
            }
            catch (Exception ex)
            {
                threwToCaller = ex;
            }
        });

        // Worth pinning: an async void method does not throw to its caller even when it faults
        // before its first await, so a try/catch around the call site would not see this.
        Assert.IsNull(threwToCaller, "async void does not propagate to the caller.");
        Assert.IsNotNull(seen, "The fault must still reach the handler App reports from.");
        Assert.AreEqual("before-await", seen.Message);
    });

    [TestMethod]
    public Task DispatcherPost_Faulting_ReachesTheDispatcherHandler() => HeadlessTest.OnDispatcher(async () =>
    {
        // The view models marshal bound-state writes with Post; a fault inside one of those
        // callbacks has no caller either.
        var seen = await CaptureFromDispatcher(
            () => Dispatcher.UIThread.Post(() => throw new InvalidOperationException("posted")));

        Assert.IsNotNull(seen, "A fault inside a posted callback must reach the handler App reports from.");
        Assert.AreEqual("posted", seen.Message);
    });
}
