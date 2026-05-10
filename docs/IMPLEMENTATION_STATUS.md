# CimianAdmin Implementation Status

Last updated: 2026-05-09

## What works today

The app **launches** as a WinUI 3 window and can read a Cimian repository directory enough to report counts and validate the four expected subdirectories.

Verified flow on a real deployment repository (`Cimian/deployment` — 394 pkginfo, 615 manifests, 5 catalogs):

1. Run `dotnet build src/CimianAdmin/CimianAdmin.csproj -c Debug -p:Platform=x64`.
2. Launch `src\CimianAdmin\bin\x64\Debug\net10.0-windows10.0.19041.0\CimianAdmin.exe`.
3. Click **Browse...**, pick a directory.
4. Click **Open Repository** — it prints OK/missing for `catalogs/`, `manifests/`, `pkgsinfo/`, `pkgs/` plus a count of YAML files in each.

## What was added in the bootstrap session (2026-05-09)

### Models — `src/CimianAdmin.Core/Models/Packages/`

- `Package.cs` — full pkginfo model with YamlDotNet attributes. Fields: name, display_name, version, description, developer, category, catalogs, installer, uninstaller, unattended_install (default true), unattended_uninstall (default true), autoremove (default false), installs, supported_architectures, minimum_os_version, blocking_applications, requires, update_for, notes. Repository metadata (FilePath, LastModified) marked `[YamlIgnore]`. Computed `EffectiveDisplayName` returns DisplayName ?? Name.
- `Installer.cs` — type, location, arguments, hash, hash_algorithm, size.
- `Uninstaller.cs` — type, location, arguments.
- `InstallsItem.cs` — type, path, version, version_comparison, hash, hash_algorithm, registry_key, registry_value, product_code, upgrade_code.

### App shell — `src/CimianAdmin/`

- `App.xaml` + `App.xaml.cs` — minimal WinUI 3 application with default XamlControlsResources. **No DI yet.**
- `MainWindow.xaml` + `MainWindow.xaml.cs` — single-page welcome shell with Repository TextBox, Browse button (FolderPicker via `WinRT.Interop.InitializeWithWindow`), Open button. On open it lists subdirectory presence and YAML file counts. **Title bar shows `CimianAdmin 0.1.0`.**
- `Assets/.gitkeep` — placeholder so the existing `Content Include="Assets\**"` glob in `CimianAdmin.csproj` doesn't fail.

### Build configuration — `Directory.Build.props`

Suppressed analyzer rules that would otherwise block the build under `TreatWarningsAsErrors=true`:

- `CA1716` (namespace conflicts with reserved keyword — `Shared`)
- `CA1034` (nested public types in `Constants.cs`)
- `CA1002` (`List<T>` for public properties — needed for YAML deserialization)
- `CA2227` (collections need setters for YAML deserialization)
- `CA1003` (events use `EventHandler<T>` with non-EventArgs payloads — idiomatic for MVVM)
- `CA1515` (WinUI 3 partial classes need to be public for the XAML compiler)
- `CA5392` (WindowsAppSDK auto-init source ships without `DefaultDllImportSearchPaths`)
- `CA1305` (`StringBuilder.AppendLine` overloads without `IFormatProvider` — pedantic for UI)
- `CA1031` (catching `Exception` in click handlers — top-level UX safety)

### Package versions — `Directory.Packages.props`

Already bumped in the previous session to `Microsoft.EntityFrameworkCore 10.0.0-preview.2.25163.8`, `Microsoft.Extensions.* 10.0.0-preview.2.25163.2`, `CommunityToolkit.WinUI.Controls.SettingsControls 8.2.251219`. Added `Microsoft.Extensions.DependencyInjection.Abstractions`. No further changes needed.

## What's missing — the gap to "MunkiAdmin parity"

The current app is **Phase 1 scaffolding only**. To replicate [MunkiAdmin](https://github.com/hjuutilainen/munkiadmin) on Windows you need everything in `NEXT_STEPS.md`. The high-level gap:

| Surface | MunkiAdmin has | CimianAdmin has |
|---|---|---|
| App shell | NavigationView with sections + master/detail | Single-page welcome only |
| Persistence | Reopens last repo on launch; recent repos menu | Nothing — re-enters Browse every time |
| Repository | Header with name + quick stats | Inline text dump |
| Pkginfo (Packages) | List + searchable + multi-pane editor with all fields | Not implemented |
| Manifests | Tree + editor (managed_installs/uninstalls/optional, conditional items) | Not implemented |
| Catalogs | List with packages-per-catalog | Not implemented |
| Pkgs | List of installer files with size/hash/orphan detection | Not implemented |
| Categories / Developers / Icons | Auto-derived faceted views | Not implemented |
| Scripts | Editor for pre/post install/uninstall | Not implemented |
| Application Usage | Reads usage data | N/A |
| Cimian tools | Run `makecatalogs`/`cimiimport` from inside the app | Not implemented |
| Service implementations | — | **All four `IxService` interfaces in `CimianAdmin.Core/Services/` have NO implementations.** `CimianAdmin.Infrastructure` project is empty. |

## Key files for orientation

- `docs/PROJECT_PLAN.md` — original scope document (still accurate for the long-term vision)
- `docs/NEXT_STEPS.md` — concrete implementation plan for the next session
- `samples/SampleRepository/` — minimal test repo with one Firefox pkginfo + one site_default manifest
- `Cimian/deployment/` (parent repo) — real-world repo with 394 pkginfo / 615 manifests / 5 catalogs for stress testing
- `src/CimianAdmin.Core/Services/I{Repository,Package,Manifest,Catalog}Service.cs` — interfaces ready to implement
- `tests/CimianAdmin.Core.Tests/Models/PackageTests.cs` — passing tests for the Package model
- `tests/CimianAdmin.Core.Tests/Models/ManifestTests.cs` — existing manifest tests

## CI status

`.github/workflows/{ci,release}.yml` exist and the CI build is **green** as of the previous session (after the analyzer suppressions). Adding new code in the next session needs to keep CI green — re-run `dotnet build CimianAdmin.sln -c Release` locally before pushing.
