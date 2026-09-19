using System;
using System.Text;

namespace RedMist.Timing.UI.Utilities;

/// <summary>
/// The file name a shared card travels under.
/// </summary>
/// <remarks>
/// It is the name the recipient sees attached to the message and the one a save lands under, so it
/// is built from readable words rather than ids, and reduced to characters every filesystem and
/// share target accepts. Mirrors the web app's <c>src/shared/share-card-name.ts</c>.
/// </remarks>
public static class ShareCardName
{
    /// <summary>Beyond this the name stops being readable and starts risking path limits.</summary>
    private const int MaxSlugLength = 60;

    public static string For(params string?[] parts)
    {
        var slug = new StringBuilder();
        foreach (var part in parts ?? [])
        {
            Append(slug, part);
        }

        // Anything that is not a letter or digit became a dash above, including the joins between
        // parts, so trimming is all that is left to do.
        var trimmed = slug.ToString().Trim('-');
        if (trimmed.Length > MaxSlugLength)
        {
            trimmed = trimmed[..MaxSlugLength].TrimEnd('-');
        }

        return $"red-mist-{(trimmed.Length == 0 ? "timing" : trimmed)}.png";
    }

    /// <summary>
    /// Adds one part, lower-cased, with every run of other characters collapsed to a single dash.
    /// </summary>
    private static void Append(StringBuilder slug, string? part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            return;
        }

        if (slug.Length > 0 && slug[^1] != '-')
        {
            slug.Append('-');
        }

        foreach (var character in part.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                slug.Append(char.ToLowerInvariant(character));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }
    }
}
