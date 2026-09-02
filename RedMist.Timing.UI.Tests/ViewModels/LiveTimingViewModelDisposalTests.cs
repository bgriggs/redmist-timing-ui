using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers releasing a whole timing grid, which is what finally reaches the rows inside it.
/// </summary>
/// <remarks>
/// Rows are disposed by the cache subscription the grid holds, so a grid that is dropped without
/// being disposed never releases any of them - and every expanded row keeps a control log
/// subscription on the hub. ResultsViewModel builds one of these per session opened, so this is the
/// path that matters in practice.
/// </remarks>
[TestClass]
public sealed class LiveTimingViewModelDisposalTests
{
    private static SessionStatusNotification Update(SessionStatePatch patch) => new(patch);

    private static EventEntry Entry(string number)
        => new() { Number = number, Name = "Car " + number, Team = "Team", Class = "GP1" };

    [TestMethod]
    public void DisposingTheGridReleasesEveryRow()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1"), Entry("2")] }));
        var rows = vm.Cars.ToArray();
        Assert.AreEqual(2, rows.Length);

        vm.Dispose();

        Assert.IsTrue(rows.All(r => r.IsDisposed), "Every row should have been released with the grid.");
    }

    [TestMethod]
    public void DisposeIsIdempotent()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1")] }));

        vm.Dispose();
        vm.Dispose();
    }

    [TestMethod]
    public void ASearchLandingAfterDisposeIsIgnored()
    {
        // The search is debounced, and disposing that timer cannot recall a callback already posted
        // to the dispatcher. Without the guard this pushes onto a disposed subject and throws.
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1"), Entry("2")] }));

        vm.Dispose();

        vm.ApplySearchFilter("1");
    }
}
