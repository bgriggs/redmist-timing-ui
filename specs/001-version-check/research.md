# Research: Application Version Check and Update Notification

**Date**: 2025-10-24  
**Feature**: 001-version-check  
**Purpose**: Resolve technical unknowns and establish implementation patterns

## Research Tasks

### 1. Platform Detection in Avalonia

**Question**: How to detect iOS, Android, Web (WASM), and Desktop platforms in Avalonia?

**Research Findings**:

**Decision**: Use `RuntimeInformation.IsOSPlatform()` and Avalonia's conditional compilation symbols for platform detection.

**Implementation Approach**:
```csharp
public enum AppPlatform
{
    iOS,
    Android,
    Browser,
    Desktop
}

public static class PlatformDetectionService
{
    public static AppPlatform GetCurrentPlatform()
    {
        #if ANDROID
            return AppPlatform.Android;
        #elif IOS
            return AppPlatform.iOS;
        #elif BROWSER
            return AppPlatform.Browser;
        #else
            return AppPlatform.Desktop;
        #endif
    }
}
```

**Rationale**: Avalonia uses conditional compilation symbols (ANDROID, IOS, BROWSER) for each platform project. This is the most reliable and performant way to detect platform at compile time. Runtime detection using `OperatingSystem.Is*()` methods can supplement for edge cases.

**Alternatives Considered**:
- Runtime OS detection only: Rejected because it cannot distinguish between Desktop and Browser (both run on same OS)
- UserAgent parsing for browser: Rejected as unnecessary complexity when conditional compilation is available
- Avalonia.Platform APIs: Considered but conditional compilation is simpler and more explicit

**References**:
- Avalonia multi-platform architecture documentation
- .NET 8 OperatingSystem class for runtime detection
- Existing Avalonia platform-specific projects structure in repository

---

### 2. Assembly Version Retrieval in Avalonia

**Question**: How to reliably get assembly version using `Assembly.GetExecutingAssembly().GetName().Version` across all Avalonia platforms?

**Research Findings**:

**Decision**: Use `Assembly.GetExecutingAssembly().GetName().Version` with platform-specific fallbacks for version retrieval.

**Implementation Approach**:
```csharp
public static class VersionHelper
{
    public static Version GetApplicationVersion()
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
            
            // Ultimate fallback
            return new Version(1, 0, 0);
        }
        
        return version;
    }
}
```

**Rationale**: `Assembly.GetExecutingAssembly().GetName().Version` is the standard approach and works across all .NET platforms. However, some build configurations may not populate this, so we provide fallbacks to AssemblyInformationalVersionAttribute and a default version.

**Alternatives Considered**:
- Platform-specific version APIs (NSBundle for iOS, PackageInfo for Android): Rejected because it adds platform-specific code when .NET reflection works consistently
- Hard-coded version: Rejected as it requires manual updates and defeats the purpose
- Build timestamp as version: Rejected as it doesn't follow semantic versioning

**References**:
- .NET Assembly versioning documentation
- Avalonia packaging and versioning guidelines
- Existing version configuration in Directory.Build.props (SharedVersion property)

---

### 3. Dialog/Message Display Patterns in Avalonia

**Question**: What are the best practices for displaying blocking (mandatory update) vs. non-blocking (optional update) messages in Avalonia?

**Research Findings**:

**Decision**: Use Avalonia's `Window.ShowDialog<T>()` for mandatory updates (modal) and custom notification overlay for optional updates (non-modal).

**Implementation Approach**:
```csharp
// Mandatory update - blocks all interaction
public async Task ShowMandatoryUpdateDialog(string message, string actionUrl)
{
    var dialog = new UpdateRequiredDialog
    {
        Message = message,
        ActionUrl = actionUrl
    };
    
    await dialog.ShowDialog(mainWindow);
    // App cannot proceed until user takes action
}

// Optional update - dismissible notification
public void ShowOptionalUpdateNotification(string message, string actionUrl)
{
    var notification = new UpdateNotification
    {
        Message = message,
        ActionUrl = actionUrl,
        IsDismissible = true
    };
    
    // Show as overlay that can be dismissed
    NotificationPanel.Children.Add(notification);
}
```

**Rationale**: 
- Modal dialogs (`ShowDialog`) are appropriate for mandatory updates as they block all user interaction until addressed
- Non-modal overlays/notifications allow users to dismiss and continue, perfect for optional updates
- Follows platform conventions for critical vs. informational messages

**Alternatives Considered**:
- Same modal dialog for both: Rejected because it blocks users unnecessarily for optional updates
- Toast notifications for mandatory updates: Rejected because users could miss or dismiss critical messages
- Full-screen blocking page: Rejected as overly aggressive and poor UX for optional updates

**Platform-Specific Considerations**:
- **iOS**: Use standard alert dialogs, integrate with App Store links via URL scheme
- **Android**: Use standard dialogs, integrate with Play Store links via Intent
- **Browser/WASM**: Use custom styled dialogs, provide browser refresh instructions

**References**:
- Avalonia dialog documentation
- Platform-specific UI guidelines (iOS HIG, Material Design, Web accessibility)
- Existing dialog patterns in RedMist.Timing.UI codebase

---

### 4. Version Comparison Best Practices

**Question**: What is the most reliable way to compare semantic versions for determining mandatory vs. optional updates?

**Research Findings**:

**Decision**: Use `System.Version.CompareTo()` method with clear business logic for mandatory vs. optional distinction.

**Implementation Approach**:
```csharp
public enum UpdateRequirement
{
    None,           // Current version is up-to-date
    Optional,       // Newer version available, current meets minimum
    Mandatory       // Current version below minimum required
}

public static UpdateRequirement CheckVersionRequirement(
    Version currentVersion,
    Version minimumRequired,
    Version latestAvailable)
{
    // Mandatory if below minimum
    if (currentVersion.CompareTo(minimumRequired) < 0)
    {
        return UpdateRequirement.Mandatory;
    }
    
    // Optional if newer version available
    if (currentVersion.CompareTo(latestAvailable) < 0)
    {
        return UpdateRequirement.Optional;
    }
    
    // Up-to-date
    return UpdateRequirement.None;
}
```

**Rationale**: `System.Version.CompareTo()` is built-in, well-tested, and handles all semantic version comparison edge cases correctly. Clear enum-based result makes business logic explicit and testable.

**Alternatives Considered**:
- String comparison: Rejected as unreliable (e.g., "1.10.0" < "1.9.0" alphabetically)
- NuGet.Versioning library: Rejected as overkill for simple version comparison, adds dependency
- Custom comparison logic: Rejected when System.Version already handles it correctly

**Edge Cases Handled**:
- Development versions (higher than production): Return UpdateRequirement.None
- Missing version components: System.Version treats missing as 0
- Equal versions: CompareTo returns 0, handled correctly

**References**:
- System.Version documentation
- Semantic versioning specification
- Existing version handling in RedMist.TimingCommon.UIVersionInfo

---

### 5. Network Timeout and Error Handling

**Question**: How to handle network failures during version check to avoid blocking app startup indefinitely?

**Research Findings**:

**Decision**: Implement 5-second timeout with CancellationToken and graceful degradation on failure.

**Implementation Approach**:
```csharp
public async Task<UIVersionInfo?> GetVersionInfoWithTimeout()
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    
    try
    {
        var versionInfo = await eventClient.LoadUIVersionInfoAsync(cts.Token);
        return versionInfo;
    }
    catch (OperationCanceledException)
    {
        // Timeout - log and allow app to proceed
        logger.LogWarning("Version check timed out after 5 seconds");
        return null;
    }
    catch (Exception ex)
    {
        // Network or server error - log and allow app to proceed
        logger.LogError(ex, "Failed to retrieve version information");
        return null;
    }
}
```

**Rationale**: 
- 5-second timeout per spec ensures app doesn't hang on network issues
- Returning null on failure allows app to proceed gracefully
- User experience prioritized over strict version enforcement when server unavailable
- Logging ensures issues can be diagnosed

**Alternatives Considered**:
- Infinite retry: Rejected as it blocks user indefinitely
- Fail-fast (no timeout): Rejected as it could prevent app usage during temporary network issues
- Cached version info: Considered for future enhancement but not required for initial implementation

**Error Scenarios Handled**:
- Network timeout (5s)
- Server unavailable (HTTP errors)
- Malformed response data
- DNS resolution failure

**References**:
- CancellationToken timeout patterns
- Spec requirement: "System MUST handle network failures during version check gracefully by allowing app to proceed after a timeout"
- Existing EventClient error handling patterns

---

### 6. UIVersionInfo Model Structure

**Question**: What is the structure of the existing UIVersionInfo model in RedMist.TimingCommon package?

**Research Findings**:

**Decision**: Use existing UIVersionInfo model as-is from RedMist.TimingCommon package. No new model creation required.

**Actual Model Structure** (verified from GitHub: bgriggs/redmist-timing-common):
```csharp
[MessagePackObject]
public class UIVersionInfo
{
    // iOS
    public string LatestIOSVersion { get; set; } = string.Empty;
    public string MinimumIOSVersion { get; set; } = string.Empty;
    public bool RecommendIOSUpdate { get; set; }
    public bool IsIOSMinimumMandatory { get; set; }
    
    // Android
    public string LatestAndroidVersion { get; set; } = string.Empty;
    public string MinimumAndroidVersion { get; set; } = string.Empty;
    public bool RecommendAndroidUpdate { get; set; }
    public bool IsAndroidMinimumMandatory { get; set; }
    
    // Web
    public string LatestWebVersion { get; set; } = string.Empty;
    public string MinimumWebVersion { get; set; } = string.Empty;
    public bool RecommendWebUpdate { get; set; }
    public bool IsWebMinimumMandatory { get; set; }
}
```

**Rationale**: Spec explicitly states "This class already exists in the RedMist.TimingCommon NuGet package and MUST be used with its existing properties - no new class should be created."

**Key Insights**:
- Server provides explicit flags for recommendations (`Recommend*Update`)
- Server provides explicit flags for mandatory enforcement (`Is*MinimumMandatory`)
- This simplifies client logic - respect server decisions rather than implementing complex version comparison
- All version strings are non-nullable (default to empty string)
- MessagePack serialization used for efficient data transfer

**Implementation Impact**:
- Version comparison logic should check server flags FIRST
- Only compare versions if flags are set
- Empty version strings should be treated as "no version info available"
- Client doesn't need to decide policy, just enforce server's decisions

**Alternatives Considered**: None - spec mandates using existing model.

**References**:
- Feature spec: "UIVersionInfo (from RedMist.TimingCommon package)"
- GitHub: https://github.com/bgriggs/redmist-timing-common/blob/main/RedMist.TimingCommon/UIVersionInfo.cs

---

## Summary of Decisions

| Area | Decision | Key Technology/Pattern |
|------|----------|----------------------|
| Platform Detection | Conditional compilation symbols | #if ANDROID, IOS, BROWSER |
| Version Retrieval | Assembly.GetExecutingAssembly() | With fallback to attributes |
| UI Blocking (Mandatory) | Modal Dialog | Window.ShowDialog<T>() |
| UI Non-blocking (Optional) | Notification Overlay | Custom dismissible control |
| Version Comparison | System.Version.CompareTo() | Built-in semantic versioning |
| Network Timeout | CancellationToken with 5s timeout | Graceful degradation on failure |
| Data Model | Existing UIVersionInfo | Use as-is from RedMist.TimingCommon |

## Next Steps (Phase 1)

1. Inspect actual UIVersionInfo model structure from RedMist.TimingCommon
2. Create data-model.md with VersionCheckResult and related types
3. Generate contracts/ with version check service interface
4. Create quickstart.md with implementation guide
5. Update agent context with new patterns and decisions
