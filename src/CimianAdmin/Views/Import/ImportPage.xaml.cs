namespace CimianAdmin.Views.Import;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

/// <summary>
/// Cimiimport-native import wizard host. Skeleton in M2; full multi-step wizard
/// arrives in M3-M4. The page accepts drag-drop AND a manual file picker so the
/// queue can be seeded either way.
/// </summary>
public sealed partial class ImportPage : Page
{
    public ImportViewModel ViewModel { get; }

    public ImportPage(ImportViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        // Accept files; show the system Copy/Move overlay so users get a hint that
        // a drop will actually do something here.
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

        if (paths.Count == 0)
        {
            StatusText.Text = "No files in the drop (folders aren't supported yet).";
            return;
        }

        ViewModel.EnqueueFiles(paths);
        StatusText.Text = $"Queued {paths.Count} file{(paths.Count == 1 ? string.Empty : "s")}. Wizard UI arrives in the next milestone.";
    }

    private async void OnPickFileClicked(object sender, RoutedEventArgs e)
    {
        // FileOpenPicker in unpackaged WinUI 3 needs the parent window HWND wired
        // up via WinRT interop before ShowAsync, otherwise it throws.
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

        var paths = files.Select(f => f.Path).ToList();
        ViewModel.EnqueueFiles(paths);
        StatusText.Text = $"Queued {paths.Count} file{(paths.Count == 1 ? string.Empty : "s")}. Wizard UI arrives in the next milestone.";
    }
}
