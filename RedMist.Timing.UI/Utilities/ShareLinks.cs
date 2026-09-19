using System;

namespace RedMist.Timing.UI.Utilities;

/// <summary>
/// The links the share buttons hand out, and the text that travels with them.
/// </summary>
/// <remarks>
/// Deliberately the same formats as the web app's <c>src/shared/share-links.ts</c>, so a link
/// shared from a phone and one shared from the site are the same link and open the same page. Any
/// change here belongs in both.
///
/// The origin is passed in rather than hardcoded so a build pointed at a test site keeps its links
/// on that site, which is what the web app gets for free by reading the address bar. See
/// <see cref="DefaultOrigin"/>.
/// </remarks>
public static class ShareLinks
{
    /// <summary>Where a shared link points when nothing configures it. See Share:SiteUrl.</summary>
    public const string DefaultOrigin = "https://redmist.racing";

    /// <summary>Configuration key holding the site a shared link should point at.</summary>
    public const string SiteUrlConfigurationKey = "Share:SiteUrl";

    /// <summary>Query parameter naming the car whose row should open on load.</summary>
    public const string CarParameter = "car";

    /// <summary>
    /// Query parameter marking where a link came from, so app-driven traffic can be told apart
    /// from traffic the site generated itself. The web app reads only <see cref="CarParameter"/>,
    /// so this is inert there.
    /// </summary>
    public const string SourceParameter = "src";

    /// <inheritdoc cref="SourceParameter"/>
    public const string AppSource = "app-share";

    /// <summary>
    /// The link to an event, or to one of its finished sessions.
    /// </summary>
    /// <param name="sessionId">
    /// Null while the event is live, so the link keeps following it as sessions change; a session id
    /// once a stored session is being looked at, because a result is a fixed thing.
    /// <para>
    /// Null - not zero - is what means "no session". The timing feed emits run number 0, so
    /// <c>/timing/5/0</c> is a real results page and testing the id for truthiness would silently
    /// rewrite it into the live view.
    /// </para>
    /// </param>
    public static string EventUrl(string origin, int eventId, int? sessionId)
    {
        var path = sessionId is null
            ? $"/timing/{eventId}"
            : $"/timing/{eventId}/{sessionId.Value}";
        return $"{Root(origin)}{path}?{SourceParameter}={AppSource}";
    }

    /// <summary>The same link, with the car whose row should open named on it.</summary>
    public static string CarUrl(string origin, int eventId, int? sessionId, string carNumber)
    {
        var path = sessionId is null
            ? $"/timing/{eventId}"
            : $"/timing/{eventId}/{sessionId.Value}";
        return $"{Root(origin)}{path}?{CarParameter}={Uri.EscapeDataString(carNumber ?? string.Empty)}" +
            $"&{SourceParameter}={AppSource}";
    }

    /// <summary>
    /// What the share sheet offers alongside a car's link.
    /// </summary>
    /// <remarks>
    /// The position is read when the button is pressed, so the text is a snapshot while the link
    /// stays live - which is why it is phrased as a standing fact about the car rather than a claim
    /// about right now.
    /// </remarks>
    public static string CarShareTitle(string carNumber, int classPosition, string className)
    {
        var where = classPosition > 0 && !string.IsNullOrWhiteSpace(className)
            ? $" - P{classPosition} in {className}"
            : string.Empty;
        return $"Car #{carNumber}{where}";
    }

    /// <inheritdoc cref="CarShareTitle"/>
    public static string CarShareText(string carNumber, int classPosition, string className,
        string eventName, bool isLive)
    {
        var headline = CarShareTitle(carNumber, classPosition, className);
        var where = string.IsNullOrWhiteSpace(eventName) ? string.Empty : $"{eventName} - ";
        return $"{headline}\n{where}{(isLive ? "live timing on Red Mist" : "results on Red Mist")}";
    }

    public static string EventShareText(string eventName, bool isLive)
    {
        var name = string.IsNullOrWhiteSpace(eventName) ? "This event" : eventName;
        return isLive
            ? $"{name} - follow live timing on Red Mist"
            : $"{name} - results on Red Mist";
    }

    /// <summary>
    /// The host an origin names, for the card to print. Falls back to the production host rather
    /// than to something unreadable when the configured value is not a URL.
    /// </summary>
    public static string HostOf(string origin)
    {
        return Uri.TryCreate(Root(origin), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : DefaultHost;
    }

    /// <inheritdoc cref="HostOf"/>
    public const string DefaultHost = "redmist.racing";

    /// <summary>
    /// The origin with any trailing slash taken off, so a configured value written either way
    /// yields one slash before the path rather than two.
    /// </summary>
    private static string Root(string origin)
    {
        var trimmed = (origin ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return DefaultOrigin;
        }

        return trimmed.TrimEnd('/');
    }
}
