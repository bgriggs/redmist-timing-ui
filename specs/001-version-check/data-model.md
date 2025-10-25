# Data Model: Application Version Check and Update Notification

**Date**: 2025-10-24  
**Feature**: 001-version-check

## Entity Definitions

### 1. UIVersionInfo (EXISTING - from RedMist.TimingCommon)

**Source**: RedMist.TimingCommon NuGet package  
**Purpose**: Server-provided version information containing minimum required and latest available versions per platform

**Actual Properties** (from GitHub: bgriggs/redmist-timing-common):
```csharp
[MessagePackObject]
public class UIVersionInfo
{
    // iOS properties
    [MaxLength(15)]
    [MessagePack.Key(0)]
    public string LatestIOSVersion { get; set; } = string.Empty;
    
    [MaxLength(15)]
    [MessagePack.Key(1)]
    public string MinimumIOSVersion { get; set; } = string.Empty;
    
    [MessagePack.Key(2)]
    public bool RecommendIOSUpdate { get; set; }
    
    [MessagePack.Key(3)]
    public bool IsIOSMinimumMandatory { get; set; }
    
    // Android properties
    [MaxLength(15)]
    [MessagePack.Key(4)]
    public string LatestAndroidVersion { get; set; } = string.Empty;
    
    [MaxLength(15)]
    [MessagePack.Key(5)]
    public string MinimumAndroidVersion { get; set; } = string.Empty;
    
    [MessagePack.Key(6)]
    public bool RecommendAndroidUpdate { get; set; }
    
    [MessagePack.Key(7)]
    public bool IsAndroidMinimumMandatory { get; set; }
    
    // Web properties
    [MaxLength(15)]
    [MessagePack.Key(8)]
    public string LatestWebVersion { get; set; } = string.Empty;
    
    [MaxLength(15)]
    [MessagePack.Key(9)]
    public string MinimumWebVersion { get; set; } = string.Empty;
    
    [MessagePack.Key(10)]
    public bool RecommendWebUpdate { get; set; }
    
    [MessagePack.Key(11)]
    public bool IsWebMinimumMandatory { get; set; }
}
```

**Key Properties**:
- Version strings use format like "1.2.3" (max 15 characters)
- Boolean flags for recommendations (`RecommendIOSUpdate`, `RecommendAndroidUpdate`, `RecommendWebUpdate`)
- Boolean flags for mandatory enforcement (`IsIOSMinimumMandatory`, `IsAndroidMinimumMandatory`, `IsWebMinimumMandatory`)
- All string properties default to empty string (not nullable)

**Usage**: Retrieved from server via `EventClient.LoadUIVersionInfoAsync()` and used to determine update requirements.

**Important Notes**:
- Server controls recommendation logic via `Recommend*Update` flags
- Server controls mandatory enforcement via `Is*MinimumMandatory` flags
- Client should respect these server-side decisions rather than implementing its own version comparison logic
- This simplifies client implementation and centralizes version policy on the server

**Validation Rules**:
- Version strings should be parseable as `System.Version` (e.g., "1.2.3" or "1.2.3.4")
- Empty version strings should be handled gracefully (assume no update required)
- Boolean flags take precedence over version comparison for determining update requirements

---

### 2. AppPlatform (NEW)

**Purpose**: Enumeration of supported application platforms for version checking

```csharp
public enum AppPlatform
{
    iOS,
    Android,
    Browser,    // Web/WASM
    Desktop     // Excluded from version checking per spec
}
```

**Usage**: Returned by `PlatformDetectionService` to determine which platform-specific version info to use.

**Validation Rules**:
- Desktop platform detected but excluded from version checking logic
- Must map correctly to UIVersionInfo properties (iOS → LatestVersioniOS, etc.)

---

### 3. UpdateRequirement (NEW)

**Purpose**: Enumeration indicating the type of update action required

```csharp
public enum UpdateRequirement
{
    None,           // No update needed
    Optional,       // Update recommended but not required
    Mandatory       // Update required to continue
}
```

**State Transitions** (based on UIVersionInfo flags):
```
Mandatory: IsIOSMinimumMandatory/IsAndroidMinimumMandatory/IsWebMinimumMandatory = true
           AND currentVersion < MinimumIOSVersion/MinimumAndroidVersion/MinimumWebVersion

Optional: RecommendIOSUpdate/RecommendAndroidUpdate/RecommendWebUpdate = true
          OR (currentVersion < LatestIOSVersion/LatestAndroidVersion/LatestWebVersion
              AND not Mandatory)

None: All other cases
```

**Business Rules**:
- `None`: No message shown, app proceeds normally
- `Optional`: Non-blocking notification shown, user can dismiss and continue
- `Mandatory`: Blocking dialog shown, user cannot proceed without updating
- Server-side flags (`Is*MinimumMandatory` and `Recommend*Update`) take precedence over client version comparison

---

### 4. VersionCheckResult (NEW)

**Purpose**: Encapsulates the result of version comparison and appropriate user messaging

```csharp
public class VersionCheckResult
{
    public UpdateRequirement Requirement { get; set; }
    
    public string Message { get; set; } = string.Empty;
    
    public string? ActionUrl { get; set; }
    
    public AppPlatform Platform { get; set; }
    
    public Version CurrentVersion { get; set; } = new Version(1, 0, 0);
    
    public Version? LatestVersion { get; set; }
    
    public Version? MinimumVersion { get; set; }
}
```

**Properties**:
- `Requirement`: The update action required (None/Optional/Mandatory)
- `Message`: User-facing message explaining the update situation
- `ActionUrl`: Platform-specific URL for updating (App Store, Play Store, or null for browser)
- `Platform`: The current platform (for logging/analytics)
- `CurrentVersion`: The running application version
- `LatestVersion`: The latest available version from server (nullable if server doesn't provide)
- `MinimumVersion`: The minimum required version from server (nullable if server doesn't provide)

**Validation Rules**:
- `Message` must be populated when `Requirement` is not `None`
- `ActionUrl` must be populated for iOS and Android when `Requirement` is not `None`
- `ActionUrl` should be null for Browser platform (refresh instructions in message instead)
- `CurrentVersion` must always be set (fallback to 1.0.0 if detection fails)

**Example Instances**:

```csharp
// Mandatory update for iOS
new VersionCheckResult
{
    Requirement = UpdateRequirement.Mandatory,
    Message = "A mandatory update is required to continue using Red Mist Timing. Please update to the latest version from the App Store.",
    ActionUrl = "https://apps.apple.com/app/redmist-timing/id123456789",
    Platform = AppPlatform.iOS,
    CurrentVersion = new Version(1, 5, 0),
    LatestVersion = new Version(2, 0, 0),
    MinimumVersion = new Version(1, 8, 0)
}

// Optional update for Android
new VersionCheckResult
{
    Requirement = UpdateRequirement.Optional,
    Message = "A new version of Red Mist Timing is available. Update from the Play Store for the latest features and improvements.",
    ActionUrl = "https://play.google.com/store/apps/details?id=com.bigmission.redmist",
    Platform = AppPlatform.Android,
    CurrentVersion = new Version(1, 9, 0),
    LatestVersion = new Version(2, 0, 0),
    MinimumVersion = new Version(1, 8, 0)
}

// Browser refresh instruction
new VersionCheckResult
{
    Requirement = UpdateRequirement.Optional,
    Message = "A new version of Red Mist Timing is available. Please refresh your browser (Ctrl+F5 or Cmd+Shift+R) to load the latest version.",
    ActionUrl = null,
    Platform = AppPlatform.Browser,
    CurrentVersion = new Version(1, 9, 0),
    LatestVersion = new Version(2, 0, 0),
    MinimumVersion = new Version(1, 8, 0)
}

// No update needed
new VersionCheckResult
{
    Requirement = UpdateRequirement.None,
    Message = string.Empty,
    ActionUrl = null,
    Platform = AppPlatform.iOS,
    CurrentVersion = new Version(2, 0, 0),
    LatestVersion = new Version(2, 0, 0),
    MinimumVersion = new Version(1, 8, 0)
}
```

---

## Entity Relationships

```
MainViewModel
    ↓ (calls on startup)
PlatformDetectionService.GetCurrentPlatform()
    ↓ (returns)
AppPlatform enum
    ↓ (determines which version to check)
EventClient.LoadUIVersionInfoAsync()
    ↓ (returns)
UIVersionInfo
    ↓ (combined with current version)
VersionCheckService.CheckVersion(currentVersion, uiVersionInfo, platform)
    ↓ (returns)
VersionCheckResult
    ↓ (determines UI action)
MainViewModel displays dialog/notification
```

## Data Flow

1. **Startup**: MainViewModel initializes
2. **Platform Detection**: Determine current platform (iOS/Android/Browser/Desktop)
3. **Skip Desktop**: If Desktop, skip version check entirely
4. **Retrieve Server Versions**: Call `EventClient.LoadUIVersionInfoAsync()` with 5-second timeout
5. **Get Current Version**: Call `Assembly.GetExecutingAssembly().GetName().Version`
6. **Compare Versions**: Create `VersionCheckResult` based on platform-specific version comparison
7. **Display UI**: 
   - If `Mandatory`: Show blocking modal dialog with platform-specific update instructions
   - If `Optional`: Show dismissible notification with platform-specific update instructions
   - If `None`: Proceed silently to events list
8. **Timeout/Error Handling**: If step 4 times out or fails, create `VersionCheckResult` with `Requirement.None` and proceed

## Validation & Error Handling

### Version String Parsing
- **Input**: String version from UIVersionInfo (e.g., "1.2.3")
- **Process**: Parse using `Version.TryParse()`
- **Error Handling**: If parse fails, treat as null (no version info available)

### Null Version Info
- **Scenario**: Server returns null or empty version strings
- **Handling**: Treat as `UpdateRequirement.None` (allow app to proceed)
- **Logging**: Log warning for diagnostic purposes

### Platform Detection Failure
- **Scenario**: Cannot determine platform
- **Handling**: Default to `AppPlatform.Desktop` and skip version check
- **Logging**: Log error for diagnostic purposes

### Network Timeout
- **Scenario**: LoadUIVersionInfoAsync times out after 5 seconds
- **Handling**: Return `VersionCheckResult` with `Requirement.None`
- **Logging**: Log warning
- **User Experience**: App proceeds normally (better than blocking on network issues)

---

## Storage & Persistence

**No persistence required** for this feature. Version check is performed on every app startup using fresh data from server. This ensures:
- Users always get latest version requirements
- No stale cached version data
- Simpler implementation (no cache invalidation logic needed)

**Future Enhancement**: Consider caching `UIVersionInfo` for offline scenarios, but not required for initial implementation.
