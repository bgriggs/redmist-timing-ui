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
}