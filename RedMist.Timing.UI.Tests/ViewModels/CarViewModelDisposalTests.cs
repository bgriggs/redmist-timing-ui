using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers when a car row is released, and - the harder half - when it must not be.
/// </summary>
/// <remarks>
/// A row owns a details view model, which owns a chart and a control log subscription on the hub,
/// so a row that leaves the field without being told has stranded all of it. The cache says so
/// through DisposeMany, but only for rows that implement IDisposable, so for a long time the call
/// did nothing.
///
/// Making the row disposable is only half of it: DisposeMany releases whatever the stream it is
/// attached to drops, and a filtered stream drops a car that is merely out of view. Hung off the
/// filtered projection it would dispose most of the field on the first keystroke in the search box
/// and then hand the same dead rows back when the search was cleared. The searching tests below are
/// the ones that pin that down.
/// </remarks>
[TestClass]
public sealed class CarViewModelDisposalTests
{
    private static SessionStatusNotification Update(SessionStatePatch patch) => new(patch);

    private static EventEntry Entry(string number, string @class = "GP1")
        => new() { Number = number, Name = "Car " + number, Team = "Team", Class = @class };

    private static CarViewModel Row(LiveTimingViewModel vm, string number)
        => vm.Cars.Single(c => c.Number == number);

    [TestMethod]
    public void ACarDroppedFromTheEntryList_IsDisposed()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1"), Entry("2")] }));
        var leaving = Row(vm, "2");

        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1")] }));

        Assert.IsTrue(leaving.IsDisposed, "A car that left the entry list should have been released.");
        Assert.IsFalse(Row(vm, "1").IsDisposed, "The car still entered should not have been.");
    }

    [TestMethod]
    public void SearchingDoesNotDisposeTheCarsItHides()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2"), Entry("3")],
        }));
        var hidden = Row(vm, "2");

        vm.ApplySearchFilter("1");

        Assert.AreEqual(1, vm.Cars.Count, "Only the matching car should be shown.");
        Assert.IsFalse(hidden.IsDisposed,
            "A car hidden by the search is still in the field and must not be released.");
    }

    [TestMethod]
    public void ClearingASearchBringsBackWorkingRows()
    {
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch
        {
            EventEntries = [Entry("1"), Entry("2")],
        }));
        var hidden = Row(vm, "2");

        vm.ApplySearchFilter("1");
        vm.ApplySearchFilter(string.Empty);

        // The cache holds the same instances throughout, so a row disposed while hidden would come
        // back dead rather than being rebuilt.
        Assert.AreEqual(2, vm.Cars.Count);
        Assert.AreSame(hidden, Row(vm, "2"), "The same row instance should come back.");
        Assert.IsFalse(hidden.IsDisposed, "And it should still be usable.");
    }

    [TestMethod]
    public void ChangingClassDoesNotDisposeTheCar()
    {
        // Grouping by class projects each class into its own collection, which drops a car when its
        // class changes. That is not the car leaving the event.
        var vm = TestViewModelFactory.CreateLiveTiming();
        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1", "GP1")] }));
        var car = Row(vm, "1");

        vm.ApplySessionUpdate(Update(new SessionStatePatch { EventEntries = [Entry("1", "GP2")] }));

        Assert.IsFalse(car.IsDisposed);
        Assert.AreEqual("GP2", car.Class);
    }

    [TestMethod]
    public void DisposeIsIdempotent()
    {
        var car = TestViewModelFactory.CreateCar();

        car.Dispose();
        car.Dispose();

        Assert.IsTrue(car.IsDisposed);
    }

    [TestMethod]
    public void ADisposedRowDoesNotBuildNewDetails()
    {
        // The expander drives this through a two-way binding, and a row can still be realized while
        // it is being removed. Details built at that point would hold a hub subscription that
        // nothing is left to release.
        var car = TestViewModelFactory.CreateCar();
        car.Dispose();

        car.IsDetailsExpanded = true;

        Assert.IsNull(car.CarDetailsViewModel);
    }
}
