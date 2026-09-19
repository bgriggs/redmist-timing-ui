using CommunityToolkit.Mvvm.Messaging.Messages;

namespace RedMist.Timing.UI.Models;

/// <summary>
/// What a share attempt actually did, so the caller can say something true about it.
/// </summary>
/// <remarks>
/// <see cref="Dismissed"/> is the share sheet being closed without choosing anything, which is a
/// decision rather than a failure and is not reported as one.
/// </remarks>
public enum ShareOutcome
{
    /// <summary>Handed to the platform's share sheet.</summary>
    Shared,
    /// <summary>Put on the clipboard, because this platform has no share sheet.</summary>
    Copied,
    /// <summary>Written to a file the viewer picked.</summary>
    Saved,
    /// <summary>The viewer closed the share sheet without choosing anything.</summary>
    Dismissed,
    /// <summary>Nothing could be done with it.</summary>
    Failed,
}

/// <summary>What travels with a share, whether it carries an image or only a link.</summary>
public sealed class SharePayload
{
    public required string Title { get; init; }
    public required string Text { get; init; }
    public required string Url { get; init; }
}

/// <summary>
/// Asks the platform to share a link, the way <see cref="LauncherEvent"/> asks it to open one.
/// </summary>
/// <remarks>
/// A request message rather than a plain notification, because the view model has to say what
/// happened - a link that went to the clipboard instead of a share sheet needs saying, and one that
/// went nowhere needs saying more. Sent with no recipient registered, <c>HasReceivedResponse</c>
/// stays false, which is how a head with nothing wired up reports failure rather than silence.
/// </remarks>
public sealed class ShareRequest(SharePayload payload) : AsyncRequestMessage<ShareOutcome>
{
    public SharePayload Payload { get; } = payload;
}

/// <summary>
/// Asks the platform to share an image file.
/// </summary>
/// <remarks>
/// The image is the whole point of the feature on a phone: a link preview only appears if the
/// receiving app fetches the URL and reads Open Graph tags out of HTML the client-rendered site
/// never serves a crawler, whereas an image shared as a file is the message.
/// </remarks>
public sealed class ShareImageRequest(SharePayload payload, byte[] image, string fileName)
    : AsyncRequestMessage<ShareOutcome>
{
    public SharePayload Payload { get; } = payload;

    /// <summary>The card, as PNG bytes.</summary>
    public byte[] Image { get; } = image;

    public string FileName { get; } = fileName;
}
