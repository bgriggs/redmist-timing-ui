using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Models;
using RedMist.TimingCommon;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Service for checking application version against server requirements
/// and determining update actions needed.
/// </summary>
public interface IVersionCheckService
{
    /// <summary>
    /// Checks the current application version against server requirements
    /// and returns the appropriate update action.
    /// </summary>
    /// <param name="currentVersion">The current running application version</param>
    /// <param name="versionInfo">Server-provided version requirements with boolean flags</param>
    /// <param name="platform">The current platform (iOS/Android/Browser)</param>
    /// <returns>Version check result with update requirement and messaging</returns>
    /// <remarks>
    /// The UIVersionInfo model contains server-side decision flags:
    /// - IsIOSMinimumMandatory/IsAndroidMinimumMandatory/IsWebMinimumMandatory
    /// - RecommendIOSUpdate/RecommendAndroidUpdate/RecommendWebUpdate
    /// These flags should be respected rather than implementing independent version comparison logic.
    /// </remarks>
    VersionCheckResult CheckVersion(Version currentVersion, UIVersionInfo versionInfo, AppPlatform platform);
    
    /// <summary>
    /// Retrieves version information from the server with timeout handling.
    /// </summary>
    /// <param name="timeoutSeconds">Maximum time to wait for server response (default 5 seconds)</param>
    /// <returns>Server version info or null if timeout/error occurs</returns>
    Task<UIVersionInfo?> GetVersionInfoAsync(int timeoutSeconds = 5);
    
    /// <summary>
    /// Gets the current application version using Assembly reflection.
    /// </summary>
    /// <returns>Current application version, or 1.0.0 if detection fails</returns>
    Version GetCurrentApplicationVersion();
}

/// <summary>
/// Implementation of version check service with version comparison logic
/// </summary>
public class VersionCheckService : IVersionCheckService
{
    private readonly EventClient _eventClient;
    private readonly IUpdateMessageService _messageService;
    private readonly ILogger<VersionCheckService> _logger;
    
    public VersionCheckService(
        EventClient eventClient,
        IUpdateMessageService messageService,
        ILogger<VersionCheckService> logger)
    {
        _eventClient = eventClient;
        _messageService = messageService;
        _logger = logger;
    }
    
    public Version GetCurrentApplicationVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            
            if (version == null || version.Major == 0)
            {
                // Fallback to attribute-based version
                var attribute = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                
                if (attribute != null && Version.TryParse(attribute.InformationalVersion, out var parsedVersion))
                {
                    return parsedVersion;
                }
                
                _logger.LogWarning("Could not determine application version, using fallback 1.0.0");
                return new Version(1, 0, 0);
            }
            
            return version;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting application version, using fallback 1.0.0");
            return new Version(1, 0, 0);
        }
    }
    
    public async Task<UIVersionInfo?> GetVersionInfoAsync(int timeoutSeconds = 5)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        
        try
        {
            // Create timeout task
            var versionTask = _eventClient.LoadUIVersionInfoAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cts.Token);
            
            var completedTask = await Task.WhenAny(versionTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Version check timed out after {TimeoutSeconds} seconds", timeoutSeconds);
                return null;
            }
            
            return await versionTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve version information from server");
            return null;
        }
    }
    
    public VersionCheckResult CheckVersion(Version currentVersion, UIVersionInfo versionInfo, AppPlatform platform)
    {
        // Get platform-specific version strings and flags
        var (minimumVersionStr, latestVersionStr, isMinimumMandatory, recommendUpdate) = platform switch
        {
            AppPlatform.iOS => (
                versionInfo.MinimumIOSVersion,
                versionInfo.LatestIOSVersion,
                versionInfo.IsIOSMinimumMandatory,
                versionInfo.RecommendIOSUpdate),
            
            AppPlatform.Android => (
                versionInfo.MinimumAndroidVersion,
                versionInfo.LatestAndroidVersion,
                versionInfo.IsAndroidMinimumMandatory,
                versionInfo.RecommendAndroidUpdate),
            
            AppPlatform.Browser => (
                versionInfo.MinimumWebVersion,
                versionInfo.LatestWebVersion,
                versionInfo.IsWebMinimumMandatory,
                versionInfo.RecommendWebUpdate),
            
            _ => (string.Empty, string.Empty, false, false)
        };
        
        // Parse version strings
        Version? minimumVersion = null;
        Version? latestVersion = null;
        
        if (!string.IsNullOrEmpty(minimumVersionStr) && !Version.TryParse(minimumVersionStr, out minimumVersion))
        {
            _logger.LogWarning("Could not parse minimum version string: {VersionString}", minimumVersionStr);
        }
        
        if (!string.IsNullOrEmpty(latestVersionStr) && !Version.TryParse(latestVersionStr, out latestVersion))
        {
            _logger.LogWarning("Could not parse latest version string: {VersionString}", latestVersionStr);
        }
        
        // Determine update requirement based on server flags and version comparison
        UpdateRequirement requirement;
        
        // Check for mandatory update (server flag AND version below minimum)
        if (isMinimumMandatory && minimumVersion != null && currentVersion < minimumVersion)
        {
            requirement = UpdateRequirement.Mandatory;
        }
        // Check for recommended update (server flag OR version below latest but not mandatory)
        else if (recommendUpdate || (latestVersion != null && currentVersion < latestVersion))
        {
            requirement = UpdateRequirement.Optional;
        }
        else
        {
            requirement = UpdateRequirement.None;
        }
        
        // Build result
        var result = new VersionCheckResult
        {
            Requirement = requirement,
            Platform = platform,
            CurrentVersion = currentVersion,
            LatestVersion = latestVersion,
            MinimumVersion = minimumVersion
        };
        
        // Add message and action URL if update is needed
        if (requirement != UpdateRequirement.None)
        {
            result.Message = _messageService.GetUpdateMessage(requirement, platform);
            result.ActionUrl = _messageService.GetActionUrl(platform);
        }
        
        return result;
    }
}
