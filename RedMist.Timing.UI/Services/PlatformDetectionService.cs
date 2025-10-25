using System;
using RedMist.Timing.UI.Models;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Service for detecting the current application platform.
/// </summary>
public interface IPlatformDetectionService
{
    /// <summary>
    /// Determines the current platform on which the application is running.
    /// </summary>
    /// <returns>The current application platform</returns>
    AppPlatform GetCurrentPlatform();
    
    /// <summary>
    /// Checks if the current platform should participate in version checking.
    /// </summary>
    /// <returns>True if version checking should be performed, false otherwise (e.g., Desktop)</returns>
    bool ShouldCheckVersion();
}

/// <summary>
/// Implementation of platform detection service using conditional compilation and System.OperatingSystem
/// </summary>
public class PlatformDetectionService : IPlatformDetectionService
{
    public AppPlatform GetCurrentPlatform()
    {
        // Use conditional compilation symbols provided by .NET SDK based on target framework
        #if ANDROID
            return AppPlatform.Android;
        #elif IOS
            return AppPlatform.iOS;
        #elif BROWSER
            return AppPlatform.Browser;
        #else
            // Fallback: Use System.OperatingSystem for runtime detection
            if (OperatingSystem.IsAndroid())
                return AppPlatform.Android;
            if (OperatingSystem.IsIOS())
                return AppPlatform.iOS;
            if (OperatingSystem.IsBrowser())
                return AppPlatform.Browser;
            
            return AppPlatform.Desktop;
        #endif
    }
    
    public bool ShouldCheckVersion()
    {
        // Desktop is development-only, exclude from version checking
        var platform = GetCurrentPlatform();
        return platform != AppPlatform.Desktop;
    }
}
