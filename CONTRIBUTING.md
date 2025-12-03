# Contributing to CimianAdmin

Thank you for your interest in contributing to CimianAdmin. This document provides guidelines and information for contributors.

## Getting Started

1. Fork the repository on GitHub
2. Clone your fork locally
3. Create a branch for your changes
4. Make your changes following the coding standards below
5. Submit a pull request

## Development Environment

### Prerequisites

- Windows 10 version 1809 or later
- Visual Studio 2022 17.12+ or VS Code with C# Dev Kit
- .NET 10 SDK
- Git

### Building the Project

```powershell
git clone https://github.com/windowsadmins/cimianadmin.git
cd cimianadmin
dotnet restore
dotnet build
```

### Running Tests

```powershell
dotnet test
```

## Coding Standards

### General Guidelines

- Follow Microsoft C# coding conventions
- Use meaningful names for variables, methods, and classes
- Write XML documentation comments for public APIs
- Keep methods focused and reasonably sized
- Prefer composition over inheritance

### Code Style

- Use file-scoped namespaces
- Use primary constructors where appropriate
- Prefer pattern matching and switch expressions
- Use nullable reference types consistently
- Avoid magic numbers and strings; use constants

### Architecture

- Follow MVVM pattern for UI components
- Keep business logic in the Core project
- Infrastructure concerns belong in the Infrastructure project
- Use dependency injection for loose coupling

## Pull Request Process

1. Ensure your code builds without warnings
2. Run all tests and ensure they pass
3. Update documentation if needed
4. Write a clear PR description explaining the changes
5. Link any related issues

## Reporting Issues

When reporting issues, please include:

- A clear description of the problem
- Steps to reproduce
- Expected vs actual behavior
- CimianAdmin version
- Windows version
- Any relevant error messages or logs

## Code of Conduct

- Be respectful and inclusive
- Focus on constructive feedback
- Help others learn and grow

## Questions

If you have questions, open a discussion on GitHub or reach out to the maintainers.
