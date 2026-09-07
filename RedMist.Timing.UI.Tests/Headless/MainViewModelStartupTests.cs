using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Tests.ViewModels;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers what startup waits for before the events list can appear.
/// </summary>
/// <remarks>
/// The version check used to be awaited at the top of <c>Initialize</c>, ahead of
/// <c>IsContentVisible</c>, and that held back the request as well as the paint - see
/// <see cref="AControlUnderAHiddenParent_IsNeverEvenBuilt"/> for why. So a cold start ran the
/// Keycloak token, then the version request, and only then asked for the events the person opened
/// the app to see.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class MainViewModelStartupTests
{
    private readonly List<MainViewModel> created = [];

    /// <summary>
    /// Navigation runs through the shared <c>WeakReferenceMessenger.Default</c> and nothing
    /// unregisters a view model, so a leftover one would answer the next test's router events.
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

    /// <summary>
    /// A version check that never answers on its own, so a test can ask what startup did while it
    /// was still outstanding.
    /// </summary>
    private sealed class PendingVersionCheckService : IVersionCheckService
    {
        private readonly TaskCompletionSource<UIVersionInfo?> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public UpdateRequirement Requirement { get; set; } = UpdateRequirement.None;

        public void Answer() => answer.TrySetResult(new UIVersionInfo());

        public Task<UIVersionInfo?> GetVersionInfoAsync(int timeoutSeconds = 5) => answer.Task;

        public Version GetCurrentApplicationVersion() => new(1, 0, 0);

        public VersionCheckResult CheckVersion(Version currentVersion, UIVersionInfo versionInfo, AppPlatform platform) =>
            new()
            {
                Requirement = Requirement,
                Platform = platform,
                CurrentVersion = currentVersion,
                Message = "Update required",
            };
    }

    private MainViewModel CreateMain(PendingVersionCheckService versionCheck)
    {
        var vm = TestViewModelFactory.CreateMain(versionCheckService: versionCheck);
        created.Add(vm);
        return vm;
    }

    [TestMethod]
    public Task TheContentIsShown_WithoutWaitingForTheVersionCheck() => HeadlessTest.OnDispatcher(async () =>
    {
        var versionCheck = new PendingVersionCheckService();
        var vm = CreateMain(versionCheck);

        var initialize = vm.Initialize();

        // Asserted synchronously, and that is the point: on this host Initialize runs to its own
        // first await, which is the one on the version check at the very end, so everything the
        // first screen needs has already happened by the time it hands the task back. On the
        // browser head there is one await before it - the JS module, which the deep-link branch
        // needs - so there the content appears a turn later. Either way it does not wait on the
        // version check, which is what this is about.
        Assert.IsTrue(vm.IsContentVisible,
            "The events list is inside the content grid, so holding this back holds back the request for the events themselves.");
        Assert.IsFalse(initialize.IsCompleted, "The version check has to still be outstanding, or this proves nothing.");

        versionCheck.Answer();
        await initialize.WaitAsync(TimeSpan.FromSeconds(10));
    });

    /// <summary>
    /// The version check still gets to block the app when it comes back; it just no longer blocks
    /// the app on the way there.
    /// </summary>
    /// <remarks>
    /// The overlay is the last child of the same content grid, so it is drawn over the list rather
    /// than instead of it. That is what makes showing the list early safe.
    /// </remarks>
    [TestMethod]
    public Task AMandatoryUpdate_StillBlocksTheApp() => HeadlessTest.OnDispatcher(async () =>
    {
        var versionCheck = new PendingVersionCheckService { Requirement = UpdateRequirement.Mandatory };
        var vm = CreateMain(versionCheck);

        var initialize = vm.Initialize();
        Assert.IsFalse(vm.IsMandatoryUpdateVisible, "Nothing is known about the version yet.");

        versionCheck.Answer();
        await initialize.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.IsTrue(vm.IsMandatoryUpdateVisible, "The overlay has to arrive once the check does.");
        Assert.IsTrue(vm.IsContentVisible, "The overlay covers the content; it does not replace it.");
    });

    /// <summary>
    /// Pins the Avalonia behavior the ordering above depends on.
    /// </summary>
    /// <remarks>
    /// An invisible control is not measured, so its ContentPresenter never builds its child and the
    /// child is not in the visual tree at all - meaning no OnLoaded, and <c>EventsListView.OnLoaded</c>
    /// is the only thing that starts the events request on a cold start. Without this, someone could
    /// reasonably assume hiding the grid only skipped the drawing.
    /// </remarks>
    [TestMethod]
    public Task AControlUnderAHiddenParent_IsNeverEvenBuilt() => HeadlessTest.OnDispatcher(() =>
    {
        var probe = new LoadCountingControl();
        var grid = new Grid { IsVisible = false };
        grid.Children.Add(new ContentControl { Content = probe });
        var window = new Window { Content = grid, Width = 400, Height = 400 };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.AreEqual(0, probe.Loads, "A hidden parent means the child never loads, so nothing it does on load runs.");
        Assert.IsNull(probe.GetVisualRoot(), "The child is not in the visual tree at all, not merely undrawn.");

        grid.IsVisible = true;
        window.Measure(new Size(400, 400));
        window.Arrange(new Rect(0, 0, 400, 400));
        Dispatcher.UIThread.RunJobs();

        Assert.AreEqual(1, probe.Loads, "Making the parent visible is what finally loads the child.");
    });

    private sealed class LoadCountingControl : UserControl
    {
        public int Loads;

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            Loads++;
        }
    }
}
