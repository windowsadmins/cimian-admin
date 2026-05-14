# CimianStudio

A modern Windows GUI application for managing Cimian software deployment repositories.

CimianStudio provides a graphical interface similar to MunkiAdmin (macOS) but designed specifically for Windows and Cimian repositories. It allows IT administrators to efficiently manage software packages, manifests, and catalogs for enterprise software deployment.

## Features

- Browse and manage Cimian repository contents
- Create, edit, and delete package definitions (pkginfo files)
- Manage deployment manifests with conditional items
- Generate and rebuild catalogs
- Import new packages with automatic metadata extraction
- Search and filter across packages and manifests
- Validate repository structure and detect issues

## Requirements

- Windows 10 version 1809 or later
- .NET 10 Runtime
- Cimian client tools (optional, for catalog generation)

## Getting Started

### Prerequisites

1. Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
2. Clone this repository:

```powershell
git clone https://github.com/windowsadmins/cimianstudio.git
cd cimianstudio
```

### Building

```powershell
dotnet build
```

### Running

```powershell
dotnet run --project src/CimianStudio
```

## Project Structure

```
CimianStudio/
  src/
    CimianStudio/              # WinUI 3 application
    CimianStudio.Core/         # Domain models and service interfaces
    CimianStudio.Infrastructure/  # YAML parsing, file system, tools integration
    CimianStudio.Shared/       # Shared constants and utilities
  tests/
    CimianStudio.Core.Tests/
    CimianStudio.Infrastructure.Tests/
  samples/
    SampleRepository/         # Example Cimian repository for testing
  docs/
    PROJECT_PLAN.md          # Detailed project plan and architecture
```

## Technology Stack

- .NET 10 with WinUI 3 (Windows App SDK)
- MVVM architecture with CommunityToolkit.Mvvm
- YamlDotNet for YAML serialization
- Entity Framework Core with SQLite for local caching
- Serilog for structured logging

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on contributing to this project.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

## Related Projects

- [Cimian](https://github.com/almenscorner/cimian) - Windows software deployment system
- [MunkiAdmin](https://github.com/hjuutilainen/munkiadmin) - macOS Munki repository manager (inspiration)
