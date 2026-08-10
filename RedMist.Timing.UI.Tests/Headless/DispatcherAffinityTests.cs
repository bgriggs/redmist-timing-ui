using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Establishes that the headless session gives us a real dispatcher, and pins the marshalling
/// helper the view models are built on.
/// </summary>
/// <remarks>
/// These are the foundation for the rest of the headless tests: if the platform were not actually
/// initialized, <c>CheckAccess</c> would answer true everywhere and every affinity assertion in
/// this folder would pass vacuously.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class DispatcherAffinityTests
{
    [TestMethod]
    public Task TheSessionProvidesARealDispatcher() => HeadlessTest.OnDispatcher(async () =>
    {
        Assert.IsTrue(Dispatcher.UIThread.CheckAccess(), "The test body runs on the dispatcher thread.");

        var onPoolThread = await Task.Run(Dispatcher.UIThread.CheckAccess);

        Assert.IsFalse(onPoolThread,
            "A threadpool thread must not claim dispatcher access. If this fails the platform is not " +
            "initialized and every affinity test here is meaningless.");
    });

    [TestMethod]
    public Task InvokeOnUIThread_FromTheUIThread_RunsInline() => HeadlessTest.OnDispatcher(() =>
    {
        var ran = false;

        Dispatcher.UIThread.InvokeOnUIThread(() => ran = true);

        // Inline, not queued. Several view models rely on this - they call it and immediately read
        // back the state it wrote.
        Assert.IsTrue(ran, "Already on the dispatcher, so the action should have run before returning.");
    });

    [TestMethod]
    public Task InvokeOnUIThread_FromAPoolThread_RunsOnTheDispatcher() => HeadlessTest.OnDispatcher(async () =>
    {
        bool? ranOnDispatcher = null;

        await HeadlessTest.OffDispatcherThen(() =>
            Dispatcher.UIThread.InvokeOnUIThread(() => ranOnDispatcher = Dispatcher.UIThread.CheckAccess()));

        Assert.IsTrue(ranOnDispatcher, "The action must be marshalled onto the dispatcher, not run on the caller.");
    });

    [TestMethod]
    public Task InvokeOnUIThreadAsync_FromAPoolThread_CompletesAfterTheActionRuns() => HeadlessTest.OnDispatcher(async () =>
    {
        var ran = false;

        // InitializeLiveAsync awaits this before touching anything else, so the await has to mean
        // "the bound state has been written", not "the write has been queued".
        await Task.Run(async () => await Dispatcher.UIThread.InvokeOnUIThreadAsync(() => ran = true));

        Assert.IsTrue(ran);
    });
}
