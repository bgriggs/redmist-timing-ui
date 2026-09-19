using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Models;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// The WebAssembly head's share sheet: the browser's own, through <c>navigator.share</c>.
/// </summary>
/// <remarks>
/// This head is also the viewer the website puts in an iframe, so it is the one share path a visitor
/// who has not installed the app will ever use - which makes it the one that matters most to the
/// reason this feature exists.
///
/// The fallbacks live in JavaScript rather than here, because they are the browser's own facilities:
/// a clipboard image write, then a download. See main.js. <c>MainView</c>'s clipboard fallback still
/// sits behind all of it for a link.
/// </remarks>
public sealed class BrowserShareSheet : IShareSheet
{
    private static ILogger Logger => App.GetLogger(nameof(BrowserShareSheet));

    /// <summary>
    /// Always true, both of them, and that is the point.
    /// </summary>
    /// <remarks>
    /// These do not mean "this browser has navigator.share" - they mean "hand it to me and I will
    /// get it in front of the viewer somehow". Answering for navigator.share instead was wrong in
    /// the worst direction: <c>MainView</c> only calls a sheet that says it can, so on Firefox, and
    /// on Chrome and Edge under Linux, a false here skipped straight past the clipboard and download
    /// fallbacks in main.js that exist for exactly those browsers. What actually happened is reported
    /// by the outcome instead, which is the honest place for it.
    /// </remarks>
    public bool CanShareText => true;

    /// <inheritdoc cref="CanShareText"/>
    public bool CanShareImages => true;

    public async Task<ShareOutcome> ShareTextAsync(SharePayload payload)
    {
        try
        {
            return Parse(await BrowserInterop.ShareLink(payload.Title, payload.Text, payload.Url));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Browser refused to share a link");
            return ShareOutcome.Failed;
        }
    }

    public async Task<ShareOutcome> ShareImageAsync(SharePayload payload, byte[] png, string fileName)
    {
        try
        {
            // The text carries the link, because a share of files takes no url of its own.
            var text = $"{payload.Text}\n{payload.Url}";
            return Parse(await BrowserInterop.ShareImage(payload.Title, text, Convert.ToBase64String(png), fileName));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Browser refused to share a timing card");
            return ShareOutcome.Failed;
        }
    }

    private static ShareOutcome Parse(string? outcome) => outcome switch
    {
        "shared" => ShareOutcome.Shared,
        "copied" => ShareOutcome.Copied,
        "saved" => ShareOutcome.Saved,
        "dismissed" => ShareOutcome.Dismissed,
        _ => ShareOutcome.Failed,
    };
}
