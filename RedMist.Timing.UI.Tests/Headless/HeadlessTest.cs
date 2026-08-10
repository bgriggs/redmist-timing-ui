using Avalonia.Headless;
using Avalonia.Threading;
using System.Reflection;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Runs a test body on a real Avalonia dispatcher.
/// </summary>
/// <remarks>
/// There is no Avalonia.Headless.MSTest adapter, so the session is driven directly. This matters
/// more than it sounds: with no Avalonia platform initialized, <c>Dispatcher.UIThread.CheckAccess()</c>
/// answers true on every thread and <c>Post</c>/<c>InvokeAsync</c> never run at all, so the whole
/// class of thread-affinity bugs is invisible to an ordinary unit test. Under this session the
/// dispatcher is real and off-thread work genuinely has to be marshalled.
///
/// Every test class using this must be <c>[DoNotParallelize]</c>, and the reason is not queueing -
/// the session serializes dispatches by itself. It is that Avalonia's statics are process-wide.
/// The session defaults to per-dispatch isolation, which enters an <c>AvaloniaLocator</c> scope and
/// installs a dispatcher for the duration; both are plain statics, so while a dispatch is running
/// every other thread in the process also sees a non-null <c>Application.Current</c> and a
/// <c>Dispatcher.UIThread</c> that answers <c>CheckAccess() == false</c>. That is enough to change
/// how the ordinary method-parallel tests behave. <c>[DoNotParallelize]</c> moves these classes into
/// MSTest's sequential phase, which runs after the parallel one, so the two never overlap.
///
/// Two consequences of that isolation worth knowing: nothing leaks between headless tests through
/// the dispatcher queue, because teardown drains and shuts down the dispatcher; and teardown races
/// the test method's continuation, so for a few milliseconds after an <c>OnDispatcher</c> call
/// returns, Avalonia may still look initialized to whatever runs next. No current test is exposed
/// to that, but a future <c>[DoNotParallelize]</c> class scheduled after these could be.
/// </remarks>
public static class HeadlessTest
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    /// <summary>
    /// Runs <paramref name="body"/> on the dispatcher thread. Awaits inside the body resume there
    /// too, so a test can hop to a background thread and come back.
    /// </summary>
    /// <remarks>
    /// The wrapping is load-bearing. The session has no <c>Dispatch(Func&lt;Task&gt;)</c> overload, so
    /// handing it an async lambda directly binds to <c>Dispatch&lt;TResult&gt;(Func&lt;TResult&gt;)</c>
    /// with <c>TResult = Task</c>: the session hands back the un-awaited inner task, the test method
    /// completes at the body's first await, and every assertion after that point runs unobserved.
    /// Tests written that way pass no matter what the code under test does. Returning a value forces
    /// the <c>Func&lt;Task&lt;TResult&gt;&gt;</c> overload, which does await the body; the explicit type
    /// argument is what keeps that choice unambiguous.
    ///
    /// There is a second way into the same hole, which the compiler will not warn about: writing a
    /// test as <c>public void X() =&gt; HeadlessTest.OnDispatcher(...)</c> compiles fine and discards
    /// the returned task, so again nothing is observed. Test methods here must return the task -
    /// <c>public Task X() =&gt; ...</c> - or await it.
    /// </remarks>
    public static Task OnDispatcher(Func<Task> body)
    {
        Func<Task<bool>> awaited = async () =>
        {
            await body();
            return true;
        };
        return Session.Dispatch<bool>(awaited, CancellationToken.None);
    }

    public static Task OnDispatcher(Action body) => Session.Dispatch(body, CancellationToken.None);

    /// <summary>
    /// Runs <paramref name="work"/> on a threadpool thread and drains anything it posted back to
    /// the dispatcher, so the test can assert on the result synchronously.
    /// </summary>
    public static async Task OffDispatcherThen(Action work)
    {
        Assert.IsTrue(Dispatcher.UIThread.CheckAccess(), "Call this from inside OnDispatcher.");

        await Task.Run(work);

        Assert.IsTrue(Dispatcher.UIThread.CheckAccess(), "Should have resumed on the dispatcher.");
        Dispatcher.UIThread.RunJobs();
    }
}
