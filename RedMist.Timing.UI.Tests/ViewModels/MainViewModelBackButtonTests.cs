using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.ViewModels;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers the Android hardware back button. The return value is the whole contract: false hands the
/// press back to the OS, which backgrounds the app, so a branch that forgets to return true drops
/// the user out of the app instead of navigating.
/// </summary>
/// <remarks>
/// Not parallelized. Navigation runs through the shared <c>WeakReferenceMessenger.Default</c>, so
/// two of these view models running at once would receive each other's router events.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class MainViewModelBackButtonTests
{
    private readonly List<MainViewModel> created = [];

    /// <summary>
    /// Nothing unregisters a view model from the messenger, and it stays registered until it is
    /// collected. Without this, every view model built by an earlier test in this class also
    /// receives the router events the later ones send, and each one answers by kicking off its own
    /// round of background event loading.
    /// </summary>
    [TestCleanup]
    public void UnregisterFromTheMessenger()
    {
        foreach (var vm in created)
        {
            WeakReferenceMessenger.Default.UnregisterAll(vm);
        }
        created.Clear();
    }

    private MainViewModel Create()
    {
        var vm = TestViewModelFactory.CreateMain();
        created.Add(vm);
        return vm;
    }

    /// <summary>
    /// A view model sitting on the tab strip inside an event, which is where the back button has
    /// somewhere to go.
    /// </summary>
    private MainViewModel InEvent()
    {
        var vm = Create();
        vm.IsEventsListVisible = false;
        return vm;
    }

    [TestMethod]
    public void EventsList_IsNotHandled_SoTheOsBackgroundsTheApp()
    {
        var vm = Create();
        Assert.IsTrue(vm.IsEventsListVisible, "Sanity check - the app opens on the events list.");

        Assert.IsFalse(vm.HandleDeviceBackButton());
    }

    [TestMethod]
    public void NoTabSelected_IsNotHandled()
    {
        var vm = InEvent();

        // Nothing is selected and driver mode is off, so there is no back target.
        Assert.IsFalse(vm.HandleDeviceBackButton());
    }

    #region Tabs

    [TestMethod]
    public void LiveTimingTab_ReturnsToTheEventsList()
    {
        var vm = InEvent();
        vm.IsLiveTimingTabSelected = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
        Assert.IsTrue(vm.IsEventsListVisible, "Back from the timing grid navigates to the events list.");
    }

    [TestMethod]
    public void SettingsTab_ReturnsToTheEventsList()
    {
        var vm = InEvent();
        vm.IsSettingsTabSelected = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
        Assert.IsTrue(vm.IsEventsListVisible);
    }

    /// <summary>
    /// These tabs delegate to a child view model that is only built once its tab has been opened.
    /// The press still has to count as handled while that child is absent, otherwise back quits.
    /// </summary>
    [TestMethod]
    public void ResultsTab_IsHandled()
    {
        var vm = InEvent();
        vm.IsResultsTabSelected = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
    }

    [TestMethod]
    public void InformationTab_IsHandled()
    {
        var vm = InEvent();
        vm.IsInformationTabSelected = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
    }

    [TestMethod]
    public void ControlLogTab_IsHandled()
    {
        var vm = InEvent();
        vm.IsControlLogTabSelected = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
    }

    [TestMethod]
    public void FlagsTab_IsHandled()
    {
        var vm = InEvent();
        vm.IsFlagsTabSelected = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
    }

    #endregion

    #region Driver mode

    /// <summary>
    /// Driver mode selects no tab at all. It has to be checked ahead of the tab branches - with the
    /// tab flags all false the press would otherwise fall through unhandled and quit the app.
    /// </summary>
    [TestMethod]
    public void DriverMode_IsHandledEvenThoughNoTabIsSelected()
    {
        var vm = InEvent();
        vm.IsDriverModeVisible = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
    }

    [TestMethod]
    public void DriverMode_ShowingPositions_GoesBackToTheDriverSettings()
    {
        var vm = InEvent();
        using var settings = TestViewModelFactory.CreateInCarSettings();
        settings.IsPositionsVisible = true;
        vm.InCarSettingsViewModel = settings;
        vm.IsDriverModeVisible = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
        Assert.IsFalse(settings.IsPositionsVisible, "Back from the positions view returns to the settings view.");
        Assert.IsFalse(vm.IsEventsListVisible, "It should not have left driver mode altogether.");
    }

    [TestMethod]
    public void DriverMode_ShowingSettings_LeavesDriverMode()
    {
        var vm = InEvent();
        using var settings = TestViewModelFactory.CreateInCarSettings();
        settings.IsPositionsVisible = false;
        vm.InCarSettingsViewModel = settings;
        vm.IsDriverModeVisible = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
        Assert.IsTrue(vm.IsEventsListVisible, "Back from the driver settings returns to the events list.");
    }

    /// <summary>
    /// Driver mode is entered from the timing grid, so the live timing tab is still flagged as
    /// selected underneath it. Handling the tab as well would navigate twice on one press.
    /// </summary>
    [TestMethod]
    public void DriverMode_TakesPrecedenceOverTheSelectedTab()
    {
        var vm = InEvent();
        vm.IsLiveTimingTabSelected = true;
        vm.IsDriverModeVisible = true;

        Assert.IsTrue(vm.HandleDeviceBackButton());
        Assert.IsFalse(vm.IsEventsListVisible, "The live timing tab's back action should not have run.");
    }

    #endregion
}
