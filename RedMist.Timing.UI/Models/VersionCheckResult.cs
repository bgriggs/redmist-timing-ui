using System;

namespace RedMist.Timing.UI.Models;

/// <summary>
/// Enumeration of supported application platforms for version checking
/// </summary>
public enum AppPlatform
{
    iOS,
    Android,
    Browser,    // Web/WASM
    Desktop     // Excluded from version checking per spec
}

/// <summary>
/// Enumeration indicating the type of update action required
/// </summary>
public enum UpdateRequirement
{
    None,           // No update needed
    Optional,       // Update recommended but not required
    Mandatory       // Update required to continue
}

/// <summary>
/// Encapsulates the result of version comparison and appropriate user messaging
/// </summary>
public class VersionCheckResult
{
    /// <summary>
    /// The update action required (None/Optional/Mandatory)
    /// </summary>
    public UpdateRequirement Requirement { get; set; }
    
    /// <summary>
    /// User-facing message explaining the update situation
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Platform-specific URL for updating (App Store, Play Store, or null for browser)
    /// </summary>
    public string? ActionUrl { get; set; }
    
    /// <summary>
    /// The current platform (for logging/analytics)
    /// </summary>
    public AppPlatform Platform { get; set; }
    
    /// <summary>
    /// The running application version
    /// </summary>
    public Version CurrentVersion { get; set; } = new Version(1, 0, 0);
    
    /// <summary>
    /// The latest available version from server (nullable if server doesn't provide)
    /// </summary>
    public Version? LatestVersion { get; set; }
    
    /// <summary>
    /// The minimum required version from server (nullable if server doesn't provide)
    /// </summary>
    public Version? MinimumVersion { get; set; }
}
