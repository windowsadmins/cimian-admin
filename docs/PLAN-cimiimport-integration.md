# Plan — cimiimport integration

Status: planning (not yet implemented)
Owner: CimianStudio
Related CLI: `packages/CimianTools/cli/cimiimport/Program.cs`

## Goal

Bring MunkiAdmin-style "drag an installer onto the app and import it" into CimianStudio. The user picks an `.msi` / `.exe` / `.nupkg` / `.msix`; the app shells out to `cimiimport.exe --nointeractive`, captures stdout/stderr, and lands the user in the new pkginfo editor.

## Why shell out vs. reimplement

`cimiimport` already encapsulates the canonical logic: hash, size, MSI ProductCode/UpgradeCode/version sniffing, icon extraction, pkgs/ layout convention, alphabetic key emitter. Re-doing this in C# would be a maintenance liability — the CLI is the source of truth and we want to stay aligned with it across Cimian releases. We invoke the same `cimiimport.exe` that ships with Cimian.

## CLI surface we depend on (today)

From `Program.cs`:

| Flag                          | Use |
|---|---|
| `<installerPath>` (positional)| File to import |
| `--repo_path <path>`          | Override repo root |
| `--nointeractive`             | No prompts — required for GUI invocation |
| `--arch <x64\|arm64>`         | Override arch |
| `--minimum_os_version`        | Optional version gate |
| `--maximum_os_version`        | Optional version gate |
| `--minimum_cimian_version`    | Optional version gate |
| `--uninstaller <path>`        | Optional uninstaller file |
| `--preinstall-script` …       | Script attachments |
| `--installs-array <path>`     | Repeatable; manual installs[] paths |
| `--extract-icon` / `--icon`   | Icon control |

## Phase 1 — basic import (MVP)

UI:
- New button on the Home page and on Packages page: **"Import installer…"** (FontIcon `` Upload + label).
- Click → `FileOpenPicker` filtered to `.msi;.exe;.nupkg;.msix;.zip`.
- On selection, show a **CimiImportDialog** (`ContentDialog`, similar to `CatalogCompareDialog`) with:
  - File path (read-only)
  - Architecture dropdown: `x64` / `arm64` (default: auto from filename / blank)
  - Minimum OS version (optional text)
  - Catalogs to publish to (checkboxes from `CatalogService.GetCatalogNamesAsync` + preferred order)
  - "Import" / "Cancel" buttons

Backend:
- New service `IImportService` in `CimianStudio.Core` with `Task<ImportResult> RunImportAsync(ImportOptions options, IProgress<string>? progress)`.
- Implementation in `CimianStudio.Infrastructure/Services/ImportService.cs`:
  - Resolves `cimiimport.exe` via the same `ResolveTool` helper `CatalogService` uses.
  - Builds `ProcessStartInfo.ArgumentList` from options (always includes `--nointeractive` + `--repo_path`).
  - Streams stdout/stderr line-by-line into `IProgress<string>`.
  - On exit-zero, parses the new pkginfo path out of stdout (cimiimport prints `Wrote pkgsinfo/...yaml`).
  - Returns `{ Success, NewPkgInfoPath, Stdout, Stderr }`.

Wire-up:
- After successful import: refresh `PackageService.GetAllPackagesAsync` (or trigger `PackagesChanged`), then `MainWindow.NavigateToPackage(newPackage)` so the user lands in the editor.
- After failed import: keep the dialog open with the cimiimport stderr in an `InfoBar`.

Tests:
- Unit-test `ImportService.BuildArguments(options)` to lock down the flag set we send.
- No end-to-end test for the actual exe (it touches disk + the live repo).

## Phase 2 — richer options + script attachment

- Add catalog checkbox row (writes `--repo_path` only; catalogs land in the emitted pkginfo via cimiimport's interactive logic — confirm what `--nointeractive` defaults are).
- Add a "Pre/post script" file pickers section (maps to `--preinstall-script` etc).
- Optional "uninstaller" file picker → `--uninstaller`.
- Manual `installs[]` editor (add/remove path strings) → repeated `--installs-array` flags.
- "Extract icon" checkbox → `--extract-icon` plus optional `--icon` output.

## Phase 3 — batch / drop-target

- Wire `App.xaml.cs` window-level drag-drop: dropping multiple files queues them, opens dialog per-file (or a multi-row dialog).
- Optional: a "queue" view that lets the user adjust each pending import before running them serially.

## Open questions

1. Does `cimiimport --nointeractive` honor catalog selection via flag, or only via config? — verify in `cli/cimiimport/Services/ImportService.cs` before building the catalog checkboxes.
2. Where do icons land when `--extract-icon` is used? — confirm path conventions so the UI can preview them after import.
3. How do we handle imports that need elevation (signing tools, MSI inspection)? — likely just surface the cimiimport stderr; the CLI handles its own elevation prompts when run interactively, but `--nointeractive` should fail fast.

## Out of scope

- Replacing or reimplementing cimiimport's pkginfo emission logic.
- Direct YAML write of a new pkginfo without going through `cimiimport.exe`.
- Modifying CimianTools itself (lives in a separate repo / submodule).
