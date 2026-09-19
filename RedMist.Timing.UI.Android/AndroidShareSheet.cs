using Android.App;
using Android.Content;
using AndroidX.Core.Content;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Android;

/// <summary>
/// Android's share sheet: an <c>ACTION_SEND</c> intent wrapped in a chooser.
/// </summary>
/// <remarks>
/// Built over whichever activity is live rather than one captured at startup - see
/// <see cref="MainActivity.Current"/> - because this service outlives any single activity and an
/// intent started from a finished one goes nowhere.
/// </remarks>
public sealed class AndroidShareSheet(Func<Activity?> activity) : IShareSheet
{
    /// <summary>Matches the authority declared for the provider in AndroidManifest.xml.</summary>
    private const string FileProviderSuffix = ".fileprovider";

    /// <summary>
    /// Matches the <c>cache-path</c> in Resources/xml/file_paths.xml. Only what is under that path is
    /// reachable through the provider.
    /// </summary>
    private const string SharedDirectory = "shared";

    private static ILogger Logger => App.GetLogger(nameof(AndroidShareSheet));

    public bool CanShareText => true;

    public bool CanShareImages => true;

    public Task<ShareOutcome> ShareTextAsync(SharePayload payload)
    {
        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(Intent.ExtraSubject, payload.Title);
        // One field, because ACTION_SEND has no separate slot for a link and apps that read only the
        // text would otherwise drop it.
        intent.PutExtra(Intent.ExtraText, $"{payload.Text}\n{payload.Url}");
        return Task.FromResult(Start(intent));
    }

    /// <remarks>
    /// The image goes through a <see cref="FileProvider"/> with read permission granted on the
    /// intent. A raw <c>file://</c> URI throws <c>FileUriExposedException</c> on anything since
    /// Android 7, and the receiving app has no rights to the app's cache directory regardless.
    /// </remarks>
    public async Task<ShareOutcome> ShareImageAsync(SharePayload payload, byte[] png, string fileName)
    {
        var context = activity() ?? Application.Context;
        try
        {
            var root = new Java.IO.File(context.CacheDir, SharedDirectory);
            root.Mkdirs();
            SweepOldCards(root);

            // Each share gets its own directory so the file can keep the readable name the recipient
            // sees without two shares of the same standings colliding. That collision is not
            // hypothetical: the name is derived from the event and the car, so sharing the same card
            // twice would rewrite the file underneath whichever app is still reading the first -
            // Gmail composing, Messages attaching - and hand it a half-written PNG.
            var directory = new Java.IO.File(root, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            directory.Mkdirs();

            var file = new Java.IO.File(directory, fileName);
            await File.WriteAllBytesAsync(file.AbsolutePath, png);

            var uri = FileProvider.GetUriForFile(context, context.PackageName + FileProviderSuffix, file);

            var intent = new Intent(Intent.ActionSend);
            intent.SetType("image/png");
            intent.PutExtra(Intent.ExtraStream, uri);
            intent.PutExtra(Intent.ExtraSubject, payload.Title);
            intent.PutExtra(Intent.ExtraText, $"{payload.Text}\n{payload.Url}");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

            // The flag above grants to whichever app the viewer picks, but the chooser itself is a
            // third party to that and needs the URI on the clip to read it. Without this the
            // sharesheet cannot open the file, so it offers "1 item" and a generic document icon
            // instead of the card - which is the one thing that makes the viewer confident they are
            // about to post the right image.
            intent.ClipData = ClipData.NewUri(context.ContentResolver, payload.Title, uri);

            return Start(intent);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not hand a timing card to the Android share sheet");
            return ShareOutcome.Failed;
        }
    }

    /// <summary>
    /// How long a shared card is left where the app that took it can still read it.
    /// </summary>
    /// <remarks>
    /// The receiving app holds a content URI, not a copy, and may not read it until the viewer sends
    /// the message - so these cannot be deleted on the way out. Generous enough to cover a message
    /// drafted and left, short enough that the cache does not grow for the life of the install.
    /// </remarks>
    private static readonly TimeSpan CardLifetime = TimeSpan.FromHours(6);

    /// <summary>Removes cards old enough that nothing can still be reading them.</summary>
    private static void SweepOldCards(Java.IO.File root)
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.Subtract(CardLifetime).ToUnixTimeMilliseconds();
            foreach (var directory in root.ListFiles() ?? [])
            {
                if (directory.LastModified() < cutoff)
                {
                    foreach (var file in directory.ListFiles() ?? [])
                    {
                        file.Delete();
                    }

                    directory.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            // Housekeeping. A share is not worth failing over it.
            Logger.LogWarning(ex, "Could not sweep old shared cards");
        }
    }

    /// <summary>
    /// Shows the chooser for <paramref name="intent"/>.
    /// </summary>
    /// <remarks>
    /// Reports <see cref="ShareOutcome.Shared"/> as soon as the chooser is up, which is as much as
    /// ACTION_SEND will say: nothing comes back to report what the viewer picked, or whether they
    /// picked anything. Learning that would mean an IntentSender and a broadcast receiver for a
    /// message the app has nothing to do with. What matters here - that the share sheet appeared -
    /// the viewer can see for themselves.
    /// </remarks>
    private ShareOutcome Start(Intent intent)
    {
        try
        {
            var chooser = Intent.CreateChooser(intent, "Share");
            var current = activity();
            if (current is not null)
            {
                current.StartActivity(chooser);
            }
            else
            {
                // No activity to start from, so the chooser needs its own task. Reached when the app
                // is between activities, which a share started by a tap should not be - kept because
                // failing outright here would send the caller to its clipboard fallback for no reason.
                chooser?.AddFlags(ActivityFlags.NewTask);
                Application.Context.StartActivity(chooser);
            }

            return ShareOutcome.Shared;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Android refused to show the share sheet");
            return ShareOutcome.Failed;
        }
    }
}
