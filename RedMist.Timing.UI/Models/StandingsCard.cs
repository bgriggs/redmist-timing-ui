using System.Collections.Generic;

namespace RedMist.Timing.UI.Models;

/// <summary>One row of the shared standings card.</summary>
public sealed class StandingsCardRow
{
    /// <summary>The position the row is showing, which is not always the overall one.</summary>
    public int Position { get; init; }
    public string CarNumber { get; init; } = string.Empty;
    /// <summary>The entry name the timing row shows, e.g. "ROUND 3 RACING".</summary>
    public string Name { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public int Laps { get; init; }
    /// <summary>Best lap, already shortened the way the table shows it.</summary>
    public string BestTime { get; init; } = string.Empty;
}

/// <summary>Everything the card draws.</summary>
/// <remarks>
/// Built in one go before any awaiting, so a live session patching positions mid-render cannot
/// leave the card and the message that carries it disagreeing.
/// </remarks>
public sealed class StandingsCardData
{
    public string EventName { get; init; } = string.Empty;
    public string SessionName { get; init; } = string.Empty;
    public bool IsLive { get; init; }

    /// <summary>The cars on screen, in the order they are shown, already capped.</summary>
    public IReadOnlyList<StandingsCardRow> Rows { get; init; } = [];

    /// <summary>How many more were on screen than the card could take.</summary>
    public int Omitted { get; init; }

    /// <summary>A car to pick out, when the card is shared from that car rather than the event.</summary>
    public string? HighlightCarNumber { get; init; }

    /// <summary>
    /// The site the card names in its footer, which is the same one its link points at - so a build
    /// aimed at a test site does not hand out a card advertising production.
    /// </summary>
    public string SiteHost { get; init; } = Utilities.ShareLinks.DefaultHost;
}
