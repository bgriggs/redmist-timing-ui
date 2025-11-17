<!--
Sync Impact Report:
- Version change: 1.1.0 → 1.2.0
- Modified principles: None
- Added sections:
  - Principle VI: Theme Support (light/dark mode requirement, centralized color management)
- Removed sections: None
- Templates requiring updates: 
  ✅ constitution.md (updated)
  ✅ plan-template.md (updated - added theme support to Constitution Check and Technical Context)
  ✅ spec-template.md (updated - added theme requirements CP-006, CP-007, CP-008 to Cross-Platform Requirements and PC-007, PC-008 to Platform Success Criteria)
  ✅ tasks-template.md (updated - added theme resource tasks T013-T014 to Foundational phase and theme verification tasks to Polish phase)
- Follow-up TODOs: 
  - Set actual ratification date when constitution is formally adopted
-->

# RedMist Timing UI Constitution

## Core Principles

### I. MVVM Architecture (NON-NEGOTIABLE)
All UI components MUST follow the Model-View-ViewModel pattern. ViewModels handle business logic and data binding, Views are purely declarative UI, Models represent data structures. No direct Model-View communication allowed. This ensures testability, separation of concerns, and consistent architecture across all platforms.

**Naming Conventions**: All asynchronous methods MUST be suffixed with "Async" (e.g., `LoadDataAsync`, `SaveChangesAsync`). This applies to all layers: ViewModels, Services, Repositories, and API clients.

**Rationale**: MVVM enables platform-agnostic business logic, facilitates unit testing of ViewModels without UI dependencies, and provides clear separation between UI and business logic essential for multi-platform development. Consistent async naming improves code readability and prevents confusion between synchronous and asynchronous operations.

### II. Cross-Platform Support (NON-NEGOTIABLE)
Application MUST support iOS, Android, Web (WASM), and Windows Desktop as first-class citizens. Shared business logic through common libraries. Platform-specific UI implementations allowed only when platform conventions require it. Feature parity required across all platforms unless technical limitations prevent it.

**Rationale**: Maximizes user reach, reduces development costs through code sharing, and ensures consistent user experience across different devices and operating systems.

### III. Open Source Licensing Only
All dependencies and packages MUST use permissive licenses (MIT, Apache 2.0, BSD) or LGPL. NO packages with restrictive licensing, copyleft requirements (GPL), or commercial-only licenses. License compatibility verification required before adding any dependency.

**Rationale**: Prevents legal complications, ensures application can be distributed freely, and avoids future licensing conflicts that could block releases or require expensive refactoring.

### IV. Comprehensive Testing
All features MUST include comprehensive tests after implementation is complete. Unit tests required for all ViewModels and business logic. Integration tests required for platform-specific implementations. Minimum 80% code coverage for shared libraries. Tests MUST be added before pull request approval.

**Rationale**: Ensures code quality, catches regressions, facilitates refactoring, and provides documentation of expected behavior across multiple platforms. Post-implementation testing allows for faster initial development while maintaining quality standards.

### V. Performance Standards
UI response time MUST be <100ms for user interactions. App startup time <3 seconds on target devices. Memory usage monitoring required. Platform-specific optimizations allowed when they don't break architectural principles.

**Rationale**: User experience is critical for timing applications where precision and responsiveness are essential. Performance standards ensure the app remains usable across all supported platforms.

### VI. Theme Support (NON-NEGOTIABLE)
All user interfaces MUST support both light mode and dark mode. Theme-specific colors and resources MUST be defined in `App.axaml` using `ResourceDictionary.ThemeDictionaries`. All UI components MUST reference theme resources using `DynamicResource` bindings, never hard-coded colors. Platform-specific theme overrides allowed only when respecting platform conventions.

**Rationale**: Modern applications must support user theme preferences for accessibility and user comfort. Centralized theme management in App.axaml ensures consistency, maintainability, and easy updates across the entire application. Dynamic resource binding enables runtime theme switching without requiring app restart.

## Platform Requirements

### Technology Stack Constraints
- **UI Framework**: Avalonia UI for cross-platform development
- **Shared Logic**: .NET 9+ with C#
- **Web**: WebAssembly (WASM) deployment required
- **Desktop**: Native Windows, macOS, and Linux application support
- **Mobile**: Native iOS and Android builds
- **Dependencies**: Only MIT, Apache 2.0, BSD, or LGPL licensed packages
- **Testing**: Platform-agnostic unit testing framework
- **Build**: Single codebase with platform-specific outputs
- **Theming**: App.axaml as single source of truth for colors and theme resources

### Platform-Specific Guidelines
Each platform implementation MUST maintain feature parity while respecting platform conventions. Platform-specific code limited to UI presentation layer only. Business logic remains in shared libraries following MVVM pattern.

### Theme Management
All color definitions and theme resources MUST be centralized in `RedMist.Timing.UI/App.axaml`. Light and dark theme dictionaries MUST contain all color resources needed across the application. UI components MUST use `DynamicResource` markup extensions to reference theme colors, enabling runtime theme switching.

## Development Workflow

### Architecture Review Process
All architectural decisions MUST be documented and reviewed for multi-platform impact. Changes affecting shared ViewModels require approval from platform leads. Breaking changes to Models require migration strategy for all platforms.

### Quality Gates
- License compliance verification before dependency addition
- MVVM pattern compliance review in all code reviews
- Cross-platform testing required before release
- Performance benchmarking on all target platforms

## Governance

Constitution supersedes all other development practices. All pull requests MUST verify compliance with MVVM architecture, cross-platform requirements, and theme support. License verification MUST be performed before adding any new dependencies. Performance standards MUST be validated through automated testing. All UI changes MUST be verified in both light and dark themes.

Amendments require documentation, cross-platform impact assessment, and approval from platform leads. Breaking changes require migration plan for all supported platforms.

**Version**: 1.3.0 | **Ratified**: 2025-10-24 | **Last Amended**: 2025-11-16
