using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BigMission.Avalonia.Utilities.Extensions;
using BigMission.Shared.Utilities;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.ViewModels;
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Views;

public partial class MainView : UserControl, IRecipient<LauncherEvent>, IRecipient<ShareRequest>,
    IRecipient<ShareImageRequest>
{
    private readonly Debouncer debouncer = new(TimeSpan.FromMilliseconds(25));

    private static ILogger Logger => App.GetLogger(nameof(MainView));

    public MainView()
    {
        InitializeComponent();
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        try
        {
            // Set the app to display edge-to-edge specifically for Android Pixel devices
            // http://github.com/AvaloniaUI/Avalonia/issues/18544
            var insetsManager = TopLevel.GetTopLevel(this)?.InsetsManager;
            if (insetsManager is not null)
            {
                // Same effect as before, said once. This went through dynamic to set
                // DisplayEdgeToEdgePreference to 1, but that property is a bool, so the assignment
                // threw every time and the catch below it did the actual work. DisplayEdgeToEdge
                // sets the same preference, and dropping the dynamic also drops a dependency on the
                // C# runtime binder, which is the kind of thing trimming removes.
#pragma warning disable CS0618 // Obsolete in favor of the preference property, which is not on the interface.
                insetsManager.DisplayEdgeToEdge = true;
#pragma warning restore CS0618
            }

            if (DataContext is MainViewModel vm)
            {
                await vm.Initialize();
                vm.IsTimingTabStripVisibleChanged += isVisible => Observable.Timer(TimeSpan.FromMilliseconds(100)).Subscribe(_ => Dispatcher.UIThread.InvokeOnUIThread(() => UpdateTabBarVisibility(Bounds.Size)));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in MainView.OnLoaded");
        }
    }

    public void Receive(LauncherEvent message)
    {
        try
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null && !string.IsNullOrEmpty(message.Uri))
            {
                launcher.LaunchUriAsync(new(message.Uri));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to launch URI {Uri}", message.Uri);
        }
    }

    /// <summary>
    /// Hands a link to the platform, falling back to the clipboard.
    /// </summary>
    /// <remarks>
    /// Handled here rather than in the timing view because two timing grids can be alive at once -
    /// the live tab and a stored session under the results tab - and a request message may only be
    /// answered once. There is one of these.
    /// </remarks>
    public void Receive(ShareRequest message)
    {
        if (!ShouldAnswer(message))
        {
            return;
        }

        message.Reply(ShareLinkAsync(message.Payload));
    }

    /// <inheritdoc cref="Receive(ShareRequest)"/>
    public void Receive(ShareImageRequest message)
    {
        if (!ShouldAnswer(message))
        {
            return;
        }

        message.Reply(ShareImageAsync(message));
    }

    /// <summary>
    /// Whether this view should be the one to answer <paramref name="message"/>.
    /// </summary>
    /// <remarks>
    /// Android can briefly hold two of these - a recreated activity's view alongside the one it
    /// replaces - and the messenger delivers in registration order, so the stale one is asked first.
    /// A second reply throws, so it has to be the detached view that stands down rather than the
    /// live one: an unattached view has no TopLevel, and so neither of the fallbacks this reply
    /// exists to provide.
    /// </remarks>
    private bool ShouldAnswer(AsyncRequestMessage<ShareOutcome> message)
        => !message.HasReceivedResponse && TopLevel.GetTopLevel(this) is not null;

    private async Task<ShareOutcome> ShareLinkAsync(SharePayload payload)
    {
        try
        {
            if (App.ShareSheet.CanShareText)
            {
                var outcome = await App.ShareSheet.ShareTextAsync(payload);
                if (outcome != ShareOutcome.Failed)
                {
                    return outcome;
                }

                // Fell through deliberately: a sheet that refused still leaves the viewer wanting
                // the link.
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(payload.Url);
                return ShareOutcome.Copied;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to share a link");
        }

        return ShareOutcome.Failed;
    }

    /// <summary>
    /// Hands the card to the platform, falling back to saving it where the viewer chooses.
    /// </summary>
    /// <remarks>
    /// A save dialog rather than a clipboard write, which is the browser's fallback: Avalonia's
    /// clipboard takes text everywhere and an image nowhere in particular, and a file is what a
    /// desktop viewer is going to attach to a post anyway.
    /// </remarks>
    private async Task<ShareOutcome> ShareImageAsync(ShareImageRequest request)
    {
        try
        {
            if (App.ShareSheet.CanShareImages)
            {
                var outcome = await App.ShareSheet.ShareImageAsync(request.Payload, request.Image, request.FileName);
                if (outcome != ShareOutcome.Failed)
                {
                    return outcome;
                }
            }

            return await SaveImageAsync(request);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to share a timing card");
            return ShareOutcome.Failed;
        }
    }

    private async Task<ShareOutcome> SaveImageAsync(ShareImageRequest request)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanSave)
        {
            return ShareOutcome.Failed;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save timing card",
            SuggestedFileName = request.FileName,
            DefaultExtension = "png",
            FileTypeChoices = [FilePickerFileTypes.ImagePng],
            ShowOverwritePrompt = true,
        });

        if (file is null)
        {
            // Closed the dialog, which is a decision rather than a failure.
            return ShareOutcome.Dismissed;
        }

        // The picked file is a handle, not a path, and holds a stream on some backends.
        using (file)
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(request.Image);
        }

        return ShareOutcome.Saved;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        WeakReferenceMessenger.Default.Send(new SizeChangedNotification(e.NewSize));
        debouncer.ExecuteAsync(async () => Dispatcher.UIThread.InvokeOnUIThread(() => UpdateTabBarVisibility(e.NewSize)));
        //UpdateTabBarVisibility(e.NewSize);
    }

    private void UpdateTabBarVisibility(Size size)
    {
        const double WidthMargin = 20;

        if (double.IsNaN(size.Width) || double.IsInfinity(size.Width))
            return;

        var liveTimingWidth = LiveTimingTab.IsVisible ? LiveTimingTab.DesiredSize.Width : 0;
        var resultsWidth = ResultsTab.IsVisible ? ResultsTab.DesiredSize.Width : 0;
        var informationWidth = InformationTab.IsVisible ? InformationTab.DesiredSize.Width : 0;
        var settingsWidth = SettingsTab.IsVisible ? SettingsTab.DesiredSize.Width : 0;

        double fixedWith = liveTimingWidth + resultsWidth + informationWidth + settingsWidth + WidthMargin;

        if (DataContext is MainViewModel vm && vm.IsControlLogAvailable)
        {
            if (fixedWith + ControlLogTab.DesiredSize.Width < size.Width)
            {
                fixedWith += ControlLogTab.DesiredSize.Width;
                ControlLogTab.IsVisible = true;
            }
            else
            {
                ControlLogTab.IsVisible = false;
            }
        }
        else
        {
            ControlLogTab.IsVisible = false;
        }

        FlagsTab.IsVisible = fixedWith + FlagsTab.DesiredSize.Width < size.Width;
    }
}
