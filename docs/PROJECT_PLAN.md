# CimianAdmin Project Plan

## Overview

CimianAdmin is a native Windows GUI application for managing Cimian software deployment repositories. It provides similar functionality to MunkiAdmin (macOS) but is designed specifically for Windows and Cimian.

## Goals

- Provide a user-friendly interface for managing Cimian repositories
- Enable efficient creation and editing of package definitions (pkginfo files)
- Support manifest management with conditional items
- Integrate with Cimian tools for catalog generation
- Ensure modern Windows look and feel using WinUI 3

## Technology Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 10 |
| UI | WinUI 3 (Windows App SDK 1.6+) |
| Architecture | MVVM |
| MVVM Toolkit | CommunityToolkit.Mvvm |
| Serialization | YamlDotNet |
| Database | Entity Framework Core 10 + SQLite |
| Logging | Serilog |
| Testing | xUnit, FluentAssertions, Moq |

## Project Structure

```
CimianAdmin/
  src/
    CimianAdmin/                    # WinUI 3 application
      Views/                        # XAML views
      ViewModels/                   # View models
      Services/                     # Application services
      Helpers/                      # UI helpers
      Assets/                       # Icons and resources
    CimianAdmin.Core/               # Domain layer
      Models/                       # Domain models
      Services/                     # Service interfaces
    CimianAdmin.Infrastructure/     # Infrastructure layer
      Serialization/                # YAML serialization
      FileSystem/                   # File system operations
      Tools/                        # Cimian tools integration
      Data/                         # EF Core context
    CimianAdmin.Shared/             # Shared code
      Constants/                    # Application constants
  tests/
    CimianAdmin.Core.Tests/
    CimianAdmin.Infrastructure.Tests/
  samples/
    SampleRepository/               # Test repository
  docs/
    PROJECT_PLAN.md                 # This document
```

## Development Phases

### Phase 1: Foundation (Current)

- Project structure and build configuration
- Core domain models (Package, Manifest, Catalog)
- Service interfaces
- Basic test infrastructure

### Phase 2: Infrastructure

- YAML serialization for pkginfo and manifests
- File system service for repository operations
- Repository validation
- SQLite caching layer

### Phase 3: Basic UI

- Application shell with navigation
- Repository open/create dialogs
- Package list view with search and filter
- Basic package editor

### Phase 4: Full Functionality

- Manifest editor with conditional items
- Catalog viewer and rebuild
- Package import with metadata extraction
- Cimian tools integration (makecatalogs, cimiimport)

### Phase 5: Polish

- Settings and preferences
- Keyboard shortcuts
- Recent repositories
- Error handling and user feedback
- Performance optimization

## Feature Scope

### MVP Features

- Open existing Cimian repository
- Browse packages with search and filter
- View and edit package properties
- View and edit manifests
- Rebuild catalogs

### Extended Features

- Create new repository
- Import packages from installers
- Drag and drop support
- Batch operations on packages
- Manifest conditional item builder
- Duplicate package detection
- Repository health check

### Future Features

- Multiple repository support
- Repository synchronization
- Diff view for changes
- Undo/redo support
- Plugin system

## UI Design

### Navigation

- Left sidebar with navigation menu
- Top bar with repository info and actions
- Main content area with list/detail views

### Views

1. Packages View
   - List of all packages with columns (Name, Version, Catalogs)
   - Detail panel for selected package
   - Search and filter controls

2. Manifests View
   - Tree view of manifests
   - Detail panel with package assignments
   - Conditional item editor

3. Catalogs View
   - List of catalogs with package counts
   - Rebuild catalog action
   - Package list per catalog

4. Settings View
   - Application preferences
   - Cimian tools configuration
   - Repository defaults

## Data Models

### Package (pkginfo)

Represents a software package with:
- Identity (name, version, display_name)
- Metadata (description, developer, category)
- Installer configuration (type, location, arguments)
- Detection rules (installs items)
- Scripts (pre/post install/uninstall)
- Dependencies (requires, update_for)
- Behavior flags (blocking_applications, restart_action)

### Manifest

Represents a deployment manifest with:
- Catalog references
- Managed installs/uninstalls
- Optional installs
- Included manifests
- Conditional items

### Conditional Item

Represents conditional deployment logic with:
- Condition expression or fact-based rules
- Package assignments when condition is true
- Nested conditions for complex logic

## Integration

### Cimian Tools

- makecatalogs: Rebuild catalogs from pkginfo files
- cimiimport: Import packages with metadata extraction

### File System

- Watch for external changes
- Support for symbolic links
- Handle large repositories efficiently

## Testing Strategy

- Unit tests for models and services
- Integration tests for file operations
- UI tests for critical workflows
- Sample repository for manual testing

## Repository

- GitHub: https://github.com/windowsadmins/cimianadmin
- License: MIT
- Contributing: See CONTRIBUTING.md
