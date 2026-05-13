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
/// Cimiimport-native import wizard host. Idle view = drop-zone / file picker.
/// Single-file selection auto-advances to Step 1 (detected metadata + template
/// match banner). Multi-file selection enqueues for batch processing (M6).
/// Steps 2-5 land in M4.
/// </summary>
public sealed partial class ImportPage : Page
{
    private readonly IRepositoryService _repositoryService;
    private readonly IPackageService _packageService;
    // _metadataBuffer & _templateMatch carry forward into Step 2 (M4). _useTemplate
    // gates whether catalogs/scripts/blocking_applications get inherited from the
    // matched pkginfo when the M4 metadata form opens.
#pragma warning disable CS0414 // assigned but never used — read in M4 wizard steps
    private InstallerMetadata? _metadataBuffer;
    private Package? _templateMatch;
    private bool _useTemplate;
#pragma warning restore CS0414

    public ImportViewModel ViewModel { get; }

    public ImportPage(
        ImportViewModel viewModel,
        IRepositoryService repositoryService,
        IPackageService packageService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(repositoryService);
        ArgumentNullException.ThrowIfNull(packageService);
        ViewModel = viewModel;
        _repositoryService = repositoryService;
        _packageService = packageService;
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

        // M3 stops here — Continue moves to Step 2 in M4.
        ContinueButton.IsEnabled = false;
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

    private void OnContinueClicked(object sender, RoutedEventArgs e)
    {
        // Step 2 (metadata edit form) arrives in M4.
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
        ViewModel.Queue.Clear();
    }
}
