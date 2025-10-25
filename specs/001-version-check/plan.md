# Implementation Plan: Application Version Check and Update Notification

**Branch**: `001-version-check` | **Date**: 2025-10-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-version-check/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

This feature implements a version check system that validates the app version on startup, comparing it against server-provided minimum and latest versions. When the app version is below minimum requirements, users are blocked from accessing functionality and shown a mandatory update dialog with platform-specific app store links. When a newer version is available but not mandatory, users receive an optional update recommendation. The version check uses the existing EventClient.LoadUIVersionInfoAsync API, performs platform detection, and displays modal dialogs with appropriate instructions for iOS (App Store), Android (Play Store), or Web (browser refresh). The feature ensures all users run compatible versions while providing a smooth experience for users on current versions.

## Technical Context

**Language/Version**: C# (.NET 8+ with Avalonia UI)  
**Primary Dependencies**: Avalonia UI (MIT licensed), RedMist.TimingCommon NuGet package (contains UIVersionInfo model)  
**Storage**: N/A (version check result stored transiently in ViewModel only)  
**Testing**: MSTest (already used in RedMist.Timing.UI.Tests project)  
**Target Platforms**: iOS 15+, Android API 21+, Web (WASM) (Desktop excluded per spec)
**Project Type**: multi-platform (MVVM architecture required)  
**Performance Goals**: Version check <2s under normal conditions, <5s timeout for network failures, startup time remains <3s  
**Constraints**: Open source licensing only, MVVM pattern mandatory, cross-platform feature parity (iOS/Android/Web), light/dark theme support, Desktop platform excluded  
**Scale/Scope**: Consumer racing timing application, single-user client-side validation

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **MVVM Architecture**: ViewModel (MainViewModel) handles version check logic, View displays modal dialogs, Model (UIVersionInfo) is data-only from RedMist.TimingCommon package
- [x] **Cross-Platform Support**: Feature works on iOS, Android, Web (WASM) - Desktop explicitly excluded per spec
- [x] **Open Source Licensing**: Avalonia UI (MIT), RedMist.TimingCommon (internal package), no new external dependencies required
- [x] **Comprehensive Testing**: Plan includes unit tests for MainViewModel version check logic and platform-specific integration tests (added post-implementation per constitution)
- [x] **Performance Standards**: Version check <2s normal, <5s timeout, startup remains <3s per spec requirements
- [x] **Theme Support**: Modal update dialogs will use DynamicResource bindings for colors defined in App.axaml, support light/dark themes

**Additional Gates**:
- [x] **Platform Detection**: Must correctly detect iOS, Android, or Web at runtime using Avalonia platform APIs
- [x] **Network Resilience**: Must handle timeout (5s) and continue app startup on failure to prevent blocking users
- [x] **Logging**: Must log version check events (start, result, failures) for production diagnostics

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
RedMist.Timing.UI/                    # Main shared project (Avalonia UI)
├── ViewModels/
│   └── MainViewModel.cs              # MODIFY: Add version check logic on startup
├── Services/
│   └── VersionCheckService.cs        # NEW: Service for version comparison logic
├── Models/
│   └── VersionCheckResult.cs         # NEW: Result model (mandatory/optional/current)
├── Views/
│   └── UpdateDialogView.axaml        # NEW: Modal dialog for update messages
│   └── UpdateDialogView.axaml.cs
├── Clients/
│   └── EventClient.cs                # EXISTING: Contains LoadUIVersionInfoAsync
├── App.axaml                          # MODIFY: Add update dialog color resources
└── RedMist.Timing.UI.csproj

RedMist.Timing.UI.iOS/                # iOS-specific project
└── (Platform-specific configuration only, no code changes needed)

RedMist.Timing.UI.Android/            # Android-specific project
└── (Platform-specific configuration only, no code changes needed)

RedMist.Timing.UI.Browser/            # Web/WASM project
└── (Platform-specific configuration only, no code changes needed)

RedMist.Timing.UI.Desktop/            # Desktop project (excluded from version check)
└── (No changes - version check disabled for Desktop)

RedMist.TimingCommon/                 # Existing shared library (NuGet package)
└── Models/
    └── UIVersionInfo.cs              # EXISTING: Server version info model (DO NOT MODIFY)

RedMist.Timing.UI.Tests/              # Test project
├── ViewModels/
│   └── MainViewModelTests.cs         # MODIFY: Add version check tests
└── Services/
    └── VersionCheckServiceTests.cs   # NEW: Unit tests for version check service
```

**Structure Decision**: Using existing single shared project structure (RedMist.Timing.UI) with platform-specific build projects. All business logic (version check service, ViewModel updates) goes in the shared project following MVVM. Platform-specific projects only contain configuration and no code changes are needed since Avalonia handles platform abstraction. UIVersionInfo model already exists in RedMist.TimingCommon package and will be used as-is.

## Phase 0: Research

All technical decisions resolved and documented in [research.md](./research.md):

### Key Decisions Made
1. **Platform Detection**: Use conditional compilation symbols (#if ANDROID, IOS, BROWSER) for reliable compile-time platform detection
2. **Version Retrieval**: Use `Assembly.GetExecutingAssembly().GetName().Version` with attribute fallback
3. **Modal Dialogs**: Use Avalonia's `Window.ShowDialog()` for update messages (both mandatory and optional per clarification)
4. **Version Comparison**: Use `System.Version.CompareTo()` for semantic version comparison
5. **Network Timeout**: Implement 5-second timeout with CancellationToken, graceful degradation on failure
6. **Logging**: Use existing Microsoft.Extensions.Logging (ILogger) for diagnostics
7. **UIVersionInfo Model**: Use existing model from RedMist.TimingCommon package as-is (DO NOT MODIFY)

### Dependencies Verified
- ✅ Avalonia UI (MIT License) - already in project
- ✅ RedMist.TimingCommon - internal package
- ✅ Microsoft.Extensions.Logging (MIT) - already in project
- ✅ No new external dependencies required

**Output**: [research.md](./research.md) - Complete technical research documentation

---

## Phase 1: Design & Contracts

### Data Model
Documented in [data-model.md](./data-model.md):

**New Models Created**:
- `VersionCheckResult` - Result of version comparison with user messaging and action URLs
- `UpdateRequirement` (enum) - None, Optional, or Mandatory
- `AppPlatform` (enum) - iOS, Android, Web, Desktop

**Existing Models Reused**:
- `UIVersionInfo` - From RedMist.TimingCommon package (contains server version requirements)

### Service Contracts
Documented in [contracts/](./contracts/):

**IVersionCheckService Interface**:
- Defines version checking logic abstraction
- Methods for platform detection, version comparison, and result generation
- Used by MainViewModel for MVVM compliance

**IPlatformDetectionService Interface**:
- Abstracts platform detection logic
- Enables platform-specific testing
- Returns AppPlatform enum

### Implementation Guide
Documented in [quickstart.md](./quickstart.md):

Step-by-step implementation guide covering:
1. Service implementation (VersionCheckService, PlatformDetectionService)
2. ViewModel integration (MainViewModel startup logic)
3. View creation (UpdateDialogView with AXAML)
4. Theme resources (App.axaml color additions)
5. Testing strategy (unit and integration tests)
6. Platform-specific considerations

### Agent Context Updated
✅ GitHub Copilot context updated with:
- C# (.NET 8+ with Avalonia UI)
- Avalonia UI (MIT licensed)
- RedMist.TimingCommon NuGet package
- MVVM multi-platform project type
- Transient storage pattern

---

## Phase 2: Constitution Re-Check

*Re-evaluating gates after design completion:*

- [x] **MVVM Architecture**: ✅ Design confirms separation - VersionCheckService (business logic), MainViewModel (presentation logic), UpdateDialogView (declarative UI), VersionCheckResult (data model)
- [x] **Cross-Platform Support**: ✅ Platform detection implemented, feature works on iOS/Android/Web, Desktop excluded as specified
- [x] **Open Source Licensing**: ✅ No new dependencies, all existing dependencies MIT licensed
- [x] **Comprehensive Testing**: ✅ Testing strategy defined in quickstart.md (unit tests for services/ViewModels, integration tests per platform)
- [x] **Performance Standards**: ✅ Version check <2s normal operation, <5s timeout, minimal memory footprint (<1KB)
- [x] **Theme Support**: ✅ UpdateDialogView will use DynamicResource bindings, colors in App.axaml ThemeDictionaries

**All gates PASS** - Ready for task decomposition (Phase 3)

---

## Next Steps

Run `/speckit.tasks` to generate the task breakdown in [tasks.md](./tasks.md).

The implementation plan is complete with:
- ✅ Technical context defined
- ✅ Constitution compliance verified
- ✅ Research completed (research.md)
- ✅ Data model designed (data-model.md)
- ✅ Service contracts defined (contracts/)
- ✅ Implementation guide created (quickstart.md)
- ✅ Agent context updated (.github/copilot-instructions.md)

**Branch**: `001-version-check`  
**Plan Location**: `C:\Code\redmist-timing-ui\specs\001-version-check\plan.md`  
**Artifacts Generated**:
- `research.md` - Technical decisions and patterns
- `data-model.md` - Data structures and validation rules
- `contracts/` - Service interface definitions
- `quickstart.md` - Implementation walkthrough

