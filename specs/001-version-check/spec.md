# Feature Specification: Application Version Check and Update Notification

**Feature Branch**: `001-version-check`  
**Created**: October 24, 2025  
**Status**: Draft  
**Input**: User description: "We are implementing a new feature for this app. The app runs in ios, android, and web browser (WASM). It has a desktop version too, but this is only used in development and can be ignored. This feature needs to get version from the server using EventClient LoadUIVersionInfoAsync when the app is starting in MainViewModel. It then needs to determine where it is running, ios, android or web. It then needs to get the version of the application running using: Assembly.GetExecutingAssembly().GetName().Version. It then needs to use the corresponding platform version information to determine 1) show the user a message that a new version is available and recommend they update, or 2) the version they are using is too old and a mandatory update is required to continue. There should be a link to the app store for ios or android. For web version, tell them to try to refresh their browser to get a new version. When mandatory, do not allow the application to progress past the initial startup. Stop the app before loading the events list."

## Clarifications

### Session 2025-10-24

- Q: After the initial version check at startup, should the app re-check the version periodically during the session, or only check once per app launch? → A: Check once per app launch only (recommended for minimal disruption)
- Q: What level of logging/telemetry should be captured for version check operations to help diagnose issues in production? → A: Log key events only (check started, result, failures)
- Q: Should the version check API call use HTTPS enforcement, and what should happen if the server certificate is invalid or the connection is insecure? → A: No specific requirement (use whatever EventClient default behavior is)
- Q: For the optional update recommendation (User Story 2), should the message be dismissible permanently for that version, or should it reappear every time the app launches until the user updates? → A: Show on each app launch (user must dismiss each time)
- Q: What should be the visual presentation of the update messages (mandatory and optional)? → A: Modal dialog/popup that overlays content (standard pattern)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Mandatory Update Enforcement (Priority: P1)

A user starts the app with a version that is too old to be compatible with current server APIs or data formats. The system must prevent access to any functionality until the user updates to a supported version.

**Why this priority**: This is the highest priority because it protects data integrity and prevents crashes or incorrect behavior from incompatible versions. It ensures all users can successfully use the app and prevents support issues from version mismatches.

**Independent Test**: Can be fully tested by configuring server to return version requirements that mark current app version as below minimum required, starting the app, and verifying that the app blocks all access and displays mandatory update message with appropriate platform-specific update instructions.

**Acceptance Scenarios**:

1. **Given** the app version is below the minimum required version defined by the server, **When** the user starts the app, **Then** the app displays a mandatory update message and prevents access to the events list
2. **Given** a mandatory update is required on iOS, **When** the update message is shown, **Then** the message includes a link to the iOS App Store
3. **Given** a mandatory update is required on Android, **When** the update message is shown, **Then** the message includes a link to the Google Play Store
4. **Given** a mandatory update is required on Web (WASM), **When** the update message is shown, **Then** the message instructs the user to refresh their browser to get the new version
5. **Given** the mandatory update message is displayed, **When** the user attempts to navigate to any app functionality, **Then** the app remains on the update screen and does not load any event data

---

### User Story 2 - Optional Update Recommendation (Priority: P2)

A user starts the app with a version that is functional but not the latest available. The system should inform them a new version is available and encourage updating, but allow them to continue using the current version.

**Why this priority**: This helps keep users on recent versions for best experience and latest features, while not disrupting their immediate use of the app. It reduces technical debt of supporting many old versions.

**Independent Test**: Can be fully tested by configuring server to return a newer available version that is above current app version but current version meets minimum requirements, starting the app, and verifying that a recommendation message is shown but the app allows proceeding to normal functionality.

**Acceptance Scenarios**:

1. **Given** the app version meets minimum requirements but a newer version is available, **When** the user starts the app, **Then** the app displays an update recommendation message
2. **Given** an update recommendation is shown, **When** the user dismisses or ignores the message, **Then** the app proceeds to load the events list normally
3. **Given** an update recommendation is shown on iOS, **When** the message is displayed, **Then** it includes a link to the iOS App Store
4. **Given** an update recommendation is shown on Android, **When** the message is displayed, **Then** it includes a link to the Google Play Store
5. **Given** an update recommendation is shown on Web, **When** the message is displayed, **Then** it instructs the user to refresh their browser

---

### User Story 3 - Current Version Validation (Priority: P3)

A user starts the app with the latest version. The system should verify the version silently and proceed directly to normal app functionality without any update messages.

**Why this priority**: This is the expected normal case for most users and should provide the smoothest experience with no interruptions or delays.

**Independent Test**: Can be fully tested by configuring server to return version requirements that match or are below current app version, starting the app, and verifying that no update messages are shown and the events list loads immediately.

**Acceptance Scenarios**:

1. **Given** the app version matches the latest version from the server, **When** the user starts the app, **Then** no update message is displayed and the events list loads normally
2. **Given** the app version is newer than the server's latest version, **When** the user starts the app, **Then** no update message is displayed and the events list loads normally
3. **Given** version check is performed at startup, **When** version validation succeeds, **Then** the version check completes in under 2 seconds and does not noticeably delay app startup

---

### Edge Cases

- What happens when the server is unreachable and version information cannot be retrieved? (System should allow app to proceed with a timeout after 5 seconds to prevent blocking users when offline or server is down)
- How does the system handle malformed version data from the server? (System should log the error and allow app to proceed rather than blocking usage)
- What happens if the app cannot determine its own version number? (System should log the error and allow app to proceed rather than blocking usage)
- How does the system handle platform detection failure? (System should fall back to generic update instructions without platform-specific links)
- What happens when the user is on a beta or development version with a higher version number than production? (System should not show update messages for development versions)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST retrieve version information from the server using the EventClient's LoadUIVersionInfoAsync method during app startup in MainViewModel, relying on EventClient's default security and transport configuration
- **FR-002**: System MUST detect the current platform (iOS, Android, or Web/WASM) at runtime
- **FR-003**: System MUST determine the application's current version using Assembly.GetExecutingAssembly().GetName().Version
- **FR-004**: System MUST compare the current app version against the minimum required version for the detected platform
- **FR-005**: System MUST compare the current app version against the latest available version for the detected platform
- **FR-006**: System MUST block access to all app functionality (events list and beyond) when a mandatory update is required
- **FR-007**: System MUST display a mandatory update message when the current version is below the minimum required version
- **FR-008**: System MUST display an optional update recommendation when a newer version is available but current version meets minimum requirements
- **FR-009**: System MUST include a link to the iOS App Store in update messages on iOS platform
- **FR-010**: System MUST include a link to the Google Play Store in update messages on Android platform
- **FR-011**: System MUST instruct users to refresh their browser in update messages on Web/WASM platform
- **FR-012**: System MUST allow users to dismiss optional update recommendations and continue using the app
- **FR-013**: System MUST NOT display any update messages when the current version meets or exceeds the latest version
- **FR-014**: System MUST perform version check before loading the events list
- **FR-015**: System MUST handle network failures during version check gracefully by allowing app to proceed after a timeout
- **FR-016**: Desktop platform MUST be excluded from version checking (development only)
- **FR-017**: System MUST perform version check only once per app launch and NOT re-check during the session
- **FR-018**: System MUST log version check operations including: check started, comparison result (mandatory/optional/current), and any failures or errors encountered
- **FR-019**: Optional update recommendation messages MUST be shown on each app launch when a newer version is available (no persistent dismissal or suppression)
- **FR-020**: Update messages (both mandatory and optional) MUST be displayed as modal dialogs that overlay content and require user acknowledgment

### Cross-Platform Requirements *(mandatory for all features)*

- **CP-001**: Feature MUST work identically on iOS, Android, and Web (WASM) platforms (Desktop excluded)
- **CP-002**: Version check MUST complete within 5 seconds on all platforms to avoid blocking startup
- **CP-003**: Feature MUST follow MVVM pattern with shared ViewModels for version checking logic
- **CP-004**: Platform-specific UI code (update messages and links) MUST only exist in Views layer
- **CP-005**: All new dependencies MUST use MIT, Apache 2.0, BSD, or LGPL licenses
- **CP-006**: All UI elements MUST support both light and dark themes
- **CP-007**: All colors MUST be defined in App.axaml using ResourceDictionary.ThemeDictionaries
- **CP-008**: All UI components MUST use DynamicResource bindings for theme colors (no hard-coded colors)
- **CP-009**: Modal dialogs for update messages MUST be accessible to screen readers and follow platform accessibility guidelines

### Key Entities *(include if feature involves data)*

- **UIVersionInfo** (from RedMist.TimingCommon package): Existing server-provided version information model that contains minimum required versions and latest available versions per platform (iOS, Android, Web). This class already exists in the RedMist.TimingCommon NuGet package and MUST be used with its existing properties - no new class should be created.
- **VersionCheckResult**: Result of version comparison indicating whether update is mandatory, optional, or not needed, along with appropriate message and action URL for the platform

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users with outdated versions are blocked from accessing app functionality 100% of the time when mandatory update is required
- **SC-002**: Users with current versions can access the app without update prompts 100% of the time
- **SC-003**: Version check completes within 2 seconds under normal network conditions
- **SC-004**: Users can proceed to use the app within 5 seconds even when version check times out due to network issues
- **SC-005**: Update messages display correct platform-specific instructions and links 100% of the time
- **SC-006**: Support tickets related to version incompatibility reduce by 90% after feature implementation

### Platform Success Criteria *(mandatory)*

- **PC-001**: Feature performs identically across iOS, Android, and Web platforms
- **PC-002**: Version check completes in under 2 seconds on each platform under normal conditions
- **PC-003**: App startup time remains under 3 seconds on target devices for each platform including version check
- **PC-004**: Memory usage for version checking is negligible (<1MB) on each platform
- **PC-005**: All unit tests pass for shared ViewModels and version checking services
- **PC-006**: Integration tests pass for each platform-specific implementation (iOS App Store links, Android Play Store links, Web browser refresh instructions)
