using CoreGraphics;
using Foundation;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UIKit;

namespace RedMist.Timing.UI.iOS;

/// <summary>
/// iOS's share sheet: a <see cref="UIActivityViewController"/> over whatever is being shared.
/// </summary>
public sealed class iOSShareSheet : IShareSheet
{
    private static ILogger Logger => App.GetLogger(nameof(iOSShareSheet));

    public bool CanShareText => true;

    public bool CanShareImages => true;

    public Task<ShareOutcome> ShareTextAsync(SharePayload payload)
    {
        // The link as an NSUrl rather than as part of the text, so the sheet offers the actions that
        // belong to a URL - Messages renders a preview, Safari offers to open it - instead of treating
        // the whole thing as a block of prose.
        var items = NSUrl.FromString(payload.Url) is { } url
            ? new NSObject[] { new NSString(payload.Text), url }
            : [new NSString($"{payload.Text}\n{payload.Url}")];

        return PresentAsync(items);
    }

    /// <remarks>
    /// Shared as a file on disk rather than as a <see cref="UIImage"/>, so the recipient gets a PNG
    /// with the card's file name on it and Photos is not the only sensible destination.
    /// </remarks>
    public async Task<ShareOutcome> ShareImageAsync(SharePayload payload, byte[] png, string fileName)
    {
        try
        {
            // Not deleted on the way out. The sheet reads the file while it is up and for as long as
            // the app the viewer chose needs it, so removing it here would hand some of them an empty
            // attachment. Instead the ones from earlier shares are swept first, which is also what
            // stops a card per event accumulating for the life of the install - the name is derived
            // from the event and the car, so only a repeat of the same one overwrites.
            var directory = Path.Combine(Path.GetTempPath(), CardDirectory);
            Directory.CreateDirectory(directory);
            SweepOldCards(directory);

            var path = Path.Combine(directory, fileName);
            await File.WriteAllBytesAsync(path, png);

            var items = new NSObject[] { new NSString(payload.Text), NSUrl.FromFilename(path) };
            return await PresentAsync(items);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not hand a timing card to the iOS share sheet");
            return ShareOutcome.Failed;
        }
    }

    /// <summary>Where shared cards are written, so they can be swept without touching anything else.</summary>
    private const string CardDirectory = "shared-cards";

    /// <summary>
    /// How long a shared card is left where the app that took it can still read it.
    /// </summary>
    /// <remarks>
    /// The receiving app is handed a file URL, and may not read it until the viewer sends the
    /// message, so these cannot go on the way out of a share.
    /// </remarks>
    private static readonly TimeSpan CardLifetime = TimeSpan.FromHours(6);

    /// <summary>Removes cards old enough that nothing can still be reading them.</summary>
    private static void SweepOldCards(string directory)
    {
        try
        {
            var cutoff = DateTime.UtcNow - CardLifetime;
            foreach (var path in Directory.EnumerateFiles(directory, "*.png"))
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            // Housekeeping. A share is not worth failing over it.
            Logger.LogWarning(ex, "Could not sweep old shared cards");
        }
    }

    private Task<ShareOutcome> PresentAsync(NSObject[] items)
    {
        var completion = new TaskCompletionSource<ShareOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var presenter = TopViewController();
            if (presenter is null)
            {
                Logger.LogWarning("No view controller to present the share sheet from");
                return Task.FromResult(ShareOutcome.Failed);
            }

            var sheet = new UIActivityViewController(items, null);
            sheet.CompletionWithItemsHandler = (activityType, completed, returnedItems, error) =>
            {
                if (error is not null)
                {
                    Logger.LogWarning("The iOS share sheet reported {Error}", error.LocalizedDescription);
                }

                completion.TrySetResult(completed ? ShareOutcome.Shared : ShareOutcome.Dismissed);
            };

            // An iPad presents this as a popover and throws without somewhere to anchor it. The middle
            // of the presenting view is the honest answer when the tap came from a button this code
            // cannot see; anchoring is what iPad requires, not where it insists on.
            if (sheet.PopoverPresentationController is { } popover && presenter.View is { } anchor)
            {
                popover.SourceView = anchor;
                var bounds = anchor.Bounds;
                popover.SourceRect = new CGRect(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, 0, 0);
            }

            presenter.PresentViewController(sheet, animated: true, completionHandler: null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "iOS refused to present the share sheet");
            return Task.FromResult(ShareOutcome.Failed);
        }

        return completion.Task;
    }

    /// <summary>
    /// The controller a sheet can be presented from: the key window's root, walked past anything it is
    /// already presenting.
    /// </summary>
    /// <remarks>
    /// Presenting from a controller that already has something up is silently ignored, which is
    /// exactly the share button that appears to do nothing.
    /// </remarks>
    private static UIViewController? TopViewController()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);

        var controller = window?.RootViewController;
        while (controller?.PresentedViewController is { } presented)
        {
            controller = presented;
        }

        return controller;
    }
}
