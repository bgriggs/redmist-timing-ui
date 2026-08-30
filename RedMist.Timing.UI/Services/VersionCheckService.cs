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
    
    /// <summary>
    /// Fetches the server's version rules, or null if they could not be had in time.
    /// </summary>
    /// <remarks>
    /// Null is an ordinary answer here rather than a failure: the caller carries on without a
    /// version check, which is the right behavior for an optional call made over a phone's
    /// connection. Everything is therefore logged below the level that raises a crash report,
    /// unexpected failures included - this runs during startup on whatever network the user has,
    /// and a real defect in it shows up as the version check never working rather than as a crash.
    ///
    /// The timeout needs both halves below, because neither bounds this on its own. The token is
    /// what actually cancels the request, so it is not left running with nobody to await it; a
    /// late failure from an abandoned request arrives at TaskScheduler.UnobservedTaskException,
    /// which does report at error level. But the token never reaches RestSharp's authenticator,
    /// and the Keycloak token request behind it accepts none and so runs under HttpClient's 100
    /// second default - on the call that gates app startup. WaitAsync is the bound over that.
    /// </remarks>
    public async Task<UIVersionInfo?> GetVersionInfoAsync(int timeoutSeconds = 5)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var cancellation = new CancellationTokenSource(timeout);
        var deadline = cancellation.Token;
        var request = _eventClient.LoadUIVersionInfoAsync(deadline);

        // The request outlives this method whenever the wait below gives up on it, so it owns the
        // source rather than a using block here. Reading the exception is what marks it observed.
        _ = request.ContinueWith(static (task, state) =>
            {
                _ = task.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            cancellation,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            return await request.WaitAsync(timeout);
        }
        catch (Exception ex) when (ex is TimeoutException || deadline.IsCancellationRequested)
        {
            // Matching on the exception type would not work: RestSharp reports a canceled request
            // as an HttpRequestException wrapping the cancellation, not as one. The token is the
            // reliable signal for whether this was us giving up.
            _logger.LogWarning(ex, "Version check gave up after {TimeoutSeconds} seconds", timeoutSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve version information from server");
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
