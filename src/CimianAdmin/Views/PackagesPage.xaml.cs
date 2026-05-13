namespace CimianAdmin.Views;

using System.ComponentModel;
using System.Globalization;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Models;
using CimianAdmin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

public sealed partial class PackagesPage : Page
{
    public PackagesViewModel ViewModel { get; }

    public PackagesPage(PackagesViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // Tree rows use TWO inline Paths (package + folder) with Visibility toggled by
    // these helpers — sidesteps XamlReader.Load on the hot virtualization path,
    // which crashed during tree materialization.
    public static Microsoft.UI.Xaml.Visibility VisIfPackage(bool hasPackage) =>
        hasPackage ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public static Microsoft.UI.Xaml.Visibility VisIfFolder(bool hasPackage) =>
        hasPackage ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Sync ComboBoxes from VM defaults before user interaction so the
        // SelectionChanged handlers see correct state on first fire.
        SyncGroupSortCombosFromViewModel();
        await ViewModel.LoadAsync().ConfigureAwait(true);
        UpdateCount();
        SelectPending();
    }

    private void SyncGroupSortCombosFromViewModel()
    {
        GroupByCombo.SelectedIndex = ViewModel.GroupBy switch
        {
            PackagesGroupBy.Categories => 0,
            PackagesGroupBy.Developers => 1,
            PackagesGroupBy.Directories => 2,
            PackagesGroupBy.Types => 3,
            _ => 4,
        };
        SortByCombo.SelectedIndex = ViewModel.SortBy switch
        {
            PackagesSortBy.Name => 0,
            PackagesSortBy.RecentlyModified => 1,
            _ => 2,
        };
    }

    private void OnGroupByChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GroupByCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<PackagesGroupBy>(tag, out var g))
        {
            ViewModel.GroupBy = g;
        }
    }

    private void OnSortByChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortByCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag
            && Enum.TryParse<PackagesSortBy>(tag, out var s))
        {
            ViewModel.SortBy = s;
        }
    }

    private async void OnFindClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new SmartSearchDialog(ViewModel.SmartPredicate)
        {
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
        if (dialog.Result is { } predicate)
        {
            ViewModel.SmartPredicate = predicate.IsEmpty ? null : predicate;
            UpdateFindButtonLabel();
        }
    }

    /// <summary>Reflects active filter count on the Find button so it's visible at a glance.</summary>
    private void UpdateFindButtonLabel()
    {
        var n = ViewModel.SmartPredicate?.Rules.Count ?? 0;
        FindButtonText.Text = n == 0
            ? "Find"
            : string.Format(CultureInfo.InvariantCulture, "Find ({0})", n);
    }

    /// <summary>Apply any pending cross-page selection (set by NavigateToPackage or back/forward).</summary>
    public void SelectPending()
    {
        if (App.PendingPackageSelection is not { } pending) return;
        App.PendingPackageSelection = null;

        // Prefer matching by FilePath; fall back to name+version because callers
        // like the Catalog tree pass packages reconstructed from catalog YAML which
        // have no FilePath. Using `?? pending` as a last resort would land us in a
        // save-broken editor (Package.FilePath is required before save).
        var live = ViewModel.Packages.FirstOrDefault(p =>
            !string.IsNullOrEmpty(pending.FilePath) &&
            string.Equals(p.FilePath, pending.FilePath, StringComparison.OrdinalIgnoreCase));
        live ??= ViewModel.Packages.FirstOrDefault(p =>
            string.Equals(p.Name, pending.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Version ?? string.Empty, pending.Version ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        live ??= ViewModel.Packages.FirstOrDefault(p =>
            string.Equals(p.Name, pending.Name, StringComparison.OrdinalIgnoreCase));
        ViewModel.SelectedPackage = live ?? pending;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PackagesViewModel.SelectedPackage):
                Editor.SetPackage(ViewModel.SelectedPackage);
                if (ViewModel.SelectedPackage is { } pkg && App.MainWindowInstance is { } window)
                {
                    window.RecordSelection("packages", pkg);
                }
                break;
            case nameof(PackagesViewModel.Packages):
                UpdateCount();
                break;
        }
    }

    private void UpdateCount()
    {
        CountText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0} package{1}",
            ViewModel.Packages.Count,
            ViewModel.Packages.Count == 1 ? string.Empty : "s");
    }

    private void OnNodeInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not PackageTreeNode node)
        {
            return;
        }

        if (node.Package is not null)
        {
            ViewModel.SelectedPackage = node.Package;
        }
        else if (node.Children.Count > 0)
        {
            // Category click toggles expansion in addition to the chevron.
            node.IsExpanded = !node.IsExpanded;
        }
    }

    private void OnOpenInNewWindow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.DataContext is PackageTreeNode node
            && node.Package is Package package)
        {
            App.Resolve<PackageEditorWindow>().Open(package);
        }
    }

    /// <summary>
    /// Accept installer files dropped anywhere on the Packages page so the user
    /// doesn't have to switch to the Import tab first. We only opt in for
    /// StorageItems so dragging a row inside the page doesn't accidentally
    /// trigger import.
    /// </summary>
    private void OnInstallerDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Start import wizard";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
        }
    }

    private async void OnInstallerDrop(object sender, DragEventArgs e)
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
        if (paths.Count == 0) return;

        // Bounce to the Import tab and let its handler do the wizard entry.
        // This keeps the dispatch logic in one place — the same code path runs
        // whether files arrive via the Import drop zone, the picker, or here.
        if (App.MainWindowInstance is { } window)
        {
            window.NavigateTo("import");
            var importPage = App.Resolve<Views.Import.ImportPage>();
            await importPage.HandleExternalFilesAsync(paths).ConfigureAwait(true);
        }
    }
}
