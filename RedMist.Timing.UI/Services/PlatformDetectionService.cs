using System;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<PlatformDetectionService> _logger;
    
    public PlatformDetectionService(ILogger<PlatformDetectionService> logger)
    {
        _logger = logger;
    }
    
    public AppPlatform GetCurrentPlatform()
    {
        try
        {
            // T045: Use conditional compilation symbols provided by .NET SDK based on target framework
            #if ANDROID
                return AppPlatform.Android;
            #elif IOS
                return AppPlatform.iOS;
            #elif BROWSER
                return AppPlatform.Browser;
            #else
                // Fallback: Use System.OperatingSystem for runtime detection
                try
                {
                    if (OperatingSystem.IsAndroid())
                        return AppPlatform.Android;
                    if (OperatingSystem.IsIOS())
                        return AppPlatform.iOS;
                    if (OperatingSystem.IsBrowser())
                        return AppPlatform.Browser;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error detecting platform using System.OperatingSystem, defaulting to Desktop");
                }
                
                return AppPlatform.Desktop;
            #endif
        }
        catch (Exception ex)
        {
            // T045: Comprehensive error handling for platform detection failures
            _logger.LogError(ex, "Unexpected error during platform detection, defaulting to Desktop");
            return AppPlatform.Desktop;
        }
    }
    
    public bool ShouldCheckVersion()
    {
        try
        {
            // Desktop is development-only, exclude from version checking
            var platform = GetCurrentPlatform();
            var shouldCheck = platform != AppPlatform.Desktop;
            
            if (!shouldCheck)
            {
                _logger.LogInformation("Platform detection: {Platform} - version checking disabled", platform);
            }
            
            return shouldCheck;
        }
        catch (Exception ex)
        {
            // T045: Error handling - if platform detection fails, don't check version (safe default)
            _logger.LogError(ex, "Error determining if version check should run, defaulting to false (skip version check)");
            return false;
        }
    }
}
