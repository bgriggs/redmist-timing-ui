# Quickstart Guide: Implementing Application Version Check

**Date**: 2025-10-24  
**Feature**: 001-version-check  
**Target Audience**: Developers implementing this feature

## Overview

This guide walks through implementing the version check feature for Red Mist Timing UI, which displays update notifications (mandatory or optional) based on version comparison with server requirements.

---

## Prerequisites

- .NET 9.0 SDK
- Avalonia UI knowledge
- Familiarity with MVVM pattern
- Understanding of RedMist.Timing.UI project structure

---

## Implementation Steps

### Step 1: Create Data Models

**File**: `RedMist.Timing.UI/Models/VersionCheckResult.cs`

```csharp
namespace RedMist.Timing.UI.Models;

public enum AppPlatform
{
    iOS,
    Android,
    Browser,
    Desktop
}

public enum UpdateRequirement
{
    None,
    Optional,
    Mandatory
}

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

---

### Step 2: Create Platform Detection Service

**File**: `RedMist.Timing.UI/Services/PlatformDetectionService.cs`

```csharp
namespace RedMist.Timing.UI.Services;

public interface IPlatformDetectionService
{
    AppPlatform GetCurrentPlatform();
    bool ShouldCheckVersion();
}

public class PlatformDetectionService : IPlatformDetectionService
{
    public AppPlatform GetCurrentPlatform()
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
    
    public bool ShouldCheckVersion()
    {
        // Desktop is development-only, exclude from version checking
        return GetCurrentPlatform() != AppPlatform.Desktop;
    }
}
```

**Register in DI** (`App.axaml.cs` or startup):
```csharp
services.AddSingleton<IPlatformDetectionService, PlatformDetectionService>();
```

---

### Step 3: Create Update Message Service

**File**: `RedMist.Timing.UI/Services/UpdateMessageService.cs`

```csharp
using Microsoft.Extensions.Configuration;

namespace RedMist.Timing.UI.Services;

public interface IUpdateMessageService
{
    string GetUpdateMessage(UpdateRequirement requirement, AppPlatform platform);
    string? GetActionUrl(AppPlatform platform);
}

public class UpdateMessageService : IUpdateMessageService
{
    private readonly IConfiguration configuration;
    
    public UpdateMessageService(IConfiguration configuration)
    {
        this.configuration = configuration;
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
            AppPlatform.iOS => configuration["VersionCheck:iOSAppStoreUrl"],
            AppPlatform.Android => configuration["VersionCheck:AndroidPlayStoreUrl"],
            AppPlatform.Browser => null, // No URL for browser, just refresh instructions
            _ => null
        };
    }
}
```

**Register in DI**:
```csharp
services.AddSingleton<IUpdateMessageService, UpdateMessageService>();
```

**Add to `appsettings.json`**:
```json
{
  "VersionCheck": {
    "iOSAppStoreUrl": "https://apps.apple.com/app/redmist-timing/id123456789",
    "AndroidPlayStoreUrl": "https://play.google.com/store/apps/details?id=com.bigmission.redmist",
    "TimeoutSeconds": 5
  }
}
```

---

### Step 4: Create Version Check Service

**File**: `RedMist.Timing.UI/Services/VersionCheckService.cs`

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.TimingCommon.Models;
using System.Reflection;

namespace RedMist.Timing.UI.Services;

public interface IVersionCheckService
{
    VersionCheckResult CheckVersion(Version currentVersion, UIVersionInfo versionInfo, AppPlatform platform);
    Task<UIVersionInfo?> GetVersionInfoAsync(int timeoutSeconds = 5);
    Version GetCurrentApplicationVersion();
}

public class VersionCheckService : IVersionCheckService
{
    private readonly EventClient eventClient;
    private readonly IUpdateMessageService messageService;
    private readonly ILogger<VersionCheckService> logger;
    
    public VersionCheckService(
        EventClient eventClient,
        IUpdateMessageService messageService,
        ILogger<VersionCheckService> logger)
    {
        this.eventClient = eventClient;
        this.messageService = messageService;
        this.logger = logger;
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
                
                logger.LogWarning("Could not determine application version, using fallback 1.0.0");
                return new Version(1, 0, 0);
            }
            
            return version;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting application version, using fallback 1.0.0");
            return new Version(1, 0, 0);
        }
    }
    
    public async Task<UIVersionInfo?> GetVersionInfoAsync(int timeoutSeconds = 5)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        
        try
        {
            // Note: EventClient.LoadUIVersionInfoAsync needs to accept CancellationToken
            // For now, using Task.Delay as timeout mechanism
            var versionTask = eventClient.LoadUIVersionInfoAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cts.Token);
            
            var completedTask = await Task.WhenAny(versionTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                logger.LogWarning("Version check timed out after {TimeoutSeconds} seconds", timeoutSeconds);
                return null;
            }
            
            return await versionTask;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve version information from server");
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
            logger.LogWarning("Could not parse minimum version '{Version}' for platform {Platform}", minimumVersionStr, platform);
        }
        
        if (!string.IsNullOrEmpty(latestVersionStr) && !Version.TryParse(latestVersionStr, out latestVersion))
        {
            logger.LogWarning("Could not parse latest version '{Version}' for platform {Platform}", latestVersionStr, platform);
        }
        
        // Determine update requirement using server flags
        var requirement = DetermineUpdateRequirement(
            currentVersion, 
            minimumVersion, 
            latestVersion,
            isMinimumMandatory,
            recommendUpdate);
        
        // Build result
        var result = new VersionCheckResult
        {
            Requirement = requirement,
            Platform = platform,
            CurrentVersion = currentVersion,
            MinimumVersion = minimumVersion,
            LatestVersion = latestVersion,
            Message = messageService.GetUpdateMessage(requirement, platform),
            ActionUrl = requirement != UpdateRequirement.None ? messageService.GetActionUrl(platform) : null
        };
        
        return result;
    }
    
    private static UpdateRequirement DetermineUpdateRequirement(
        Version currentVersion,
        Version? minimumRequired,
        Version? latestAvailable,
        bool isMinimumMandatory,
        bool recommendUpdate)
    {
        // Check mandatory update using server flag and version comparison
        if (isMinimumMandatory && minimumRequired != null && currentVersion.CompareTo(minimumRequired) < 0)
        {
            return UpdateRequirement.Mandatory;
        }
        
        // Check optional update using server flag or version comparison
        if (recommendUpdate || (latestAvailable != null && currentVersion.CompareTo(latestAvailable) < 0))
        {
            return UpdateRequirement.Optional;
        }
        
        // Up-to-date
        return UpdateRequirement.None;
    }
}
```

**Register in DI**:
```csharp
services.AddSingleton<IVersionCheckService, VersionCheckService>();
```

---

### Step 5: Update MainViewModel

**File**: `RedMist.Timing.UI/ViewModels/MainViewModel.cs`

Add version checking to initialization:

```csharp
public class MainViewModel : ViewModelBase
{
    private readonly IVersionCheckService versionCheckService;
    private readonly IPlatformDetectionService platformDetectionService;
    
    // Constructor - add new dependencies
    public MainViewModel(
        // ... existing dependencies
        IVersionCheckService versionCheckService,
        IPlatformDetectionService platformDetectionService)
    {
        // ... existing initialization
        this.versionCheckService = versionCheckService;
        this.platformDetectionService = platformDetectionService;
    }
    
    // Add property for version check result
    private VersionCheckResult? _versionCheckResult;
    public VersionCheckResult? VersionCheckResult
    {
        get => _versionCheckResult;
        set => this.RaiseAndSetIfChanged(ref _versionCheckResult, value);
    }
    
    // Modify initialization method
    public async Task InitializeAsync()
    {
        // Check if version checking should be performed
        if (!platformDetectionService.ShouldCheckVersion())
        {
            // Desktop platform - skip version check
            await LoadEventsAsync();
            return;
        }
        
        // Perform version check
        var checkResult = await PerformVersionCheckAsync();
        
        // Handle based on requirement
        if (checkResult?.Requirement == UpdateRequirement.Mandatory)
        {
            // Set result to trigger UI display
            VersionCheckResult = checkResult;
            // DO NOT load events - user must update
            return;
        }
        
        if (checkResult?.Requirement == UpdateRequirement.Optional)
        {
            // Set result to trigger UI notification
            VersionCheckResult = checkResult;
        }
        
        // Proceed to load events
        await LoadEventsAsync();
    }
    
    private async Task<VersionCheckResult?> PerformVersionCheckAsync()
    {
        try
        {
            var platform = platformDetectionService.GetCurrentPlatform();
            var currentVersion = versionCheckService.GetCurrentApplicationVersion();
            
            // Get version info from server with timeout
            var versionInfo = await versionCheckService.GetVersionInfoAsync(timeoutSeconds: 5);
            
            // If timeout or error, allow app to proceed
            if (versionInfo == null)
            {
                return null;
            }
            
            // Check version and get result
            return versionCheckService.CheckVersion(currentVersion, versionInfo, platform);
        }
        catch (Exception ex)
        {
            // Log error and allow app to proceed
            // (logger injected in constructor)
            Logger.LogError(ex, "Error performing version check");
            return null;
        }
    }
}
```

---

### Step 6: Create Update Dialog Views

**Mandatory Update Dialog**

**File**: `RedMist.Timing.UI/Views/UpdateRequiredDialog.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="RedMist.Timing.UI.Views.UpdateRequiredDialog"
        Title="Update Required"
        Width="400" Height="250"
        WindowStartupLocation="CenterScreen"
        CanResize="false">
    
    <StackPanel Margin="20" Spacing="15">
        <TextBlock Text="Update Required" 
                   FontSize="20" 
                   FontWeight="Bold"/>
        
        <TextBlock Text="{Binding Message}" 
                   TextWrapping="Wrap"
                   FontSize="14"/>
        
        <Button Content="Update Now" 
                Command="{Binding OpenUpdateUrlCommand}"
                IsVisible="{Binding HasActionUrl}"
                HorizontalAlignment="Center"
                Padding="30,10"/>
        
        <TextBlock Text="Please update to continue using the app."
                   FontSize="12"
                   Foreground="Gray"
                   TextAlignment="Center"/>
    </StackPanel>
</Window>
```

**File**: `RedMist.Timing.UI/Views/UpdateRequiredDialog.axaml.cs`

```csharp
using Avalonia.Controls;
using RedMist.Timing.UI.Models;
using System.Diagnostics;

namespace RedMist.Timing.UI.Views;

public partial class UpdateRequiredDialog : Window
{
    public VersionCheckResult? Result { get; set; }
    
    public UpdateRequiredDialog()
    {
        InitializeComponent();
    }
    
    private void OpenUpdateUrl()
    {
        if (!string.IsNullOrEmpty(Result?.ActionUrl))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Result.ActionUrl,
                UseShellExecute = true
            });
        }
    }
}
```

**Optional Update Notification**

Add to MainView or create a notification banner component.

---

### Step 7: Display Update UI from MainViewModel

Add to MainViewModel:

```csharp
private async Task ShowMandatoryUpdateDialogAsync(VersionCheckResult result)
{
    var dialog = new UpdateRequiredDialog
    {
        DataContext = result
    };
    
    await dialog.ShowDialog(GetMainWindow());
}

private void ShowOptionalUpdateNotification(VersionCheckResult result)
{
    // Show dismissible notification banner
    // Implementation depends on your UI framework
    VersionCheckResult = result; // Bind to UI element
}
```

---

## Testing

### Unit Tests

**File**: `RedMist.Timing.UI.Tests/Services/VersionCheckServiceTests.cs`

```csharp
[TestClass]
public class VersionCheckServiceTests
{
    [TestMethod]
    public void CheckVersion_BelowMinimum_ReturnsMandatory()
    {
        // Arrange
        var currentVersion = new Version(1, 0, 0);
        var versionInfo = new UIVersionInfo
        {
            MinimumVersioniOS = "1.5.0",
            LatestVersioniOS = "2.0.0"
        };
        
        var service = CreateService();
        
        // Act
        var result = service.CheckVersion(currentVersion, versionInfo, AppPlatform.iOS);
        
        // Assert
        Assert.AreEqual(UpdateRequirement.Mandatory, result.Requirement);
    }
    
    // Add more tests...
}
```

---

## Configuration

Update `appsettings.json` with actual App Store URLs before release.

---

## Deployment Checklist

- [ ] Update App Store URLs in configuration
- [ ] Test on all platforms (iOS, Android, Browser)
- [ ] Verify timeout handling
- [ ] Test with server unavailable
- [ ] Verify mandatory update blocks app
- [ ] Verify optional update allows dismissal
- [ ] Test version parsing edge cases

---

## Troubleshooting

**Issue**: Version check times out  
**Solution**: Check EventClient configuration and network connectivity

**Issue**: Platform detection returns wrong platform  
**Solution**: Verify conditional compilation symbols in project files

**Issue**: Update dialog doesn't block app  
**Solution**: Ensure LoadEventsAsync is not called when Mandatory update detected
