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
        // Pull edits from the previous step (if any) before moving on, so Back can
        // restore them without losing the user's typing.
        if (_step == WizardStep.EditMetadata)
        {
            CommitStep2Edits();
            // Step 3 (scripts) is next — wired up in the M4 follow-up commit.
            return;
        }

        if (_step == WizardStep.Review && _metadataBuffer is not null)
        {
            await EnterStep2Async().ConfigureAwait(true);
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (_step == WizardStep.EditMetadata)
        {
            // Capture the user's edits so a forward jump after Back doesn't drop them.
            CommitStep2Edits();
            SetStep(WizardStep.Review);
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
        BackButton.Visibility = step == WizardStep.Review ? Visibility.Collapsed : Visibility.Visible;

        WizardStepLabel.Text = step switch
        {
            WizardStep.Review => "Step 1 of 4 · Review",
            WizardStep.EditMetadata => "Step 2 of 4 · Edit metadata",
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
}
