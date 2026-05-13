namespace CimianAdmin.Views.Import;

using Cimian.CLI.Cimiimport.Models;
using Cimian.CLI.Cimiimport.Services;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

/// <summary>
/// Wizard state — drives the step indicator, panel visibility, and Back / Continue
/// behavior. Steps 3-5 (scripts, location, review) plug in here as the wizard grows.
/// </summary>
internal enum WizardStep
{
    Review = 1,
    EditMetadata = 2,
    Scripts = 3,
    LocationAndReview = 4,
}

/// <summary>
/// Cimiimport-native import wizard host. Idle view = drop-zone / file picker.
/// Single-file selection auto-advances to Step 1 (detected metadata + template
/// match banner). Multi-file selection enqueues for batch processing (M6).
/// Steps 2-5 land in M4.
/// </summary>
public sealed partial class ImportPage : Page
{
    private readonly IRepositoryService _repositoryService;
    private readonly IPackageService _packageService;
    private readonly ICatalogService _catalogService;
    private InstallerMetadata? _metadataBuffer;
    private Package? _templateMatch;
    private bool _useTemplate;
    private WizardStep _step = WizardStep.Review;
    private IReadOnlyList<string> _knownCatalogs = [];
    private IReadOnlyList<string> _knownPackages = [];
    // Script slots live on PkgsInfo, not InstallerMetadata, so we hold them
    // separately and stitch everything together when writing the pkginfo in
    // Step 5. Null means "no script for this slot" (omitted from YAML).
    private readonly Dictionary<string, string?> _scripts = new(StringComparer.Ordinal)
    {
        ["preinstall"] = null,
        ["postinstall"] = null,
        ["preuninstall"] = null,
        ["postuninstall"] = null,
        ["installcheck"] = null,
        ["uninstallcheck"] = null,
    };

    public ImportViewModel ViewModel { get; }

    public ImportPage(
        ImportViewModel viewModel,
        IRepositoryService repositoryService,
        IPackageService packageService,
        ICatalogService catalogService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(repositoryService);
        ArgumentNullException.ThrowIfNull(packageService);
        ArgumentNullException.ThrowIfNull(catalogService);
        ViewModel = viewModel;
        _repositoryService = repositoryService;
        _packageService = packageService;
        _catalogService = catalogService;
        InitializeComponent();
    }

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to import queue";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }
    }

    private async void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items
            .OfType<Windows.Storage.StorageFile>()
            .Select(f => f.Path)
            .ToList();

        await HandleSelectedFilesAsync(paths).ConfigureAwait(true);
    }

    private async void OnPickFileClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add(".msi");
        picker.FileTypeFilter.Add(".exe");
        picker.FileTypeFilter.Add(".nupkg");
        picker.FileTypeFilter.Add(".msix");
        picker.FileTypeFilter.Add(".msixbundle");
        picker.FileTypeFilter.Add(".appx");

        var window = App.MainWindowInstance;
        if (window is not null)
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
        }

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0)
        {
            return;
        }

        await HandleSelectedFilesAsync(files.Select(f => f.Path).ToList()).ConfigureAwait(true);
    }

    /// <summary>
    /// Single file → auto-advance into Step 1 of the wizard (mirrors what a CLI
    /// invocation does after the first prompt: extract + show + ask).
    /// Multi-file → enqueue for batch import (queue UI lands in M6).
    /// </summary>
    private async Task HandleSelectedFilesAsync(List<string> paths)
    {
        if (paths.Count == 0)
        {
            StatusText.Text = "No files in the drop (folders aren't supported yet).";
            return;
        }

        ViewModel.EnqueueFiles(paths);

        if (paths.Count > 1)
        {
            StatusText.Text = $"Queued {paths.Count} files for batch import — queue UI lands in M6.";
            return;
        }

        await EnterWizardAsync(paths[0]).ConfigureAwait(true);
    }

    private async Task EnterWizardAsync(string filePath)
    {
        _sourceInstallerPath = filePath;
        IdleView.Visibility = Visibility.Collapsed;
        WizardView.Visibility = Visibility.Visible;
        WizardFileName.Text = System.IO.Path.GetFileName(filePath);
        DetectedGrid.RowDefinitions.Clear();
        DetectedGrid.Children.Clear();
        ExtractError.Visibility = Visibility.Collapsed;
        TemplateBanner.IsOpen = false;
        SetStep(WizardStep.Review);
        ContinueButton.IsEnabled = false;

        // Run the extractor off the UI thread — MSI/MSIX parsing can touch the
        // Wix DTF interop layer which is slower than we want on the dispatcher.
        InstallerMetadata? meta = null;
        string? error = null;
        try
        {
            meta = await Task.Run(() =>
            {
                var extractor = new MetadataExtractor();
                var config = new ImportConfiguration
                {
                    RepoPath = _repositoryService.CurrentRepository?.RootPath ?? string.Empty,
                };
                return extractor.ExtractMetadata(filePath, config);
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (meta is null)
        {
            ExtractError.Text = error ?? "Metadata extractor returned no result.";
            ExtractError.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrEmpty(meta.ID))
        {
            meta.ID = System.IO.Path.GetFileNameWithoutExtension(filePath);
        }

        var filenameArch = MetadataExtractor.DetectArchFromFilename(System.IO.Path.GetFileName(filePath));
        if (!string.IsNullOrEmpty(filenameArch))
        {
            meta.Architecture = filenameArch;
            meta.SupportedArch = [filenameArch];
        }

        _metadataBuffer = meta;
        RenderDetectedFacts(meta);
        await CheckTemplateMatchAsync(meta).ConfigureAwait(true);

        // Continue lights up once we have valid metadata; the user can now move to
        // Step 2 to edit it. Steps 3-5 are still pending (M4 follow-up).
        ContinueButton.IsEnabled = true;
    }

    private void RenderDetectedFacts(InstallerMetadata m)
    {
        AddFactRow("Name", m.ID);
        AddFactRow("Version", m.Version);
        AddFactRow("Developer", m.Developer);
        AddFactRow("Installer type", m.InstallerType);
        AddFactRow("Architecture", m.SupportedArch.Count > 0 ? string.Join(", ", m.SupportedArch) : "—");
        if (!string.IsNullOrEmpty(m.Description))
        {
            AddFactRow("Description", m.Description);
        }
        if (!string.IsNullOrEmpty(m.IdentityName))
        {
            AddFactRow("MSIX identity", m.IdentityName);
        }
        if (!string.IsNullOrEmpty(m.ProductCode))
        {
            AddFactRow("Product code", m.ProductCode);
        }
        if (!string.IsNullOrEmpty(m.UpgradeCode))
        {
            AddFactRow("Upgrade code", m.UpgradeCode);
        }
    }

    private void AddFactRow(string label, string? value)
    {
        var row = DetectedGrid.RowDefinitions.Count;
        DetectedGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var keyText = new TextBlock
        {
            Text = label,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        Grid.SetRow(keyText, row);
        Grid.SetColumn(keyText, 0);
        DetectedGrid.Children.Add(keyText);

        var valText = new TextBlock
        {
            Text = string.IsNullOrEmpty(value) ? "—" : value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        Grid.SetRow(valText, row);
        Grid.SetColumn(valText, 1);
        DetectedGrid.Children.Add(valText);
    }

    private async Task CheckTemplateMatchAsync(InstallerMetadata meta)
    {
        var all = await _packageService.GetAllPackagesAsync().ConfigureAwait(true);
        var candidates = all
            .Where(p => string.Equals(p.Name, meta.ID, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        _templateMatch = candidates[0];
        TemplateBanner.Title = $"Found existing item: {_templateMatch.Name} {_templateMatch.Version}";
        TemplateBanner.Message =
            $"This name already exists in the repo. Catalogs, scripts, blocking_applications, etc. can be inherited from the existing pkginfo. The filename-detected architecture ({string.Join(", ", meta.SupportedArch)}) will override the template's.";
        TemplateBanner.IsOpen = true;
    }

    private void OnUseTemplateClicked(object sender, RoutedEventArgs e)
    {
        _useTemplate = true;
        TemplateBanner.Severity = InfoBarSeverity.Success;
        TemplateBanner.Title = $"Using {_templateMatch?.Name} {_templateMatch?.Version} as template";
        TemplateBanner.Message = "Catalogs, scripts, blocking_applications inherited. You'll review every field in Step 2.";
        UseTemplateButton.IsEnabled = false;
        StartFreshButton.IsEnabled = true;
    }

    private void OnStartFreshClicked(object sender, RoutedEventArgs e)
    {
        _useTemplate = false;
        TemplateBanner.Severity = InfoBarSeverity.Informational;
        TemplateBanner.Title = $"Found existing item: {_templateMatch?.Name} {_templateMatch?.Version}";
        TemplateBanner.Message = "Starting fresh — no values inherited from the existing pkginfo.";
        UseTemplateButton.IsEnabled = true;
        StartFreshButton.IsEnabled = false;
    }

    private async void OnContinueClicked(object sender, RoutedEventArgs e)
    {
        // Commit edits from the current step before moving on, so a later Back
        // restores them rather than losing the user's typing.
        switch (_step)
        {
            case WizardStep.Review when _metadataBuffer is not null:
                await EnterStep2Async().ConfigureAwait(true);
                break;
            case WizardStep.EditMetadata:
                CommitStep2Edits();
                EnterStep3();
                break;
            case WizardStep.Scripts:
                CommitStep3Edits();
                EnterStep4();
                break;
            case WizardStep.LocationAndReview:
                await SaveFromStep4Async().ConfigureAwait(true);
                break;
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case WizardStep.EditMetadata:
                CommitStep2Edits();
                SetStep(WizardStep.Review);
                break;
            case WizardStep.Scripts:
                CommitStep3Edits();
                SetStep(WizardStep.EditMetadata);
                break;
            case WizardStep.LocationAndReview:
                SetStep(WizardStep.Scripts);
                break;
        }
    }

    private void OnRestartClicked(object sender, RoutedEventArgs e)
    {
        ResetToIdle();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        ResetToIdle();
    }

    private void ResetToIdle()
    {
        _metadataBuffer = null;
        _templateMatch = null;
        _useTemplate = false;
        foreach (var key in _scripts.Keys.ToList())
        {
            _scripts[key] = null;
        }
        WizardView.Visibility = Visibility.Collapsed;
        IdleView.Visibility = Visibility.Visible;
        StatusText.Text = string.Empty;
        SetStep(WizardStep.Review);
        ViewModel.Queue.Clear();
    }

    /// <summary>
    /// Swap visible step panels + relabel the indicator + adjust Back/Continue.
    /// Doesn't move metadata in or out — caller is responsible for that so a
    /// back-then-forward flow can restore previously-typed edits.
    /// </summary>
    private void SetStep(WizardStep step)
    {
        _step = step;
        Step1Panel.Visibility = step == WizardStep.Review ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = step == WizardStep.EditMetadata ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = step == WizardStep.Scripts ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = step == WizardStep.LocationAndReview ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = step == WizardStep.Review ? Visibility.Collapsed : Visibility.Visible;

        // Continue → Save when we land on the final step. Re-enabled in EnterStep4
        // after the YAML preview has rendered (it stays disabled if we can't build
        // a valid pkginfo yet).
        ContinueButton.Content = step == WizardStep.LocationAndReview ? "Save" : "Continue";

        WizardStepLabel.Text = step switch
        {
            WizardStep.Review => "Step 1 of 4 · Review",
            WizardStep.EditMetadata => "Step 2 of 4 · Edit metadata",
            WizardStep.Scripts => "Step 3 of 4 · Scripts",
            WizardStep.LocationAndReview => "Step 4 of 4 · Location and review",
            _ => "Wizard",
        };
    }

    /// <summary>
    /// First time we enter Step 2 we lazy-load catalog / package-name suggestions
    /// so the chip pickers feel the same as PackageEditor. Repeat entries (Back
    /// then forward) reuse the cached lists. Pre-fills every field from
    /// <c>_metadataBuffer</c> + <c>_templateMatch</c> (when "Use template" was
    /// selected in Step 1).
    /// </summary>
    private async Task EnterStep2Async()
    {
        if (_metadataBuffer is null) return;

        if (_knownCatalogs.Count == 0)
        {
            try
            {
                _knownCatalogs = await _catalogService.GetCatalogNamesAsync().ConfigureAwait(true);
            }
            catch
            {
                _knownCatalogs = [];
            }
        }

        if (_knownPackages.Count == 0)
        {
            try
            {
                var all = await _packageService.GetAllPackagesAsync().ConfigureAwait(true);
                _knownPackages = [.. all
                    .Select(p => p.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
            }
            catch
            {
                _knownPackages = [];
            }
        }

        CatalogsPicker.Suggestions = _knownCatalogs;
        BlockingPicker.Suggestions = _knownPackages;

        NameBox.Text = _metadataBuffer.ID ?? string.Empty;
        VersionBox.Text = _metadataBuffer.Version ?? string.Empty;
        DeveloperBox.Text = _metadataBuffer.Developer ?? string.Empty;
        CategoryBox.Text = _metadataBuffer.Category ?? string.Empty;
        DescriptionBox.Text = _metadataBuffer.Description ?? string.Empty;
        UnattendedInstallCheck.IsChecked = _metadataBuffer.UnattendedInstall;
        UnattendedUninstallCheck.IsChecked = _metadataBuffer.UnattendedUninstall;

        // Architecture combo follows the same encoding the picker tags carry —
        // "x64", "arm64", or "x64,arm64". Fall back to x64 when the extractor
        // couldn't pin it down.
        var archKey = _metadataBuffer.SupportedArch.Count switch
        {
            0 => "x64",
            1 => _metadataBuffer.SupportedArch[0],
            _ => "x64,arm64",
        };
        ArchCombo.SelectedIndex = archKey switch
        {
            "arm64" => 1,
            "x64,arm64" => 2,
            _ => 0,
        };

        // Catalogs / blocking apps: prefer the template's values when the user
        // chose "Use template" in Step 1; otherwise start with whatever the
        // installer's existing metadata had (typically empty for a fresh import).
        if (_useTemplate && _templateMatch is not null)
        {
            CatalogsPicker.SetItems(_templateMatch.Catalogs);
            BlockingPicker.SetItems(_templateMatch.BlockingApplications);
        }
        else
        {
            CatalogsPicker.SetItems(_metadataBuffer.Catalogs);
            BlockingPicker.SetItems(_metadataBuffer.BlockingApps ?? []);
        }

        SetStep(WizardStep.EditMetadata);
    }

    /// <summary>
    /// Reads Step 2 inputs back into <c>_metadataBuffer</c>. Idempotent — safe to
    /// call from Back, Continue, or before Cancel; Step 3 (scripts) will read the
    /// updated buffer.
    /// </summary>
    private void CommitStep2Edits()
    {
        if (_metadataBuffer is null) return;

        _metadataBuffer.ID = NameBox.Text.Trim();
        _metadataBuffer.Version = VersionBox.Text.Trim();
        _metadataBuffer.Developer = DeveloperBox.Text.Trim();
        _metadataBuffer.Category = CategoryBox.Text.Trim();
        _metadataBuffer.Description = DescriptionBox.Text.Trim();
        _metadataBuffer.UnattendedInstall = UnattendedInstallCheck.IsChecked == true;
        _metadataBuffer.UnattendedUninstall = UnattendedUninstallCheck.IsChecked == true;

        if (ArchCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _metadataBuffer.SupportedArch = tag.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            _metadataBuffer.Architecture = tag;
        }

        _metadataBuffer.Catalogs = CatalogsPicker.GetItems();
        _metadataBuffer.BlockingApps = BlockingPicker.GetItems();
    }

    /// <summary>
    /// Populates the six script editors from <c>_scripts</c> (or the matched
    /// template's scripts when the user picked "Use template" in Step 1). Empty
    /// slots stay empty so the YAML emitter omits them in Step 5.
    /// </summary>
    private void EnterStep3()
    {
        // Slot precedence: whatever the user already typed > template (when
        // _useTemplate is set) > empty.
        string Pick(string key, Func<Package, string?> fromTemplate)
        {
            if (!string.IsNullOrEmpty(_scripts[key])) return _scripts[key]!;
            if (_useTemplate && _templateMatch is not null)
            {
                return fromTemplate(_templateMatch) ?? string.Empty;
            }
            return string.Empty;
        }

        PreinstallEditor.ScriptText = Pick("preinstall", p => p.PreinstallScript);
        PostinstallEditor.ScriptText = Pick("postinstall", p => p.PostinstallScript);
        PreuninstallEditor.ScriptText = Pick("preuninstall", p => p.PreuninstallScript);
        PostuninstallEditor.ScriptText = Pick("postuninstall", p => p.PostuninstallScript);
        InstallCheckEditor.ScriptText = Pick("installcheck", p => p.InstallCheckScript);
        UninstallCheckEditor.ScriptText = Pick("uninstallcheck", p => p.UninstallCheckScript);

        SetStep(WizardStep.Scripts);
    }

    private void CommitStep3Edits()
    {
        _scripts["preinstall"] = NullIfEmpty(PreinstallEditor.ScriptText);
        _scripts["postinstall"] = NullIfEmpty(PostinstallEditor.ScriptText);
        _scripts["preuninstall"] = NullIfEmpty(PreuninstallEditor.ScriptText);
        _scripts["postuninstall"] = NullIfEmpty(PostuninstallEditor.ScriptText);
        _scripts["installcheck"] = NullIfEmpty(InstallCheckEditor.ScriptText);
        _scripts["uninstallcheck"] = NullIfEmpty(UninstallCheckEditor.ScriptText);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // ---- Step 4: location + review ----

    private string _sourceInstallerPath = string.Empty;

    /// <summary>
    /// Builds the Step 4 view: a subdir entry, computed paths, and a live YAML
    /// preview that mirrors what will land on disk if the user clicks Save.
    /// Subdir changes re-render the resolved-paths line on the fly.
    /// </summary>
    private void EnterStep4()
    {
        SaveStatusBar.IsOpen = false;
        SubdirBox.TextChanged -= OnSubdirChanged;
        SubdirBox.TextChanged += OnSubdirChanged;

        // Default the subdir to the template's location (if any) or empty.
        if (string.IsNullOrEmpty(SubdirBox.Text) && _useTemplate && _templateMatch?.FilePath is { } existingPath
            && _repositoryService.CurrentRepository is { } repo
            && existingPath.StartsWith(repo.PkgsInfoPath, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(repo.PkgsInfoPath, Path.GetDirectoryName(existingPath) ?? string.Empty);
            SubdirBox.Text = relative == "." ? string.Empty : relative.Replace('\\', '/');
        }

        UpdateStep4Preview();
        SetStep(WizardStep.LocationAndReview);
    }

    private void OnSubdirChanged(object sender, TextChangedEventArgs e) => UpdateStep4Preview();

    private void UpdateStep4Preview()
    {
        if (_metadataBuffer is null || _repositoryService.CurrentRepository is not { } repo)
        {
            ResolvedPathsText.Text = string.Empty;
            YamlPreviewText.Text = string.Empty;
            return;
        }

        var subdir = (SubdirBox.Text ?? string.Empty).Trim().Trim('/', '\\');
        var ext = Path.GetExtension(_sourceInstallerPath);
        var fileBase = $"{_metadataBuffer.ID}-{_metadataBuffer.Version}";
        var installerRel = string.IsNullOrEmpty(subdir)
            ? $"{fileBase}{ext}"
            : $"{subdir.Replace('\\', '/')}/{fileBase}{ext}";
        var pkginfoRel = string.IsNullOrEmpty(subdir)
            ? $"{fileBase}.yaml"
            : $"{subdir.Replace('\\', '/')}/{fileBase}.yaml";

        ResolvedPathsText.Text =
            $"pkginfo → pkgsinfo/{pkginfoRel}\ninstaller → pkgs/{installerRel}";

        // Live preview: synthesize a Package + serialize. Hash is unknown until Save
        // copies the file, so we placeholder it. Same for size — both are stamped on
        // write.
        var pkg = BuildPackageFromWizardState(installerRel, hash: "<computed on save>", size: 0);
        YamlPreviewText.Text = Infrastructure.Yaml.PackageYamlSerializer.Serialize(pkg);
    }

    /// <summary>
    /// Stitches the wizard's state into a <see cref="Package"/> ready for YAML
    /// serialization. Inheritance from <c>_templateMatch</c> applies to fields
    /// the wizard doesn't currently surface (Requires, UpdateFor, MinimumOsVersion,
    /// etc.) so a template-driven import preserves them.
    /// </summary>
    private Package BuildPackageFromWizardState(string installerRelativeLocation, string hash, long size)
    {
        var m = _metadataBuffer!;
        var pkg = new Package
        {
            Name = m.ID,
            Version = m.Version,
            Description = NullIfEmpty(m.Description),
            Developer = NullIfEmpty(m.Developer),
            Category = NullIfEmpty(m.Category),
            Catalogs = [.. m.Catalogs],
            SupportedArchitectures = m.SupportedArch.Count > 0 ? [.. m.SupportedArch] : null,
            UnattendedInstall = m.UnattendedInstall,
            UnattendedUninstall = m.UnattendedUninstall,
            BlockingApplications = m.BlockingApps,
            InstallerType = NullIfEmpty(m.InstallerType),
            Installer = new CimianAdmin.Core.Models.Packages.Installer
            {
                Type = NullIfEmpty(m.InstallerType),
                Location = installerRelativeLocation,
                Hash = hash,
                Size = size > 0 ? size : null,
                ProductCode = NullIfEmpty(m.ProductCode),
                UpgradeCode = NullIfEmpty(m.UpgradeCode),
                IdentityName = NullIfEmpty(m.IdentityName),
            },
            PreinstallScript = _scripts["preinstall"],
            PostinstallScript = _scripts["postinstall"],
            PreuninstallScript = _scripts["preuninstall"],
            PostuninstallScript = _scripts["postuninstall"],
            InstallCheckScript = _scripts["installcheck"],
            UninstallCheckScript = _scripts["uninstallcheck"],
        };

        // Inherit from the template for fields the wizard doesn't expose (yet).
        if (_useTemplate && _templateMatch is not null)
        {
            pkg.Requires = _templateMatch.Requires is { Count: > 0 } req ? [.. req] : null;
            pkg.UpdateFor = _templateMatch.UpdateFor is { Count: > 0 } uf ? [.. uf] : null;
            pkg.MinimumOsVersion = _templateMatch.MinimumOsVersion;
            pkg.MaximumOsVersion = _templateMatch.MaximumOsVersion;
            pkg.MinimumCimianVersion = _templateMatch.MinimumCimianVersion;
        }

        return pkg;
    }

    /// <summary>
    /// Performs the actual import: SHA-256 hashes the source installer, copies it
    /// into <c>pkgs/&lt;subdir&gt;/</c>, then writes the pkginfo YAML via the
    /// existing <see cref="IPackageService.CreatePackageAsync"/>. Surfaces a
    /// success or failure InfoBar at the bottom of Step 4.
    /// </summary>
    private async Task SaveFromStep4Async()
    {
        if (_metadataBuffer is null || _repositoryService.CurrentRepository is not { } repo)
        {
            SaveStatusBar.Severity = InfoBarSeverity.Error;
            SaveStatusBar.Title = "Save failed";
            SaveStatusBar.Message = "No repository is open.";
            SaveStatusBar.IsOpen = true;
            return;
        }

        ContinueButton.IsEnabled = false;
        BackButton.IsEnabled = false;

        try
        {
            var subdir = (SubdirBox.Text ?? string.Empty).Trim().Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            var ext = Path.GetExtension(_sourceInstallerPath);
            var fileBase = $"{_metadataBuffer.ID}-{_metadataBuffer.Version}";
            var installerTarget = string.IsNullOrEmpty(subdir)
                ? Path.Combine(repo.PkgsPath, $"{fileBase}{ext}")
                : Path.Combine(repo.PkgsPath, subdir, $"{fileBase}{ext}");

            Directory.CreateDirectory(Path.GetDirectoryName(installerTarget)!);

            // Stream the file through SHA-256 while copying so we only read it once.
            // Big installers (>200MB) would block the UI thread on Task.Run otherwise.
            var (hash, size) = await Task.Run(() => CopyAndHash(_sourceInstallerPath, installerTarget)).ConfigureAwait(true);

            var installerRel = Path.GetRelativePath(repo.PkgsPath, installerTarget).Replace('\\', '/');
            var pkg = BuildPackageFromWizardState(installerRel, hash, size);

            // Re-render the preview with the real hash/size before persisting.
            YamlPreviewText.Text = Infrastructure.Yaml.PackageYamlSerializer.Serialize(pkg);

            await _packageService.CreatePackageAsync(pkg, subdir.Replace(Path.DirectorySeparatorChar, '/')).ConfigureAwait(true);

            SaveStatusBar.Severity = InfoBarSeverity.Success;
            SaveStatusBar.Title = "Import complete";
            SaveStatusBar.Message = $"Wrote pkginfo and copied installer for {pkg.Name} {pkg.Version}.";
            SaveStatusBar.IsOpen = true;
            ContinueButton.IsEnabled = false; // import already happened — no double-save
        }
        catch (Exception ex)
        {
            SaveStatusBar.Severity = InfoBarSeverity.Error;
            SaveStatusBar.Title = "Save failed";
            SaveStatusBar.Message = ex.Message;
            SaveStatusBar.IsOpen = true;
            ContinueButton.IsEnabled = true;
        }
        finally
        {
            BackButton.IsEnabled = true;
        }
    }

    private static (string Hash, long Size) CopyAndHash(string source, string target)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var sourceStream = File.OpenRead(source);
        using var targetStream = File.Create(target);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = sourceStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            targetStream.Write(buffer, 0, read);
            sha.TransformBlock(buffer, 0, read, null, 0);
            total += read;
        }
        sha.TransformFinalBlock([], 0, 0);
        var hex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        return (hex, total);
    }
}
