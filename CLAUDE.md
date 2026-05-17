# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

CimianStudio is a WinUI 3 / .NET 10 admin GUI for managing Cimian software-deployment repositories (the Windows analogue of MunkiAdmin). It ships as a signed MSI; CI is unsigned-only because hosted runners can't access the enterprise cert.

Project-wide rules in the parent repo's `CLAUDE.md` (signing, sudo over `RunAs`, worktree-first workflow, no-emoji commit messages, don't push without explicit instruction) apply here too.

## Build & run

`build.ps1` is the supported entry point — Defender ASR rule `01443614-cd74-433a-b99e-2ecdc07bfc25` blocks freshly-built unsigned exes on the dev box, so signing is mandatory unless you've added a Defender exclusion.

```pwsh
.\build.ps1                              # Debug/x64, signs by default
.\build.ps1 -Run                         # build + sign + launch
.\build.ps1 -Configuration Release -Architecture arm64 -Clean
.\build.ps1 -Release                     # full pipeline: publish + sign + cimipkg MSI + sign MSI (x64 + arm64)
.\build.ps1 -Release -Architecture x64 -Install   # build signed MSI and msiexec it on this host
.\build.ps1 -Release -Upload v0.3.0      # push signed artifacts to GitHub release, clobbering unsigned CI uploads
.\build.ps1 -NoSign                      # only use when targeting a different host
$env:CIMIAN_CERT_SUBJECT = 'YourOrg'; .\build.ps1   # override cert subject (default: EmilyCarrU)
```

Quick `dotnet` iteration without packaging:

```pwsh
dotnet restore CimianStudio.sln
dotnet build CimianStudio.sln -c Release
dotnet test CimianStudio.sln -c Release
dotnet test tests/CimianStudio.Core.Tests/CimianStudio.Core.Tests.csproj   # single project
dotnet test --filter "FullyQualifiedName~Canary"                           # idempotency canary on real deployment files
```

`TreatWarningsAsErrors=true` is set in `Directory.Build.props` for every project — silenced rules are listed there with reasons, don't add new `NoWarn` entries without one.

### cimipkg dependency for `-Release`

`Invoke-Release` shells out to `cimipkg.exe` to produce the MSI. `Get-CimipkgPath` searches in this order: sibling `../CimianTools/release/{x64,arm64}/cimipkg.exe`, then `PATH`, then a pinned GitHub download. **It refuses to run any `cimipkg.exe` whose Authenticode subject doesn't contain `EmilyCarrU`** (override via `$env:CIMIPKG_EXPECTED_SUBJECT`). For the GitHub download path you must set `$env:CIMIPKG_VERSION` to a release tag — there's no "latest" fallback by design (supply-chain pin).

## Architecture

Standard clean-architecture layering with WinUI 3 on top. DI graph is wired in `src/CimianStudio/App.xaml.cs` using `Microsoft.Extensions.Hosting` — that's the canonical place to look for "where does this service come from."

- **`src/CimianStudio/`** — WinUI 3 host. `App.xaml.cs` builds the `IHost`, registers services as singletons and view-models/pages as transients. `GitPage` and `ImportPage` are deliberately singletons so cross-tab handoffs (Import → Git, Packages drop → Import) operate on the live page instance.
- **`src/CimianStudio.Core/`** — Domain models (`Models/{Packages,Manifests,Catalogs,Predicates,Repository,Git,Search}`) and service interfaces (`I*Service`, `ISessionState`). No infrastructure dependencies.
- **`src/CimianStudio.Infrastructure/`** — Implementations: `{Package,Manifest,Catalog,Repository,Git,Search}Service`, the thin `Yaml/PackageYaml.cs` shim around upstream `YamlUtils` (handles only the `_metadata` underscore-alias workaround and script trailing-newline normalization), `Import/WinUIImportPrompter.cs` (GUI-side `IImportPrompter` adapter), `EditorSessionState`, settings persistence.
- **`src/CimianStudio.Shared/`** — Constants and settings types shared across layers. (The `CA1716` namespace-warning is intentionally silenced because of the name `Shared`.)
- **`tests/CimianStudio.{Core,Infrastructure}.Tests/`** — xUnit + FluentAssertions + Moq.

MVVM uses `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`). Models that round-trip through YAML use `List<T>` and public setters by design (`CA1002`/`CA2227` silenced for that reason).

### Cross-page state plumbing

`App.PendingPackageSelection` / `App.PendingManifestSelection` are one-shot statics consumed by the next-loaded `PackagesPage` / `ManifestsPage`. Use them for cross-page navigation (e.g. catalog row → open in package editor); don't promote them to long-lived state.

### Cross-repo ProjectReferences

CimianStudio links two upstream Cimian shared libraries via `..\..\..\..\packages\CimianTools\shared\` ProjectReferences (sibling-submodule layout under a parent `Cimian/` folder):

- `Cimian.Core.csproj` (in `CimianStudio.Infrastructure.csproj`) — hosts `YamlUtils`, the single source of truth for pkginfo/manifest/catalog YAML across every Cimian tool. CimianStudio routes through it via the thin `PackageYaml` shim. **If you find yourself reaching for a parallel YAML serializer here, stop — fix `YamlUtils` upstream instead.**
- `Cimian.Import.csproj` (in both `CimianStudio.csproj` and `CimianStudio.Infrastructure.csproj`) — hosts `MetadataExtractor`, `IImportPrompter`, and `ImportService.ImportAsync`. The wizard (`Views/Import/ImportPage.xaml.cs`) drives the UX, then hands collected state to `ImportService.ImportAsync` via `WinUIImportPrompter` for the actual disk write (hash, copy, pkginfo serialize). Same canonical form as `cimiimport`.

CI mirrors this with `.github/workflows/ci.yml` cloning `windowsadmins/cimian` into the four-levels-up slot. If you rename or move that path, update **both** csproj files and the CI workflow.

### Import flow

The `Views/Import/ImportPage.xaml.cs` wizard is *purely UI*: drag-drop, queue, four-step form (review, edit metadata, scripts, location+preview). On Save it writes any user-edited script content to temp files, builds a `WinUIImportPrompter` holding the collected state, and calls `ImportService.ImportAsync(...)` — the upstream orchestrator handles template lookup (against `All.yaml`), file hash, installer copy, and pkginfo write. The wizard then refreshes its preview from the file ImportService just wrote (`pkginfo` location is derived in `ComputeImportedPaths`, mirroring ImportService's filename scheme — keep them in sync).

`PackageService.NotifyPackagesChanged()` is the manual nudge the wizard sends after `ImportService` returns, since `ImportService` writes the pkginfo directly and bypasses `PackageService.CreatePackageAsync` (which would have fired the event automatically).

## Cert / signing details

`build.ps1` mirrors `CimianTools/build.ps1`:

- Looks up cert by subject (default `EmilyCarrU`, override `$env:CIMIAN_CERT_SUBJECT`), prefers `CurrentUser\My`, falls back to `LocalMachine\My`.
- Tries in-process `signtool` against three RFC3161 TSAs in order (DigiCert, Sectigo, Entrust). If ASR denies modification of the fresh binary, falls back to `sudo signtool` — **one** elevated invocation that signs every file in one shot, not one UAC prompt per file. Don't refactor this into per-file `Invoke-SignArtifact` calls during release builds.
- The full release pipeline signs CimianStudio-shipped binaries only and **leaves Microsoft.*/Windows App SDK runtime DLLs alone** — re-signing them invalidates Microsoft's signatures.
