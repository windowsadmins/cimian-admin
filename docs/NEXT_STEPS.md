# CimianStudio — Next Implementation Session

Last updated: 2026-05-09
Goal: take the launchable scaffold from `IMPLEMENTATION_STATUS.md` to a usable MunkiAdmin-equivalent on Windows.

## Session-zero context (read this first)

- **Read** `IMPLEMENTATION_STATUS.md` for what already exists and what doesn't.
- **Read** `PROJECT_PLAN.md` for the original phased plan.
- **Working dir** for this session: `C:\Users\rchristiansen\Developer\AzDevOps\Devices\Cimian\packages\CimianStudio\`.
- **Build/launch loop:**
  ```pwsh
  dotnet build src\CimianStudio\CimianStudio.csproj -c Debug -p:Platform=x64
  src\CimianStudio\bin\x64\Debug\net10.0-windows10.0.19041.0\CimianStudio.exe
  ```
- **Real test data:** `..\..\deployment\` (Cimian's deployment repo) — 394 pkginfo, 615 manifests, 5 catalogs. Use it for performance and edge cases.
- **Sample data:** `samples\SampleRepository\` — one Firefox pkginfo, one manifest. Use it for unit-testing the round-trip.
- **Reference UI:** [MunkiAdmin on macOS](https://github.com/hjuutilainen/munkiadmin) — match its shape (sidebar sections + master/detail) and field coverage.

## User-stated requirements

1. App opens the **last-used repository on launch** automatically.
2. If there is no last-used repository, the app launches with the **Browse dialog already open** (no extra click needed).
3. The application shell (sidebar + chrome) is **always visible**, even before a repo is loaded — only the content panes show "no repo" placeholders.
4. **Replicate MunkiAdmin functionally**: package editor, manifest editor, catalog viewer, etc.

## Recommended target architecture

```
src/CimianStudio/
  App.xaml(.cs)                    -- Hosts Microsoft.Extensions.Hosting; resolves MainWindow
  MainWindow.xaml(.cs)             -- NavigationView shell; switches Frame content
  Views/
    WelcomePage.xaml(.cs)          -- Empty-state with Browse + recent repos
    RepositoryPage.xaml(.cs)       -- Repo overview, validation
    PackagesPage.xaml(.cs)         -- Master/detail: list + PackageEditor
    PackageEditor.xaml(.cs)        -- Tabbed editor: General / Installer / Detection / Scripts / Advanced
    ManifestsPage.xaml(.cs)        -- Master/detail: list + ManifestEditor
    ManifestEditor.xaml(.cs)       -- managed_installs/uninstalls/optional + conditional items
    CatalogsPage.xaml(.cs)         -- List of catalogs; selection shows pkgs in catalog
    SettingsPage.xaml(.cs)         -- App preferences (placeholder)
  ViewModels/
    MainViewModel.cs               -- Holds current repo + nav state
    PackagesViewModel.cs           -- Source list + filter + selection
    ManifestsViewModel.cs
    CatalogsViewModel.cs
    PackageEditorViewModel.cs
    ManifestEditorViewModel.cs
  Services/
    INavigationService.cs / NavigationService.cs

src/CimianStudio.Infrastructure/   -- currently empty; needs:
  Yaml/YamlSerialization.cs        -- One DeserializerBuilder + SerializerBuilder shared by all
  Services/RepositoryService.cs    -- Implements IRepositoryService; raises RepositoryChanged
  Services/PackageService.cs       -- Implements IPackageService; loads + saves YAML
  Services/ManifestService.cs      -- Implements IManifestService
  Services/CatalogService.cs       -- Implements ICatalogService; spawns makecatalogs.exe
  Settings/ISettingsService.cs / JsonSettingsService.cs
                                   -- Reads/writes %LOCALAPPDATA%\CimianStudio\settings.json
                                   -- Tracks LastRepositoryPath + RecentRepositories (max 10)

tests/CimianStudio.Infrastructure.Tests/
  YamlRoundTripTests.cs            -- Round-trips samples/SampleRepository/.../Firefox.yaml
  RepositoryServiceTests.cs        -- Validates against samples + temp dirs
```

## Implementation order (what to do, in order)

### Step 1 — Infrastructure services (no UI changes yet)

**File:** `src/CimianStudio.Infrastructure/Yaml/YamlSerialization.cs`

Build a static class with a `Serializer` and `Deserializer` configured with `IgnoreUnmatchedProperties()` and `WithNamingConvention(UnderscoredNamingConvention.Instance)`. Used by all three services.

**File:** `src/CimianStudio.Infrastructure/Services/RepositoryService.cs`

Implement `IRepositoryService`:
- `OpenRepositoryAsync(path)`: validate the four subdirs exist (`catalogs`, `manifests`, `pkgsinfo`, `pkgs`); set `IsValid` accordingly; populate counts via `Directory.EnumerateFiles(..., "*.yaml", SearchOption.AllDirectories).Count()`.
- `CreateRepositoryAsync(path)`: `Directory.CreateDirectory` for all four subdirs.
- `ValidateRepositoryAsync`: returns `RepositoryValidationResult` with errors per missing subdir.
- `RefreshStatisticsAsync`: re-counts.
- Raise `RepositoryChanged` whenever `CurrentRepository` changes.

**File:** `src/CimianStudio.Infrastructure/Services/PackageService.cs`

Implement `IPackageService`:
- `GetAllPackagesAsync()`: enumerate `pkgsinfo/**/*.yaml`, deserialize each into `Package`, set `FilePath` and `LastModified` from `FileInfo`. Use `Parallel.ForEachAsync` (or `Task.WhenAll`) — 394 files needs to be fast.
- `GetPackageByPathAsync(filePath)`: read + deserialize one file.
- `GetPackageAsync(name, version)`: filter all packages.
- `SavePackageAsync(package)`: serialize + write to `package.FilePath`.
- `CreatePackageAsync(package, relativePath)`: write to `pkgsinfo/{relativePath}/{name}-{version}.yaml`.
- `DeletePackageAsync`: file delete + optional installer delete.
- `SearchPackagesAsync`: filter on Name / DisplayName / Description / Developer (case-insensitive Contains).
- `ImportPackageAsync(installerPath)`: shell out to `cimiimport.exe` (under `C:\Program Files\Cimian\`); deserialize the output.
- Raise `PackagesChanged` after save/create/delete.

**File:** `src/CimianStudio.Infrastructure/Services/ManifestService.cs`

Same shape as PackageService but for `manifests/`.

**File:** `src/CimianStudio.Infrastructure/Services/CatalogService.cs`

- `GetAllCatalogsAsync()`: enumerate `catalogs/*.yaml`, deserialize as `List<Package>` (a catalog is a list of pkginfo entries, NOT separate Catalog objects). Each becomes a `Catalog { Name = filename, Packages = ... }`.
- `GetCatalogNamesAsync()`: union of all `Catalogs` arrays across packages, plus existing files.
- `RebuildCatalogsAsync()`: shell out to `makecatalogs.exe`; capture stdout.

**Settings:**

`src/CimianStudio.Shared/Settings/AppSettings.cs`:
```csharp
public sealed class AppSettings
{
    public string? LastRepositoryPath { get; set; }
    public List<string> RecentRepositories { get; set; } = [];
    public int MaxRecentRepositories { get; set; } = 10;
    public double WindowWidth { get; set; } = 1400;
    public double WindowHeight { get; set; } = 900;
}
```

`src/CimianStudio.Shared/Settings/ISettingsService.cs` + `src/CimianStudio.Infrastructure/Settings/JsonSettingsService.cs`:
- Path: `%LOCALAPPDATA%\CimianStudio\settings.json`
- Methods: `Task<AppSettings> LoadAsync()`, `Task SaveAsync(AppSettings)`, `Task RecordRepositoryAsync(string path)` (sets LastRepositoryPath, prepends to RecentRepositories, deduplicates, trims to MaxRecentRepositories).

### Step 2 — DI bootstrap

**File:** `src/CimianStudio/App.xaml.cs`

Replace the bare `App()` with a `Microsoft.Extensions.Hosting.IHost` setup:

```csharp
public partial class App : Application
{
    public static IHost Host { get; private set; } = null!;
    public static T Resolve<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    public App()
    {
        InitializeComponent();
        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<IRepositoryService, RepositoryService>();
                services.AddSingleton<IPackageService, PackageService>();
                services.AddSingleton<IManifestService, ManifestService>();
                services.AddSingleton<ICatalogService, CatalogService>();
                services.AddSingleton<MainViewModel>();
                services.AddTransient<PackagesViewModel>();
                services.AddTransient<ManifestsViewModel>();
                services.AddTransient<CatalogsViewModel>();
                services.AddTransient<MainWindow>();
            })
            .Build();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = Resolve<MainWindow>();
        window.Activate();
        await Resolve<MainViewModel>().InitializeAsync();
    }
}
```

`MainViewModel.InitializeAsync()`:
- Load settings.
- If `LastRepositoryPath` is set AND `Directory.Exists(LastRepositoryPath)` → call `IRepositoryService.OpenRepositoryAsync` and navigate to RepositoryPage.
- Otherwise → navigate to WelcomePage AND immediately invoke the FolderPicker (the user's stated requirement: "Browse dialog already open").

### Step 3 — Replace MainWindow with a NavigationView shell

**File:** `src/CimianStudio/MainWindow.xaml`

```xml
<Window x:Class="CimianStudio.MainWindow" ...>
    <Grid>
        <NavigationView x:Name="NavView"
                        IsBackButtonVisible="Collapsed"
                        IsSettingsVisible="True"
                        SelectionChanged="NavView_SelectionChanged"
                        PaneDisplayMode="Left">
            <NavigationView.MenuItems>
                <NavigationViewItem Tag="repository" Content="Repository" Icon="Home" />
                <NavigationViewItem Tag="packages"   Content="Packages"   Icon="AllApps" />
                <NavigationViewItem Tag="manifests"  Content="Manifests"  Icon="Bookmarks" />
                <NavigationViewItem Tag="catalogs"   Content="Catalogs"   Icon="List" />
            </NavigationView.MenuItems>
            <NavigationView.PaneFooter>
                <StackPanel Padding="12">
                    <TextBlock x:Name="RepoPathText"
                               Text="No repository"
                               TextWrapping="Wrap"
                               Style="{StaticResource CaptionTextBlockStyle}" />
                    <Button Content="Switch repository..." Click="OnSwitchRepoClicked" Margin="0,8,0,0" />
                </StackPanel>
            </NavigationView.PaneFooter>
            <Frame x:Name="ContentFrame" />
        </NavigationView>
    </Grid>
</Window>
```

**File:** `src/CimianStudio/MainWindow.xaml.cs`

- On selection change, `ContentFrame.Navigate(typeof(...))` to the right page.
- Subscribe to `IRepositoryService.RepositoryChanged` to update `RepoPathText` and re-enable nav items.
- When no repo is loaded, navigate to `WelcomePage` and disable Packages/Manifests/Catalogs items.
- `OnSwitchRepoClicked` invokes the `FolderPicker` flow already in the current `MainWindow.xaml.cs:OnBrowseClicked`.

### Step 4 — Pages (in order of value)

1. **`WelcomePage`** — empty state. Shows recent repositories as a `ListView`, plus a big "Open Repository" button. On launch with no last repo, automatically pops the picker.
2. **`RepositoryPage`** — header with repo name + path; cards for `Packages`, `Manifests`, `Catalogs` showing counts; a "Validate" button that runs `IRepositoryService.ValidateRepositoryAsync` and shows errors/warnings.
3. **`PackagesPage`** — `Grid` with two columns: left is `ListView` with search box (filter via `PackagesViewModel`), right is `PackageEditor` user control bound to `SelectedPackage`. Show `Name`, `Version`, `Catalogs` joined as the row content.
4. **`PackageEditor`** — `Pivot` (or `TabView`) with tabs:
   - General: Name, DisplayName, Version, Description, Developer, Category, Catalogs (list editor)
   - Installer: Type, Location, Arguments (list), Hash, HashAlgorithm
   - Uninstaller: Type, Location, Arguments
   - Detection: Installs items list with edit form
   - Advanced: SupportedArchitectures, MinimumOsVersion, BlockingApplications, Requires, UpdateFor, UnattendedInstall, UnattendedUninstall, Autoremove, Notes
   Save button calls `PackageService.SavePackageAsync`.
5. **`ManifestsPage` + `ManifestEditor`** — same pattern. Editor needs:
   - Catalogs (list)
   - ManagedInstalls (list)
   - ManagedUninstalls (list)
   - ManagedUpdates (list)
   - OptionalInstalls (list)
   - DefaultInstalls (list)
   - IncludedManifests (list)
   - ConditionalItems (collapsible repeater of `ConditionalItem` editors with nested support)
   - Notes (multiline)
6. **`CatalogsPage`** — `ListView` of catalog names on left; selection shows the packages in that catalog on the right with a "Rebuild Catalogs" button at the top.
7. **`SettingsPage`** — placeholder for now (just shows version + settings file path).

## Suggested ViewModel pattern

Use `CommunityToolkit.Mvvm`:

```csharp
public partial class PackagesViewModel : ObservableObject
{
    private readonly IPackageService _packages;
    [ObservableProperty] private ObservableCollection<Package> _filteredPackages = new();
    [ObservableProperty] private Package? _selectedPackage;
    [ObservableProperty] private string _searchText = string.Empty;

    public PackagesViewModel(IPackageService packages) { _packages = packages; }

    public async Task LoadAsync()
    {
        var all = await _packages.GetAllPackagesAsync();
        FilteredPackages = new(all);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    private void ApplyFilter() { /* set FilteredPackages from cached _all */ }
}
```

## Tests to add as you go

- `tests/CimianStudio.Infrastructure.Tests/Yaml/PackageRoundTripTests.cs` — load `samples/SampleRepository/pkgsinfo/Mozilla/Firefox.yaml`, deserialize, re-serialize, compare structurally.
- `tests/CimianStudio.Infrastructure.Tests/Services/RepositoryServiceTests.cs` — open `samples/SampleRepository`, assert IsValid, counts.
- `tests/CimianStudio.Infrastructure.Tests/Services/PackageServiceTests.cs` — GetAllPackagesAsync count, search.
- `tests/CimianStudio.Infrastructure.Tests/Settings/JsonSettingsServiceTests.cs` — round-trip in temp dir.

## Things I expect will trip you up

1. **WinUI 3 + DI**: `Frame.Navigate(typeof(PackagesPage))` constructs the page itself, not via DI. The clean fix is a custom `IXamlMetadataProvider` factory or just doing `var page = App.Resolve<PackagesPage>(); Frame.Content = page;`. The latter is simpler.
2. **FolderPicker on WinUI 3**: must call `WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd)` — see the existing `MainWindow.xaml.cs:OnBrowseClicked`.
3. **`Microsoft.Extensions.Hosting` in WinUI**: works fine but you do NOT call `host.Run()` — just `Build()` and resolve services manually. `Host.StartAsync()` would deadlock the dispatcher.
4. **YamlDotNet null behavior**: by default it serializes nulls. Use `.ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)` on the `SerializerBuilder` to keep round-tripped files clean.
5. **Loading 394 pkginfo files**: do it in parallel and report progress, otherwise the UI freezes for ~2s on first open. Show a `ProgressRing` overlay until `LoadAsync` completes.
6. **Analyzer suppressions**: when you add new code, you may hit additional CA-rule errors because `TreatWarningsAsErrors=true`. Either add to `Directory.Build.props` `NoWarn` or fix the code — preference depends on the rule. The list of currently-suppressed rules is in `IMPLEMENTATION_STATUS.md`.

## Definition of "session done"

Minimum bar to declare the next session a success:

- App opens last repo on launch (or pops Browse if no last repo).
- NavigationView shell with Repository / Packages / Manifests / Catalogs sections is always visible.
- Packages section: list of all packages in the repo with search, click a row to see its full pkginfo in an editor (read-only is OK for v1).
- Manifests section: list with view-only editor.
- Catalogs section: list of catalogs, click one to see its packages.
- CI is still green (`dotnet build CimianStudio.sln -c Release` passes locally and on push).

Stretch goals for the same session:

- Editor panes are editable (Save button writes back to YAML).
- `Add Package` / `Delete Package` from the Packages page.
- Settings persistence works across launches.
- Recent repositories appear on Welcome page and re-open via single click.

## Out of scope (later sessions)

- Pkgs view (orphan installer detection)
- Categories / Developers / Icons faceted views
- Scripts editor
- Drag-and-drop installer import
- `cimiimport` integration
- `makecatalogs` from inside the app (button is fine, but spawning the process is the deferred part)
- EF Core / SQLite caching layer (the in-memory load is fast enough for 394 packages)
- WinUI 3 packaging (.msix) — current `WindowsPackageType=None` is correct

## Where to commit

The CimianStudio repo is a git submodule. Push commits directly to `main` on https://github.com/windowsadmins/cimianstudio. The parent Cimian repo can be left with a stale submodule pointer for now; bump it at the end of the session if desired.

## How to start the next session

```pwsh
cd C:\Users\rchristiansen\Developer\AzDevOps\Devices\Cimian\packages\CimianStudio
claude  # then: "Read docs/IMPLEMENTATION_STATUS.md and docs/NEXT_STEPS.md, then begin Step 1"
```
