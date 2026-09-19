using RedMist.Timing.UI.Models;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// The platform's native share sheet, where it has one.
/// </summary>
/// <remarks>
/// Avalonia has no share sheet of its own, so each head supplies its own implementation the way it
/// supplies <see cref="IScreenWakeService"/> - see <c>App.ShareSheetFactory</c>. What happens when a
/// platform cannot share is decided by the caller, not here: <c>MainView</c> falls back to the
/// clipboard for a link and to a save dialog for an image, so no share button can quietly do
/// nothing.
/// </remarks>
public interface IShareSheet
{
    /// <summary>Whether this platform can hand text and a link to another app.</summary>
    bool CanShareText { get; }

    /// <summary>
    /// Whether it will take an image file. Separate from <see cref="CanShareText"/> because support
    /// is per kind: a platform that shares text may still refuse a file.
    /// </summary>
    bool CanShareImages { get; }

    Task<ShareOutcome> ShareTextAsync(SharePayload payload);

    /// <param name="png">The card, as PNG bytes.</param>
    /// <param name="fileName">
    /// What the recipient sees the file called, which is also what a save lands under.
    /// </param>
    Task<ShareOutcome> ShareImageAsync(SharePayload payload, byte[] png, string fileName);
}

/// <summary>
/// The share sheet on a platform that has none - desktop, and any head that has not wired one up.
/// </summary>
/// <remarks>
/// Both share methods are unreachable while callers check the two properties first, and they answer
/// <see cref="ShareOutcome.Failed"/> rather than throwing so that a caller which forgets to is wrong
/// about the outcome rather than broken.
/// </remarks>
public sealed class NoShareSheet : IShareSheet
{
    public bool CanShareText => false;

    public bool CanShareImages => false;

    public Task<ShareOutcome> ShareTextAsync(SharePayload payload) => Task.FromResult(ShareOutcome.Failed);

    public Task<ShareOutcome> ShareImageAsync(SharePayload payload, byte[] png, string fileName)
        => Task.FromResult(ShareOutcome.Failed);
}
