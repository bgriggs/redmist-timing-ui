# Tasks: Application Version Check and Update Notification

**Input**: Design documents from `/specs/001-version-check/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/service-contracts.md, quickstart.md

**Tests**: No explicit test requirements specified in feature spec - tests not included per speckit rules

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Multi-platform Avalonia project**: `RedMist.Timing.UI/` (shared), `RedMist.Timing.UI.iOS/`, `RedMist.Timing.UI.Android/`, `RedMist.Timing.UI.Browser/`, `RedMist.Timing.UI.Desktop/`
- **MVVM Structure**: `Models/`, `ViewModels/`, `Views/`, `Services/`, `Clients/`
- All paths are absolute from repository root

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and configuration for version checking feature

- [X] T001 Verify RedMist.TimingCommon NuGet package contains UIVersionInfo model
- [X] T002 Add version check configuration to RedMist.Timing.UI/appsettings.json (App Store URLs, timeout settings)
- [X] T003 Verify conditional compilation symbols (ANDROID, IOS, BROWSER) in platform-specific projects

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core services and models that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 [P] Create UpdateRequirement enum in RedMist.Timing.UI/Models/VersionCheckResult.cs
- [X] T005 Create VersionCheckResult model in RedMist.Timing.UI/Models/VersionCheckResult.cs (depends on T004)
- [X] T006 [P] Create IPlatformDetectionService interface in RedMist.Timing.UI/Services/PlatformDetectionService.cs
- [X] T007 Implement PlatformDetectionService using System.OperatingSystem methods (IsIOS, IsAndroid, IsBrowser) and conditional compilation symbols in RedMist.Timing.UI/Services/PlatformDetectionService.cs (depends on T006)
- [X] T008 [P] Create IUpdateMessageService interface in RedMist.Timing.UI/Services/UpdateMessageService.cs
- [X] T009 Implement UpdateMessageService with platform-specific messages in RedMist.Timing.UI/Services/UpdateMessageService.cs (depends on T008)
- [X] T010 [P] Create IVersionCheckService interface in RedMist.Timing.UI/Services/VersionCheckService.cs
- [X] T011 Implement VersionCheckService with version comparison logic in RedMist.Timing.UI/Services/VersionCheckService.cs (depends on T010)
- [X] T012 Register all services (IPlatformDetectionService, IUpdateMessageService, IVersionCheckService) in dependency injection container in RedMist.Timing.UI/App.axaml.cs
- [X] T013 Verify all dependencies use permitted licenses (Avalonia MIT, RedMist.TimingCommon existing package)

**Constitutional Requirements**:
- All services MUST be in `RedMist.Timing.UI/Services/` (platform-agnostic)
- All business logic MUST be in services (testable without UI)
- Platform-specific code ONLY in platform Views (iOS, Android, Browser)
- License verification REQUIRED before adding dependencies

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Mandatory Update Enforcement (Priority: P1) 🎯 MVP

**Goal**: Prevent users with incompatible versions from accessing app functionality, display mandatory update message with platform-specific update instructions

**Independent Test**: Configure server to return version requirements that mark current app version as below minimum required, start the app, verify app blocks all access and displays mandatory update message with appropriate platform-specific update instructions (App Store link for iOS, Play Store link for Android, refresh instructions for Web)

### Implementation for User Story 1

- [X] T015 [US1] Integrate version check into MainViewModel.InitializeAsync() before loading events list in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T016 [US1] Add early exit logic in MainViewModel when UpdateRequirement.None or platform is Desktop in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T017 [US1] Add call to GetVersionInfoAsync with 5-second timeout in MainViewModel.InitializeAsync() in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T018 [US1] Add graceful degradation logic when GetVersionInfoAsync returns null (timeout/error) in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T019 [US1] Add version comparison call using CheckVersion() in MainViewModel in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T020 [US1] Add conditional blocking logic for UpdateRequirement.Mandatory in MainViewModel to prevent LoadEventsListAsync() in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T021 [US1] Create UpdateRequiredDialog XAML view for mandatory updates in RedMist.Timing.UI/Views/MainView.axaml (overlay approach)
- [X] T022 [US1] Create UpdateRequiredDialog code-behind with message and action URL properties in RedMist.Timing.UI/ViewModels/MainViewModel.cs (ViewModel properties)
- [X] T023 [US1] Add ShowMandatoryUpdateDialog method in MainViewModel using overlay bindings in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T024 [P] [US1] Implement platform-specific URL handling for iOS App Store links via LauncherEvent in RedMist.Timing.UI/Views/MainView.axaml.cs
- [X] T025 [P] [US1] Implement platform-specific URL handling for Android Play Store links via LauncherEvent in RedMist.Timing.UI/Views/MainView.axaml.cs
- [X] T026 [P] [US1] Implement browser refresh instructions display for Web/WASM via LauncherEvent in RedMist.Timing.UI/Views/MainView.axaml.cs
- [X] T027 [US1] Add error logging for version check failures in MainViewModel in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T028 [US1] Add validation that mandatory dialog cannot be dismissed until user takes action in RedMist.Timing.UI/Views/MainView.axaml

**Checkpoint**: At this point, User Story 1 should be fully functional - app blocks access for outdated versions and displays mandatory update message with platform-specific instructions

---

## Phase 4: User Story 2 - Optional Update Recommendation (Priority: P2)

**Goal**: Inform users about available updates without disrupting app usage, display dismissible recommendation message with platform-specific update instructions

**Independent Test**: Configure server to return a newer available version that is above current app version but current version meets minimum requirements, start the app, verify that a recommendation message is shown but the app allows proceeding to normal functionality (events list loads)

### Implementation for User Story 2

- [X] T029 [US2] Create UpdateNotification XAML view for optional updates in RedMist.Timing.UI/Views/UpdateNotification.axaml
- [X] T030 [US2] Create UpdateNotification code-behind with dismissible property in RedMist.Timing.UI/Views/UpdateNotification.axaml.cs
- [X] T031 [US2] Add ShowOptionalUpdateNotification method in MainViewModel using non-modal notification overlay in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T032 [US2] Add conditional logic in MainViewModel for UpdateRequirement.Optional to show notification in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T033 [US2] Ensure LoadEventsListAsync() proceeds after showing optional notification in MainViewModel in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T034 [US2] Add dismiss button handler in UpdateNotification view in RedMist.Timing.UI/Views/UpdateNotification.axaml
- [X] T035 [P] [US2] Style optional notification differently from mandatory dialog (visual distinction) in RedMist.Timing.UI/Views/UpdateNotification.axaml
- [X] T036 [US2] Add validation that optional notification does not block events list loading in RedMist.Timing.UI/ViewModels/MainViewModel.cs

**Checkpoint**: At this point, User Stories 1 AND 2 should both work - mandatory updates block access, optional updates show dismissible notification but allow app usage

---

## Phase 5: User Story 3 - Current Version Validation (Priority: P3)

**Goal**: Silently validate version for users on latest version and proceed directly to app functionality without any messages or delays

**Independent Test**: Configure server to return version requirements that match or are below current app version, start the app, verify no update messages are shown and events list loads immediately (within 2 seconds)

### Implementation for User Story 3

- [X] T037 [US3] Add performance measurement for version check duration in MainViewModel in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T038 [US3] Verify UpdateRequirement.None path in MainViewModel proceeds directly to LoadEventsListAsync() in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T039 [US3] Add validation that no UI elements are shown when UpdateRequirement.None in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T040 [US3] Add logic to handle current version newer than server's latest version (development/beta versions) in RedMist.Timing.UI/Services/VersionCheckService.cs
- [ ] T041 [US3] Ensure version check completes in under 2 seconds for UpdateRequirement.None path in RedMist.Timing.UI/ViewModels/MainViewModel.cs
- [X] T042 [US3] Add telemetry/logging for successful version checks (optional) in RedMist.Timing.UI/ViewModels/MainViewModel.cs

**Checkpoint**: All user stories should now be independently functional - mandatory updates block, optional updates notify but allow usage, current versions proceed silently

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories and final validation

- [X] T043 [P] Add comprehensive error handling for malformed version data from server in RedMist.Timing.UI/Services/VersionCheckService.cs
- [X] T044 [P] Add comprehensive error handling for Assembly.GetExecutingAssembly() failures in RedMist.Timing.UI/Services/VersionCheckService.cs
- [X] T045 [P] Add comprehensive error handling for platform detection failures in RedMist.Timing.UI/Services/PlatformDetectionService.cs
- [X] T046 Code cleanup and refactoring of version check services for maintainability
- [X] T047 Add XML documentation comments to all public interfaces and classes in Services/
- [X] T048 [P] Update .github/copilot-instructions.md with version check feature context (already done per plan.md)
- [X] T049 Verify Desktop platform is excluded from version checking via ShouldCheckVersion() method
- [ ] T050 Performance testing: Verify version check completes within 2 seconds under normal conditions
- [ ] T051 Performance testing: Verify 5-second timeout works correctly for network failures
- [ ] T052 Run through quickstart.md validation scenarios for all three user stories
- [X] T053 Security review: Validate App Store and Play Store URLs use HTTPS
- [X] T054 Accessibility review: Ensure update dialogs and notifications are screen-reader friendly

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3, 4, 5)**: All depend on Foundational phase completion
  - User Story 1 (US1 - Mandatory): Can start after Foundational - No dependencies on other stories
  - User Story 2 (US2 - Optional): Can start after Foundational - May share views with US1 but independently testable
  - User Story 3 (US3 - Current): Can start after Foundational - Validates the "no update needed" path
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - Builds on US1's MainViewModel integration but uses different UI
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - Validates silent path when no update needed

### Within Each User Story

- Models and services before ViewModels
- ViewModels before Views
- Core implementation before platform-specific code
- Integration before validation
- Story complete before moving to next priority

### Parallel Opportunities

- **Phase 1 (Setup)**: All tasks can run in parallel
- **Phase 2 (Foundational)**: 
  - T004 (enum) can start immediately
  - T006, T008, T010 (interfaces) can run in parallel
  - T007, T009, T011 (implementations) can run after their respective interfaces
- **Phase 3 (US1)**: T024, T025, T026 (platform-specific URL handling) can run in parallel
- **Phase 4 (US2)**: T035 (styling) can run in parallel with other US2 tasks
- **Phase 6 (Polish)**: T043, T044, T045, T048 can run in parallel
- **Once Foundational phase completes**: All three user stories (US1, US2, US3) can be worked on in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Launch platform-specific implementations together:
Task: "Implement platform-specific URL handling for iOS App Store links in RedMist.Timing.UI.iOS/"
Task: "Implement platform-specific URL handling for Android Play Store links in RedMist.Timing.UI.Android/"
Task: "Implement browser refresh instructions display for Web/WASM in RedMist.Timing.UI.Browser/"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (verify dependencies and configuration)
2. Complete Phase 2: Foundational (CRITICAL - all services and models)
3. Complete Phase 3: User Story 1 (mandatory update enforcement)
4. **STOP and VALIDATE**: Test User Story 1 independently with server configured for mandatory update
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently (mandatory updates block) → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently (optional updates allow usage) → Deploy/Demo
4. Add User Story 3 → Test independently (current versions proceed silently) → Deploy/Demo
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1 (Mandatory Update)
   - Developer B: User Story 2 (Optional Update)
   - Developer C: User Story 3 (Current Version)
3. Stories complete and integrate independently

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Desktop platform excluded from version checking per spec (development-only)
- Server controls update policy via UIVersionInfo boolean flags (IsIOSMinimumMandatory, RecommendIOSUpdate, etc.)
- All version checking uses existing UIVersionInfo model from RedMist.TimingCommon package
- Platform detection via conditional compilation (#if ANDROID, IOS, BROWSER) and System.OperatingSystem methods
- Version retrieval via Assembly.GetExecutingAssembly().GetName().Version
- 5-second timeout for network resilience
- Modal dialogs for mandatory updates, dismissible notifications for optional updates
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Avoid: vague tasks, same file conflicts, cross-story dependencies that break independence

---

## Edge Case Handling

The following edge cases are addressed in the implementation:

- **Server unreachable**: GetVersionInfoAsync returns null after 5-second timeout, app proceeds normally (T017, T018)
- **Malformed version data**: CheckVersion treats unparseable versions as null, returns UpdateRequirement.None (T043)
- **App version detection failure**: GetCurrentApplicationVersion falls back to Version(1, 0, 0) (T044)
- **Platform detection failure**: GetCurrentPlatform uses System.OperatingSystem methods with fallback to Desktop, ShouldCheckVersion returns false (T045)
- **Development/beta versions**: Versions higher than server's latest return UpdateRequirement.None (T040)
