namespace CimianStudio.Views.Import;

using Cimian.CLI.Cimiimport.Models;
using Cimian.CLI.Cimiimport.Services;
using CimianStudio.Core.Models.Packages;
using CimianStudio.Core.Services;
using CimianStudio.Infrastructure.Import;
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
    /// Entry point for outside callers (e.g. the Packages tab forwarding a drop).
    /// Same dispatch as the in-tab drop / picker: one file auto-advances into the
    /// wizard, many files queue for batch import. The caller is expected to have
    /// already navigated to the Import tab before invoking this.
    /// </summary>
    public Task HandleExternalFilesAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return HandleSelectedFilesAsync([.. paths]);
    }

    /// <summary>
    /// Single file → auto-advance into Step 1 of the wizard (mirrors what a CLI
    /// invocation does after the first prompt: extract + show + ask).
    /// Multi-file → enqueue for batch import and reveal the queue panel.
    /// </summary>
    private async Task HandleSelectedFilesAsync(List<string> paths)
    {
        if (paths.Count == 0)
        {
            StatusText.Text = "No files in the drop (folders aren't supported yet).";
            return;
        }

        // Single-file fast path: skip the queue UI entirely.
        if (paths.Count == 1 && ViewModel.Queue.Count == 0)
        {
            await EnterWizardAsync(paths[0]).ConfigureAwait(true);
            return;
        }

        ViewModel.EnqueueFiles(paths);
        await ShowBatchQueueAsync().ConfigureAwait(true);
        StatusText.Text = $"Queue has {ViewModel.Queue.Count} item{(ViewModel.Queue.Count == 1 ? string.Empty : "s")} — review and click Process queue to import them all.";
    }

    /// <summary>
    /// Reveals the queue panel, loads catalog suggestions into the default-catalog
    /// combo (once), and ensures the combo has a usable default. Catalog list comes
    /// from <see cref="ICatalogService"/>, same source as the wizard's chip picker.
    /// </summary>
    private async Task ShowBatchQueueAsync()
    {
        QueuePanel.Visibility = Visibility.Visible;

        if (BatchCatalogCombo.Items.Count == 0)
        {
            try
            {
                var catalogs = await _catalogService.GetCatalogNamesAsync().ConfigureAwait(true);
                foreach (var c in catalogs)
                {
                    BatchCatalogCombo.Items.Add(c);
                }
            }
            catch
            {
                // ignored — IsEditable combo lets the user type a name anyway
            }

            // Default to Development if it exists, otherwise leave empty.
            var dev = BatchCatalogCombo.Items.OfType<string>()
                .FirstOrDefault(c => string.Equals(c, "Development", StringComparison.OrdinalIgnoreCase));
            if (dev is not null)
            {
                BatchCatalogCombo.SelectedItem = dev;
            }
        }
    }

    private void OnRemoveQueueItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            var item = ViewModel.Queue.FirstOrDefault(q => string.Equals(q.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (item is not null) ViewModel.Queue.Remove(item);
            if (ViewModel.Queue.Count == 0) QueuePanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClearQueueClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.Queue.Clear();
        QueuePanel.Visibility = Visibility.Collapsed;
        StatusText.Text = string.Empty;
        _batchImportedPaths = [];
        ProcessQueueButton.Visibility = Visibility.Visible;
        QueueOpenInGitButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Runs each Pending item through a non-interactive import: extract metadata,
    /// hash + copy the installer, write the pkginfo. Already-Done and Error items
    /// are skipped so retries don't double-write. Each row's status flips live
    /// thanks to <see cref="ImportViewModel.QueueItem"/> being observable.
    /// </summary>
    private async void OnProcessQueueClicked(object sender, RoutedEventArgs e)
    {
        if (_repositoryService.CurrentRepository is not { } repo)
        {
            StatusText.Text = "No repository is open.";
            return;
        }

        ProcessQueueButton.IsEnabled = false;
        ClearQueueButton.IsEnabled = false;

        var defaultCatalog = (BatchCatalogCombo.SelectedItem as string ?? BatchCatalogCombo.Text ?? string.Empty).Trim();
        var subdir = (BatchSubdirBox.Text ?? string.Empty).Trim().Trim('/', '\\');
        var useTemplate = BatchUseTemplateCheck.IsChecked == true;

        // Reset the batch-handoff tracker — each Process run owns its own set of
        // imported paths so retrying after errors gives a clean handoff list.
        _batchImportedPaths = [];

        try
        {
            foreach (var item in ViewModel.Queue.ToList())
            {
                if (item.Status is ImportViewModel.QueueItemStatus.Done or ImportViewModel.QueueItemStatus.InProgress)
                {
                    continue;
                }

                item.Status = ImportViewModel.QueueItemStatus.InProgress;
                item.StatusText = "Extracting metadata…";

                try
                {
                    var written = await ProcessQueueItemAsync(item, repo, defaultCatalog, subdir, useTemplate).ConfigureAwait(true);
                    _batchImportedPaths.AddRange(written);
                }
                catch (Exception ex)
                {
                    item.Status = ImportViewModel.QueueItemStatus.Error;
                    item.StatusText = ex.Message;
                }
            }

            var done = ViewModel.Queue.Count(q => q.Status == ImportViewModel.QueueItemStatus.Done);
            var failed = ViewModel.Queue.Count(q => q.Status == ImportViewModel.QueueItemStatus.Error);
            StatusText.Text = failed == 0
                ? $"Batch complete — imported {done} of {ViewModel.Queue.Count}."
                : $"Batch complete — {done} imported, {failed} failed (see status lines above).";

            // Show the Git hand-off button when at least one row imported. Hide
            // the Process button to keep the action area uncluttered.
            if (done > 0)
            {
                ProcessQueueButton.Visibility = Visibility.Collapsed;
                QueueOpenInGitButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            ProcessQueueButton.IsEnabled = true;
            ClearQueueButton.IsEnabled = true;
        }
    }

    // Absolute paths of pkginfo+installer files written by the most recent batch
    // run. Used by the "Commit batch in Git tab" handoff.
    private List<string> _batchImportedPaths = [];

    private async void OnQueueOpenInGitClicked(object sender, RoutedEventArgs e)
    {
        if (_batchImportedPaths.Count == 0 || App.MainWindowInstance is not { } window) return;

        var done = ViewModel.Queue.Count(q => q.Status == ImportViewModel.QueueItemStatus.Done);
        var subject = $"Import batch: {done} package{(done == 1 ? string.Empty : "s")} into repo";
        var body = string.Join('\n', ViewModel.Queue
            .Where(q => q.Status == ImportViewModel.QueueItemStatus.Done)
            .Select(q => $"- {q.FileName}"));

        window.NavigateTo("git");
        var gitPage = App.Resolve<GitPage>();
        await gitPage.PrepareCommitAsync(_batchImportedPaths, subject, body).ConfigureAwait(true);
    }

    /// <summary>
    /// Non-interactive import for one queue item: hands off to upstream
    /// <see cref="ImportService.ImportAsync"/> with batch-default values held
    /// in a <see cref="WinUIImportPrompter"/>. ImportService does the
    /// extraction, template lookup, hash, copy, and pkginfo write; this
    /// method just wires up the inputs and surfaces per-item status into the
    /// queue list view.
    /// </summary>
    /// <param name="useTemplate">When true and a name-match exists in
    /// <c>All.yaml</c>, ImportService inherits scripts / catalogs / blocking
    /// apps / Requires / OS bounds from it. When false, each pkginfo is built
    /// fresh from extractor data only.</param>
    private async Task<List<string>> ProcessQueueItemAsync(
        ImportViewModel.QueueItem item,
        CimianStudio.Core.Models.Repository.CimianRepository repo,
        string defaultCatalog,
        string subdir,
        bool useTemplate)
    {
        var filePath = item.FilePath;
        var subdirNormalized = (subdir ?? string.Empty).Trim().Trim('/', '\\').Replace('\\', '/');

        item.StatusText = "Extracting metadata…";

        // Pre-extract once so we know the output paths to return for the Git
        // hand-off. ImportService also extracts internally — small duplicate
        // cost, but cleaner than scanning disk after the write.
        var meta = await Task.Run(() =>
        {
            var extractor = new MetadataExtractor();
            return extractor.ExtractMetadata(filePath, new ImportConfiguration { RepoPath = repo.RootPath });
        }).ConfigureAwait(true);

        if (meta is null)
        {
            throw new InvalidOperationException("Metadata extractor returned no result.");
        }
        if (string.IsNullOrEmpty(meta.ID))
        {
            meta.ID = Path.GetFileNameWithoutExtension(filePath);
        }

        var filenameArch = MetadataExtractor.DetectArchFromFilename(Path.GetFileName(filePath));
        if (!string.IsNullOrEmpty(filenameArch))
        {
            meta.Architecture = filenameArch;
            meta.SupportedArch = [filenameArch];
        }
        if (!string.IsNullOrEmpty(defaultCatalog) && meta.Catalogs.Count == 0)
        {
            meta.Catalogs = [defaultCatalog];
        }

        // Capture any pre-existing _metadata BEFORE ImportService overwrites
        // (cimian-promoter / autopkg stamps would otherwise be lost — see
        // ReadExistingMetadataAsync's docstring).
        var (pkginfoAbsPre, _) = ComputeImportedPaths(repo, subdirNormalized, meta, filePath);
        var preservedMetadata = await Infrastructure.Yaml.PackageYaml.ReadExistingMetadataAsync(pkginfoAbsPre).ConfigureAwait(true);

        item.StatusText = "Importing…";

        // Batch inheritance is now user-controllable via BatchUseTemplateCheck.
        // Default-on matches the legacy code-behind; off gives each pkginfo a
        // fresh start from extractor data only.
        var prompter = new WinUIImportPrompter(
            useTemplate: useTemplate,
            editedMetadata: meta,
            subdir: "\\" + subdirNormalized,
            statusCallback: msg => DispatcherQueue.TryEnqueue(() => item.StatusText = msg));

        var importService = new ImportService();
        var success = await Task.Run(() => importService.ImportAsync(
            packagePath: filePath,
            config: new ImportConfiguration
            {
                RepoPath = repo.RootPath,
                DefaultCatalog = string.IsNullOrEmpty(defaultCatalog) ? "Development" : defaultCatalog,
                OpenImportedYaml = false,
            },
            scripts: new ScriptPaths(), // Empty -> ImportService falls back to the matched template's scripts.
            uninstallerPath: null,
            installsPaths: [],
            minOSVersion: null,
            maxOSVersion: null,
            minCimianVersion: null,
            extractIcon: false,
            iconOutputPath: null,
            noInteractive: true,
            prompter: prompter)).ConfigureAwait(true);

        if (!success)
        {
            throw new InvalidOperationException("ImportService reported failure.");
        }

        var (pkginfoAbs, installerAbs) = ComputeImportedPaths(repo, subdirNormalized, meta, filePath);

        // Restore the captured _metadata block. No-op if there was nothing to
        // preserve or if ImportService somehow wrote one itself.
        await Infrastructure.Yaml.PackageYaml.RestoreMetadataIfMissingAsync(pkginfoAbs, preservedMetadata).ConfigureAwait(true);

        var installerRel = Path.GetRelativePath(repo.PkgsPath, installerAbs).Replace('\\', '/');

        item.Status = ImportViewModel.QueueItemStatus.Done;
        item.StatusText = $"Imported → {installerRel}";

        return [pkginfoAbs, installerAbs];
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
        QueuePanel.Visibility = Visibility.Collapsed;
        _batchImportedPaths = [];
        ProcessQueueButton.Visibility = Visibility.Visible;
        QueueOpenInGitButton.Visibility = Visibility.Collapsed;
        OpenInGitButton.Visibility = Visibility.Collapsed;
        _lastImportedPaths = [];
        _lastImportedSubject = string.Empty;
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
    // Tracks the files written by the most recent Save so the "Commit in Git tab"
    // hand-off knows which rows to pre-select. Cleared on ResetToIdle.
    private List<string> _lastImportedPaths = [];
    private string _lastImportedSubject = string.Empty;

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
        YamlPreviewText.Text = Infrastructure.Yaml.PackageYaml.Serialize(pkg);
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
            Installer = new CimianStudio.Core.Models.Packages.Installer
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
    /// Hands the wizard's collected state off to upstream
    /// <see cref="ImportService.ImportAsync"/>. ImportService owns the disk
    /// I/O — file hash, installer copy, pkginfo serialize+write — so the
    /// canonical YAML form is the same one cimiimport / makecatalogs /
    /// manifestutil produce. WinUI just supplies the answers via
    /// <see cref="WinUIImportPrompter"/> and refreshes the preview after.
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
        SaveStatusBar.Severity = InfoBarSeverity.Informational;
        SaveStatusBar.Title = "Importing...";
        SaveStatusBar.Message = string.Empty;
        SaveStatusBar.IsOpen = true;

        var tempScriptPaths = new List<string>();
        try
        {
            var subdir = (SubdirBox.Text ?? string.Empty).Trim().Trim('/', '\\').Replace('\\', '/');

            // Capture any pre-existing _metadata BEFORE ImportService overwrites
            // the file (its PkgsInfo model has no Metadata field, so the block
            // would be lost otherwise). cimian-promoter / autopkg stamps go here.
            var (pkginfoAbsPre, _) = ComputeImportedPaths(repo, subdir, _metadataBuffer, _sourceInstallerPath);
            var preservedMetadata = await Infrastructure.Yaml.PackageYaml.ReadExistingMetadataAsync(pkginfoAbsPre).ConfigureAwait(true);

            // Materialize edited script content as temp files so ImportService
            // can pick them up via its file-path-based ScriptPaths interface.
            // Empty slots stay null -> ImportService either inherits from the
            // matched template (when _useTemplate is true) or omits the key.
            var scriptPaths = await WriteEditedScriptsToTempAsync(tempScriptPaths).ConfigureAwait(true);

            var prompter = new WinUIImportPrompter(
                useTemplate: _useTemplate,
                editedMetadata: _metadataBuffer,
                subdir: "\\" + subdir,
                statusCallback: msg => DispatcherQueue.TryEnqueue(() =>
                {
                    SaveStatusBar.Title = "Importing...";
                    SaveStatusBar.Message = msg;
                }));

            var importService = new ImportService();
            var success = await Task.Run(() => importService.ImportAsync(
                packagePath: _sourceInstallerPath,
                config: new ImportConfiguration
                {
                    RepoPath = repo.RootPath,
                    DefaultCatalog = _metadataBuffer.Catalogs.Count > 0 ? _metadataBuffer.Catalogs[0] : "Development",
                    OpenImportedYaml = false, // WinUI shows the result in-app, no editor spawn.
                },
                scripts: scriptPaths,
                uninstallerPath: null,
                installsPaths: [],
                // Template fields the wizard doesn't surface — pass through so the
                // pkginfo carries them when "Use template" was selected.
                minOSVersion: _useTemplate ? _templateMatch?.MinimumOsVersion : null,
                maxOSVersion: _useTemplate ? _templateMatch?.MaximumOsVersion : null,
                minCimianVersion: _useTemplate ? _templateMatch?.MinimumCimianVersion : null,
                extractIcon: false,
                iconOutputPath: null,
                noInteractive: false,
                prompter: prompter)).ConfigureAwait(true);

            if (!success)
            {
                SaveStatusBar.Severity = InfoBarSeverity.Error;
                SaveStatusBar.Title = "Save failed";
                SaveStatusBar.Message = "ImportService reported failure (see status above).";
                ContinueButton.IsEnabled = true;
                return;
            }

            var (pkginfoAbs, installerAbs) = ComputeImportedPaths(repo, subdir, _metadataBuffer, _sourceInstallerPath);
            _lastImportedPaths = [pkginfoAbs, installerAbs];
            _lastImportedSubject = $"Import of {_metadataBuffer.ID} {_metadataBuffer.Version} into repo";

            // Restore any _metadata block we captured before the overwrite.
            // No-op if the file already has one (don't double-stamp) or if
            // there was nothing to preserve in the first place.
            await Infrastructure.Yaml.PackageYaml.RestoreMetadataIfMissingAsync(pkginfoAbs, preservedMetadata).ConfigureAwait(true);

            // Refresh the preview from disk so what the user sees matches what
            // ImportService actually wrote (plus any restored _metadata block).
            if (File.Exists(pkginfoAbs))
            {
                YamlPreviewText.Text = await File.ReadAllTextAsync(pkginfoAbs).ConfigureAwait(true);
            }

            // ImportService bypassed PackageService so the event needs a manual nudge.
            _packageService.NotifyPackagesChanged();

            SaveStatusBar.Severity = InfoBarSeverity.Success;
            SaveStatusBar.Title = "Import complete";
            SaveStatusBar.Message = $"Wrote pkginfo and copied installer for {_metadataBuffer.ID} {_metadataBuffer.Version}.";
            OpenInGitButton.Visibility = Visibility.Visible;
            ContinueButton.IsEnabled = false; // import already happened — no double-save
        }
        catch (Exception ex)
        {
            SaveStatusBar.Severity = InfoBarSeverity.Error;
            SaveStatusBar.Title = "Save failed";
            SaveStatusBar.Message = ex.Message;
            ContinueButton.IsEnabled = true;
        }
        finally
        {
            BackButton.IsEnabled = true;
            foreach (var tmp in tempScriptPaths)
            {
                try { File.Delete(tmp); } catch (IOException) { /* cleanup is best-effort */ }
            }
        }
    }

    /// <summary>
    /// Writes any non-empty <c>_scripts</c> entries to temp files and returns a
    /// <see cref="ScriptPaths"/> pointing at them. Null entries stay null —
    /// ImportService treats those as "fall back to template if a name-match
    /// exists, otherwise omit".
    /// </summary>
    private async Task<ScriptPaths> WriteEditedScriptsToTempAsync(List<string> tempPaths)
    {
        return new ScriptPaths
        {
            Preinstall     = await WriteOneAsync("preinstall").ConfigureAwait(false),
            Postinstall    = await WriteOneAsync("postinstall").ConfigureAwait(false),
            Preuninstall   = await WriteOneAsync("preuninstall").ConfigureAwait(false),
            Postuninstall  = await WriteOneAsync("postuninstall").ConfigureAwait(false),
            InstallCheck   = await WriteOneAsync("installcheck").ConfigureAwait(false),
            UninstallCheck = await WriteOneAsync("uninstallcheck").ConfigureAwait(false),
        };

        async Task<string?> WriteOneAsync(string key)
        {
            if (string.IsNullOrEmpty(_scripts[key])) return null;
            var tmp = Path.Combine(Path.GetTempPath(), $"cs-import-{key}-{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(tmp, _scripts[key]).ConfigureAwait(false);
            tempPaths.Add(tmp);
            return tmp;
        }
    }

    /// <summary>
    /// Mirrors <c>ImportService.ImportAsync</c>'s output-path derivation
    /// (sanitized name + arch tag + version + extension under
    /// <c>pkgs/&lt;subdir&gt;/</c> and <c>pkgsinfo/&lt;subdir&gt;/</c>) so the
    /// Git hand-off knows which rows to pre-select. If ImportService's filename
    /// scheme ever drifts, this helper drifts with it — kept in sync via the
    /// canary tests in CimianStudio.Infrastructure.Tests.Yaml.
    /// </summary>
    private static (string PkginfoPath, string InstallerPath) ComputeImportedPaths(
        CimianStudio.Core.Models.Repository.CimianRepository repo,
        string subdir,
        InstallerMetadata metadata,
        string sourceInstallerPath)
    {
        var sanitizedName = MetadataExtractor.SanitizeName(metadata.ID);
        var archTag = metadata.SupportedArch.Count == 1
            ? $"-{metadata.SupportedArch[0].ToLowerInvariant()}-"
            : "-";
        var ext = Path.GetExtension(sourceInstallerPath);
        var subdirNormalized = subdir.Replace('/', Path.DirectorySeparatorChar);

        var installerFilename = $"{sanitizedName}{archTag}{metadata.Version}{ext}";
        var pkginfoFilename = $"{sanitizedName}{archTag}{metadata.Version}.yaml";

        var installerAbs = Path.Combine(repo.PkgsPath, subdirNormalized, installerFilename);
        var pkginfoAbs = Path.Combine(repo.PkgsInfoPath, subdirNormalized, pkginfoFilename);
        return (pkginfoAbs, installerAbs);
    }

    /// <summary>
    /// Navigates to the Git tab and asks it to pre-select the just-imported files
    /// (pkginfo + installer) and pre-fill the commit subject. The user reviews +
    /// clicks Commit / Commit & push themselves — we don't auto-commit because the
    /// user may want to amend, push, or tweak the message.
    /// </summary>
    private async void OnOpenInGitClicked(object sender, RoutedEventArgs e)
    {
        if (_lastImportedPaths.Count == 0 || App.MainWindowInstance is not { } window)
        {
            return;
        }

        window.NavigateTo("git");
        var gitPage = App.Resolve<GitPage>();
        await gitPage.PrepareCommitAsync(_lastImportedPaths, _lastImportedSubject).ConfigureAwait(true);
    }

}
