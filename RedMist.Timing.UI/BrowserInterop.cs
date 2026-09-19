using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace RedMist.Timing.UI;

public partial class BrowserInterop
{
    public static async Task InitializeJsModuleAsync()
    {
        if (OperatingSystem.IsBrowser())
        {
            await JSHost.ImportAsync("main.js", "/main.js");
        }
    }

    [JSImport("getCurrentUrl", "main.js")]
    public static partial string GetCurrentUrl();

    // Import the JavaScript function to get a query parameter
    [JSImport("getQueryParameter", "main.js")]
    public static partial string GetQueryParameter(string param);

    /// <summary>
    /// Shares a link, falling back to the clipboard where the browser has no share sheet.
    /// </summary>
    /// <remarks>
    /// Whether navigator.share exists is decided in JavaScript rather than asked about from here.
    /// It exists only on secure origins and only in some desktop browsers - Firefox has none - and
    /// the fallbacks for the rest are the browser's own, so the whole decision belongs on that side
    /// of the boundary. See main.js.
    /// </remarks>
    /// <returns>One of the <c>ShareOutcome</c> names, lower-cased.</returns>
    [JSImport("shareLink", "main.js")]
    public static partial Task<string> ShareLink(string title, string text, string url);

    /// <param name="base64Png">
    /// The card, base64-encoded. Passed as a string rather than as bytes so the marshalling has no
    /// lifetime question in it; a card is a hundred kilobytes or so.
    /// </param>
    /// <inheritdoc cref="ShareLink"/>
    [JSImport("shareImage", "main.js")]
    public static partial Task<string> ShareImage(string title, string text, string base64Png, string fileName);
}
