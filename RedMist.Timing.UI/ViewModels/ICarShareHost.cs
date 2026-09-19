using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// What a car row calls to share itself.
/// </summary>
/// <remarks>
/// A row knows only itself; the grid it belongs to knows the event, the session and what is on
/// screen. Handed to each row as it is built rather than found through the messenger, because two
/// grids can be alive at once - the live tab and a stored session under the results tab - and a
/// broadcast would have both of them answer for a row belonging to one.
/// </remarks>
internal interface ICarShareHost
{
    /// <summary>
    /// Whether this event may be shared at all. A private event offers nothing; see
    /// <c>LiveTimingViewModel.CanShare</c>.
    /// </summary>
    bool CanShare { get; }

    Task ShareCarLinkAsync(CarViewModel car);

    Task ShareCarCardAsync(CarViewModel car);
}
