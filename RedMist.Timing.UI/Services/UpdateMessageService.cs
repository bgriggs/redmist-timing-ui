using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Models;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Service for generating platform-specific update messages and action URLs.
/// </summary>
public interface IUpdateMessageService
{
    /// <summary>
    /// Generates a user-facing message for the given update requirement and platform.
    /// </summary>
    /// <param name="requirement">The type of update required</param>
    /// <param name="platform">The current platform</param>
    /// <returns>Localized user message</returns>
    string GetUpdateMessage(UpdateRequirement requirement, AppPlatform platform);
    
    /// <summary>
    /// Gets the platform-specific action URL for updating the application.
    /// </summary>
    /// <param name="platform">The current platform</param>
    /// <returns>URL to app store or null for browser platform</returns>
    string? GetActionUrl(AppPlatform platform);
}

/// <summary>
/// Implementation of update message service with platform-specific messages
/// </summary>
public class UpdateMessageService : IUpdateMessageService
{
    private readonly IConfiguration _configuration;
    
    public UpdateMessageService(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    
    public string GetUpdateMessage(UpdateRequirement requirement, AppPlatform platform)
    {
        return (requirement, platform) switch
        {
            (UpdateRequirement.Mandatory, AppPlatform.iOS) => 
                "A mandatory update is required to continue using Red Mist Timing. Please update to the latest version from the App Store.",
            
            (UpdateRequirement.Mandatory, AppPlatform.Android) => 
                "A mandatory update is required to continue using Red Mist Timing. Please update to the latest version from the Play Store.",
            
            (UpdateRequirement.Mandatory, AppPlatform.Browser) => 
                "A mandatory update is required to continue using Red Mist Timing. Please refresh your browser (Ctrl+F5 or Cmd+Shift+R) to load the latest version.",
            
            (UpdateRequirement.Optional, AppPlatform.iOS) => 
                "A new version of Red Mist Timing is available. Update from the App Store for the latest features and improvements.",
            
            (UpdateRequirement.Optional, AppPlatform.Android) => 
                "A new version of Red Mist Timing is available. Update from the Play Store for the latest features and improvements.",
            
            (UpdateRequirement.Optional, AppPlatform.Browser) => 
                "A new version of Red Mist Timing is available. Please refresh your browser (Ctrl+F5 or Cmd+Shift+R) to load the latest version.",
            
            _ => string.Empty
        };
    }
    
    public string? GetActionUrl(AppPlatform platform)
    {
        return platform switch
        {
            AppPlatform.iOS => _configuration["VersionCheck:iOSAppStoreUrl"],
            AppPlatform.Android => _configuration["VersionCheck:AndroidPlayStoreUrl"],
            AppPlatform.Browser => null, // No URL for browser, just refresh instructions
            _ => null
        };
    }
}
