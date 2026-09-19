using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using System.Collections.Generic;

namespace RedMist.Timing.UI.ViewModels;

/// <summary>
/// Turns the rows a viewer is looking at into the rows the card draws.
/// </summary>
/// <remarks>
/// Separate from both the grid and the renderer because this is the part with decisions in it - what
/// order, how many, which car gets picked out - and it is the part worth testing. Deciding "what is
/// on screen" is the caller's job; see <c>LiveTimingViewModel.OnScreenCars</c>.
/// </remarks>
internal static class ShareStandings
{
    /// <summary>
    /// Kept in step with the renderer's own cap, so the "+N more" count on the card is honest.
    /// </summary>
    internal const int MaxRows = StandingsCardRenderer.MaxRows;

    /// <summary>
    /// The card's rows, in the order they are shown, plus how many were left out.
    /// </summary>
    /// <param name="onScreen">
    /// The rows the viewer can see, in the order the table shows them - so any search, grouping or
    /// sort they have applied is already reflected, which is the point. "What I am looking at" is the
    /// thing worth sending.
    /// </param>
    /// <param name="field">
    /// Every car in the grid, searched only to find <paramref name="highlightCarNumber"/> when that
    /// car is not among the ones on screen.
    /// </param>
    /// <param name="highlightCarNumber">
    /// The car the card is being shared from, or null when the whole event is being shared.
    /// </param>
    internal static StandingsSelection Select(
        IReadOnlyList<CarViewModel> onScreen,
        IReadOnlyList<CarViewModel> field,
        string? highlightCarNumber)
    {
        var chosen = new List<CarViewModel>(onScreen.Count + 1);
        var seen = new HashSet<string>();
        foreach (var car in onScreen)
        {
            // A car can be realized twice - a row and a duplicate view during a transition - and the
            // same car twice in the standings is the kind of wrong that a reader spots immediately.
            if (car is not null && seen.Add(car.Number))
            {
                chosen.Add(car);
            }
        }

        // The car being shared belongs on its own card even when it has been scrolled past, or its
        // open details panel has pushed its row off the bottom of the screen.
        //
        // Put at the front rather than sorted into place, which does mean the positions can read out
        // of order - "20, 11, 12, 13". Sorting it in would need a comparison that holds across every
        // way the table can be ordered, and there is no such number: grouped by class the position
        // column restarts at 1 for each class, so a car from a later class would be filed among the
        // wrong ones. First is at least unambiguous, it is where the eye starts, and the highlight
        // band says what it is. The web card does the same.
        if (!string.IsNullOrEmpty(highlightCarNumber) && !seen.Contains(highlightCarNumber))
        {
            foreach (var car in field)
            {
                if (car is not null && car.Number == highlightCarNumber)
                {
                    chosen.Insert(0, car);
                    break;
                }
            }
        }

        var omitted = chosen.Count > MaxRows ? chosen.Count - MaxRows : 0;
        var rows = new List<StandingsCardRow>(chosen.Count - omitted);
        for (var index = 0; index < chosen.Count && index < MaxRows; index++)
        {
            rows.Add(ToRow(chosen[index]));
        }

        return new StandingsSelection(rows, omitted);
    }

    /// <remarks>
    /// Position comes from the row's own <see cref="CarViewModel.Position"/> rather than from the
    /// overall or in-class field directly, because that is the number on screen: it already accounts
    /// for grouping by class and for the position override that sorting by fastest lap installs.
    /// </remarks>
    private static StandingsCardRow ToRow(CarViewModel car) => new()
    {
        Position = car.Position,
        CarNumber = car.Number,
        Name = car.Name,
        ClassName = car.Class,
        Laps = car.LastLap,
        BestTime = car.BestTimeShort,
    };
}

/// <param name="Rows">At most <see cref="ShareStandings.MaxRows"/> of them.</param>
/// <param name="Omitted">How many more were on screen than the card could take.</param>
internal sealed record StandingsSelection(IReadOnlyList<StandingsCardRow> Rows, int Omitted);
