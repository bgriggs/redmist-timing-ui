# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: C# (.NET 8+ with Avalonia UI)  
**Primary Dependencies**: Avalonia UI - MUST be MIT/Apache/BSD licensed  
**Storage**: [if applicable, e.g., SQLite (local), JSON files, cloud storage or N/A]  
**Testing**: [e.g., NUnit, xUnit, MSTest or NEEDS CLARIFICATION]  
**Target Platforms**: iOS 15+, Android API 21+, Web (WASM), Windows 10/11, macOS, Linux (ALL REQUIRED)
**Project Type**: multi-platform (MVVM architecture required)  
**Performance Goals**: <100ms UI response, <3s startup time, 60fps animations or NEEDS CLARIFICATION  
**Constraints**: Open source licensing only, MVVM pattern mandatory, cross-platform feature parity, light/dark theme support or NEEDS CLARIFICATION  
**Scale/Scope**: [domain-specific, e.g., personal use, enterprise, consumer app or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [ ] **MVVM Architecture**: Confirm ViewModels handle business logic, Views are declarative, Models are data-only
- [ ] **Cross-Platform Support**: Verify feature works on iOS, Android, Web (WASM), Windows Desktop, macOS, Linux
- [ ] **Open Source Licensing**: All dependencies use MIT, Apache 2.0, BSD, or LGPL licenses only
- [ ] **Comprehensive Testing**: Plan includes tests for ViewModels and platform implementations (added post-implementation)
- [ ] **Performance Standards**: UI response <100ms, startup <3s, memory usage monitored
- [ ] **Theme Support**: All UI elements support light/dark mode, colors defined in App.axaml, DynamicResource bindings used

[Additional gates determined based on specific feature requirements]

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
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/              # Data models (shared across platforms)
├── viewmodels/          # MVVM ViewModels (shared business logic)
├── services/            # Business services (shared)
├── views/               # Platform-specific UI implementations
│   ├── shared/          # Shared UI components
│   ├── ios/             # iOS-specific views
│   ├── android/         # Android-specific views
│   ├── web/             # Web/WASM-specific views
│   └── windows/         # Windows Desktop-specific views
└── lib/                 # Utility libraries

tests/
├── contract/            # API/interface contract tests
├── integration/         # Cross-platform integration tests
├── unit/               # Unit tests (focus on ViewModels)
└── platform/           # Platform-specific tests
    ├── ios/
    ├── android/
    ├── web/
    └── windows/

# [REMOVE IF UNUSED] Option 2: Multi-platform project structure
platforms/
├── shared/              # Shared libraries and ViewModels
│   ├── models/
│   ├── viewmodels/
│   └── services/
├── ios/                 # iOS-specific project
├── android/             # Android-specific project
├── web/                 # Web/WASM project
└── windows/             # Windows Desktop project

# [REMOVE IF UNUSED] Option 3: Microservice/API + Multiple frontends
backend/
├── api/                 # RESTful API
└── shared/              # Shared data models

frontends/
├── shared/              # Shared ViewModels and services
├── ios/                 # iOS app
├── android/             # Android app
├── web/                 # Web/WASM app
└── windows/             # Windows Desktop app

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
