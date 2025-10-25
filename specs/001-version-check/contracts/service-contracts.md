# Service Contracts: Version Checking

**Date**: 2025-10-24  
**Feature**: 001-version-check

## IVersionCheckService Interface

**Purpose**: Provides version checking capabilities for the application

```csharp
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
    VersionCheckResult CheckVersion(
        Version currentVersion, 
        UIVersionInfo versionInfo, 
        AppPlatform platform);
    
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
```

---

## IPlatformDetectionService Interface

**Purpose**: Provides platform detection capabilities

```csharp
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
```

---

## IUpdateMessageService Interface

**Purpose**: Provides platform-specific update messaging and action URLs

```csharp
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
```

---

## Service Dependencies

### VersionCheckService Implementation Dependencies
- `IEventClient` (existing) - for retrieving server version info via LoadUIVersionInfoAsync
- `ILogger<VersionCheckService>` - for logging errors and warnings
- `IUpdateMessageService` - for generating platform-specific messages

### PlatformDetectionService Implementation Dependencies
- None (uses conditional compilation and runtime OS detection)

### UpdateMessageService Implementation Dependencies
- `IConfiguration` (optional) - for configurable App Store/Play Store URLs

---

## Service Lifecycle

All services should be registered as **Singletons** in dependency injection:

```csharp
services.AddSingleton<IPlatformDetectionService, PlatformDetectionService>();
services.AddSingleton<IVersionCheckService, VersionCheckService>();
services.AddSingleton<IUpdateMessageService, UpdateMessageService>();
```

Rationale:
- No mutable state in services
- Platform detection is constant for app lifetime
- Version checking logic is stateless
- Reduces object allocation overhead

---

## Error Handling Contracts

### Network Failures
- `GetVersionInfoAsync` returns `null` on timeout or network error
- Caller should handle null and allow app to proceed gracefully
- Service logs warning/error internally

### Version Parsing Failures
- `CheckVersion` treats unparseable version strings as null
- Returns `UpdateRequirement.None` when version info incomplete
- Service logs warning internally

### Platform Detection Failures
- `GetCurrentPlatform` defaults to `AppPlatform.Desktop`
- `ShouldCheckVersion` returns `false` for unknown platforms
- Service logs error internally

---

## Usage Example

```csharp
public class MainViewModel : ViewModelBase
{
    private readonly IVersionCheckService versionCheckService;
    private readonly IPlatformDetectionService platformDetectionService;
    
    public MainViewModel(
        IVersionCheckService versionCheckService,
        IPlatformDetectionService platformDetectionService)
    {
        this.versionCheckService = versionCheckService;
        this.platformDetectionService = platformDetectionService;
    }
    
    public async Task InitializeAsync()
    {
        // Check if version checking should be performed
        if (!platformDetectionService.ShouldCheckVersion())
        {
            await LoadEventsListAsync();
            return;
        }
        
        // Get current platform
        var platform = platformDetectionService.GetCurrentPlatform();
        
        // Get version info from server (with timeout)
        var versionInfo = await versionCheckService.GetVersionInfoAsync();
        
        // If version info unavailable (timeout/error), proceed anyway
        if (versionInfo == null)
        {
            await LoadEventsListAsync();
            return;
        }
        
        // Get current app version
        var currentVersion = versionCheckService.GetCurrentApplicationVersion();
        
        // Check version and get result
        var result = versionCheckService.CheckVersion(currentVersion, versionInfo, platform);
        
        // Handle result based on requirement
        switch (result.Requirement)
        {
            case UpdateRequirement.Mandatory:
                await ShowMandatoryUpdateDialogAsync(result);
                // Do NOT proceed to LoadEventsListAsync
                break;
                
            case UpdateRequirement.Optional:
                await ShowOptionalUpdateNotificationAsync(result);
                await LoadEventsListAsync();
                break;
                
            case UpdateRequirement.None:
                await LoadEventsListAsync();
                break;
        }
    }
}
```

---

## Testing Contracts

### Unit Test Requirements

**VersionCheckService Tests**:
- Test version comparison logic for all UpdateRequirement scenarios
- Test timeout handling (mock EventClient)
- Test version parsing error handling
- Test null version info handling

**PlatformDetectionService Tests**:
- Test platform detection for each supported platform (compile-time conditionals)
- Test ShouldCheckVersion returns false for Desktop
- Test ShouldCheckVersion returns true for iOS/Android/Browser

**UpdateMessageService Tests**:
- Test message generation for each platform and requirement combination
- Test action URL generation for iOS (App Store)
- Test action URL generation for Android (Play Store)
- Test null action URL for Browser

### Integration Test Requirements

**MainViewModel Integration Tests**:
- Test startup flow with mandatory update (blocks events list)
- Test startup flow with optional update (shows notification, loads events)
- Test startup flow with no update (silent, loads events)
- Test startup flow with timeout (logs warning, loads events)
- Test startup flow on Desktop (skips check, loads events)

---

## Configuration Requirements

### App Store URLs

Store URLs should be configurable via `appsettings.json`:

```json
{
  "VersionCheck": {
    "iOSAppStoreUrl": "https://apps.apple.com/app/redmist-timing/id123456789",
    "AndroidPlayStoreUrl": "https://play.google.com/store/apps/details?id=com.bigmission.redmist",
    "TimeoutSeconds": 5
  }
}
```

### Feature Flags

Optional feature flag to enable/disable version checking:

```json
{
  "VersionCheck": {
    "Enabled": true
  }
}
```

This allows temporary disabling if server issues occur without code changes.
