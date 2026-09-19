using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RedMist.Timing.UI.Models;
using System;
using System.Globalization;
using System.IO;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Draws the image the share buttons hand out: the standings as the viewer has them on screen.
/// </summary>
/// <remarks>
/// This is the one share path that does not depend on a crawler. An Open Graph card only appears if
/// whatever the link was pasted into fetches the URL and reads tags out of HTML the site never
/// server-renders; an image shared as a file is the payload itself, so it shows up in Messages,
/// WhatsApp and a Facebook post regardless.
///
/// Drawn rather than screenshotted, and off the visual tree rather than captured from a control. The
/// table on screen is laid out for a phone in portrait and reads as noise once a feed scales it
/// down, so the same rows are redrawn at a size and contrast that survives one. Sizes and colors
/// match the web app's src/shared/share-card.ts so the two cards are recognizably one product; a
/// change to either belongs in both.
/// </remarks>
public static class StandingsCardRenderer
{
    private const int Width = 1200;
    private const int Height = 630;
    private const double Middle = Width / 2.0;
    private const double Margin = 64;
    private const double RightEdge = Width - Margin;

    /// <summary>Rows beyond this will not fit legibly, so the card says how many it left out.</summary>
    public const int MaxRows = 8;
    private const double RowHeight = 45;
    private const double FirstRowBaseline = 232;

    // Fixed brand colors: the card is always the dark mark, whatever theme the app is showing.
    private static readonly Color Ink = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static readonly Color InkMuted = Color.FromArgb(158, 0xFF, 0xFF, 0xFF);
    private static readonly Color InkFaint = Color.FromArgb(102, 0xFF, 0xFF, 0xFF);
    private static readonly Color Rule = Color.FromArgb(36, 0xFF, 0xFF, 0xFF);
    private static readonly Color Brand = Color.FromRgb(0xC9, 0x52, 0x3D);
    private static readonly Color Highlight = Color.FromArgb(77, 0xC9, 0x52, 0x3D);
    private static readonly Color BackdropTop = Color.FromRgb(0x1C, 0x0A, 0x08);
    private static readonly Color BackdropBottom = Color.FromRgb(0x2C, 0x12, 0x0E);

    private const string WordmarkUri = "avares://RedMist.Timing.UI/Assets/redmist.png";
    private const double WordmarkWidth = 190;

    /// <summary>
    /// Draws <paramref name="data"/> and returns it as PNG bytes.
    /// </summary>
    /// <remarks>
    /// Call on the UI thread. <see cref="RenderTargetBitmap"/> goes through the platform's render
    /// interface, which is the renderer's own, and the drawing is a few dozen text runs - little
    /// enough not to be worth moving off the thread that owns it.
    /// </remarks>
    public static byte[] Render(StandingsCardData data)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(Width, Height), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            Chrome(context);
            Header(context, data);

            if (data.Rows.Count == 0)
            {
                // Nothing on screen to show - a session that has not posted a field yet. Still worth
                // a card, just not a table.
                DrawCentered(context, data.IsLive ? "LIVE TIMING" : "SESSION RESULTS", Middle, 360, 40,
                    Width - 200, FontWeight.Bold, Ink);
            }
            else
            {
                ColumnHeadings(context);
                for (var index = 0; index < data.Rows.Count && index < MaxRows; index++)
                {
                    var row = data.Rows[index];
                    Row(context, row, FirstRowBaseline + index * RowHeight,
                        row.CarNumber == data.HighlightCarNumber);
                }
            }

            Footer(context, data);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    /// <summary>The chrome every card shares: backdrop, brand bar and wordmark.</summary>
    private static void Chrome(DrawingContext context)
    {
        var backdrop = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(BackdropTop, 0),
                new GradientStop(BackdropBottom, 1),
            },
        };
        context.FillRectangle(backdrop, new Rect(0, 0, Width, Height));

        // A brand bar along the top, so a card cropped to a thumbnail still reads as Red Mist.
        context.FillRectangle(new SolidColorBrush(Brand), new Rect(0, 0, Width, 8));

        // A card without the wordmark beats no card at all, so a missing or undecodable asset is not
        // allowed to take the share down with it.
        try
        {
            var wordmark = AssetImageCache.Get(WordmarkUri);
            var height = wordmark.Size.Width > 0
                ? wordmark.Size.Height / wordmark.Size.Width * WordmarkWidth
                : 0;
            if (height > 0)
            {
                context.DrawImage(wordmark, new Rect(Margin, 52, WordmarkWidth, height));
            }
        }
        catch (Exception)
        {
            // Drawn without it.
        }
    }

    /// <summary>Wordmark on the left, what is being looked at on the right.</summary>
    private static void Header(DrawingContext context, StandingsCardData data)
    {
        DrawRight(context, data.EventName?.Trim(), RightEdge, 80, 34, 620, FontWeight.Bold, Ink);
        DrawRight(context, data.SessionName?.Trim(), RightEdge, 112, 24, 620, FontWeight.Normal, InkMuted);
        context.FillRectangle(new SolidColorBrush(Rule), new Rect(Margin, 140, Width - Margin * 2, 1));
    }

    private static void ColumnHeadings(DrawingContext context)
    {
        DrawRight(context, "POS", 110, 176, 18, 80, FontWeight.Medium, InkFaint);
        DrawLeft(context, "CAR", 140, 176, 18, 200, FontWeight.Medium, InkFaint);
        DrawLeft(context, "CLASS", 730, 176, 18, 160, FontWeight.Medium, InkFaint);
        DrawRight(context, "LAPS", 980, 176, 18, 100, FontWeight.Medium, InkFaint);
        DrawRight(context, "BEST LAP", RightEdge, 176, 18, 160, FontWeight.Medium, InkFaint);
    }

    private static void Row(DrawingContext context, StandingsCardRow row, double baseline, bool isHighlighted)
    {
        if (isHighlighted)
        {
            // The car this card was shared from, picked out so the point of the image survives a
            // glance at it in a feed.
            var band = new Rect(Margin - 12, baseline - 30, Width - (Margin - 12) * 2, 40);
            context.DrawRectangle(new SolidColorBrush(Highlight), null, new RoundedRect(band, 6));
            context.DrawRectangle(new SolidColorBrush(Brand), null,
                new RoundedRect(new Rect(Margin - 12, baseline - 30, 4, 40), 2));
        }

        var secondary = isHighlighted ? Ink : InkMuted;
        var nameWeight = isHighlighted ? FontWeight.Bold : FontWeight.Medium;

        DrawRight(context, row.Position > 0 ? row.Position.ToString(CultureInfo.InvariantCulture) : null,
            110, baseline, 26, 80, FontWeight.Bold, secondary);
        DrawLeft(context, "#" + row.CarNumber, 140, baseline, 26, 120, FontWeight.Bold, Ink);
        DrawLeft(context, row.Name?.ToUpperInvariant(), 268, baseline, 24, 440, nameWeight, secondary);
        // 140 rather than the heading's own allowance: a class runs from x=730 and the lap count is
        // right-aligned at 980 over up to 100px, so anything past 870 could meet a four-figure lap
        // count coming the other way. Class names are short enough that this never bites.
        DrawLeft(context, row.ClassName?.ToUpperInvariant(), 730, baseline, 22, 140, FontWeight.Medium, InkMuted);
        DrawRight(context, row.Laps > 0 ? row.Laps.ToString(CultureInfo.InvariantCulture) : null,
            980, baseline, 24, 100, FontWeight.Medium, InkMuted);
        DrawRight(context, row.BestTime, RightEdge, baseline, 24, 170, FontWeight.Bold, Ink);
    }

    private static void Footer(DrawingContext context, StandingsCardData data)
    {
        context.FillRectangle(new SolidColorBrush(Rule), new Rect(Margin, 562, Width - Margin * 2, 1));

        var left = data.Omitted > 0
            ? $"+{data.Omitted} more {(data.Omitted == 1 ? "car" : "cars")}"
            : data.IsLive ? "Live timing" : "Results";
        DrawLeft(context, left, Margin, 598, 22, 500, FontWeight.Normal, InkMuted);
        DrawRight(context, data.SiteHost, RightEdge, 598, 22, 400, FontWeight.Medium, InkMuted);
    }

    // ---------------------------------------------------------------------------------------------
    // Drawing helpers. Every text call takes a maximum width: entry names, class names and event
    // names are all free text, and one long one must not run into the next column or off the card.
    // ---------------------------------------------------------------------------------------------

    private static void DrawLeft(DrawingContext context, string? value, double x, double baseline,
        double size, double maxWidth, FontWeight weight, Color fill)
        => Draw(context, value, x, baseline, size, maxWidth, weight, fill, TextAnchor.Left);

    private static void DrawRight(DrawingContext context, string? value, double x, double baseline,
        double size, double maxWidth, FontWeight weight, Color fill)
        => Draw(context, value, x, baseline, size, maxWidth, weight, fill, TextAnchor.Right);

    private static void DrawCentered(DrawingContext context, string? value, double x, double baseline,
        double size, double maxWidth, FontWeight weight, Color fill)
        => Draw(context, value, x, baseline, size, maxWidth, weight, fill, TextAnchor.Center);

    private enum TextAnchor { Left, Right, Center }

    /// <summary>
    /// Draws text at a baseline, shrinking it to fit the space before falling back to trimming it.
    /// </summary>
    /// <remarks>
    /// Shrinking first keeps the proportions of a name that is only a little too long; the ellipsis
    /// is what stops anything overrunning regardless of length. The y is a baseline rather than a top
    /// edge, because that is what the columns line up on and what the web card's coordinates are
    /// expressed in - Avalonia draws from the top, hence the subtraction at the end.
    /// </remarks>
    private static void Draw(DrawingContext context, string? value, double x, double baseline,
        double size, double maxWidth, FontWeight weight, Color fill, TextAnchor anchor)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var brush = new SolidColorBrush(fill);

        // Measured unconstrained to decide the size. A constrained measure reports the width it was
        // given, not the width the text wants, so asking it whether the text fits always answers yes
        // and nothing would ever shrink.
        var minimum = Math.Max(12, Math.Round(size * 0.7));
        while (size > minimum && Measure(value, size, weight, brush, maxWidth: null).Width > maxWidth)
        {
            size -= 1;
        }

        var text = Measure(value, size, weight, brush, maxWidth);

        var left = anchor switch
        {
            TextAnchor.Right => x - text.Width,
            TextAnchor.Center => x - text.Width / 2,
            _ => x,
        };

        context.DrawText(text, new Point(left, baseline - text.Baseline));
    }

    /// <param name="maxWidth">
    /// The column's width, or null to ask how wide the text would rather be. Constrained, both
    /// settings matter: MaxTextWidth is what the trimming measures against, and without the trimming
    /// a name past it would wrap onto a second line across the row below.
    /// </param>
    private static FormattedText Measure(string value, double size, FontWeight weight, IBrush brush,
        double? maxWidth)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush)
        {
            MaxLineCount = 1,
            Trimming = maxWidth is null ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };

        if (maxWidth is double width)
        {
            text.MaxTextWidth = width;
        }

        return text;
    }
}
