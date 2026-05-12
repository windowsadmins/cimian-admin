namespace CimianAdmin.Views;

using System.ComponentModel;
using System.Globalization;
using CimianAdmin.Core.Models.Manifests;
using CimianAdmin.Models;
using CimianAdmin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class ManifestsPage : Page
{
    public ManifestsViewModel ViewModel { get; }

    public ManifestsPage(ManifestsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // Segoe Fluent glyphs: Document (E8A5) for manifest leaves, FolderHorizontal (E8B7) for synthetic folders.
    public static Uri NodeIconUri(bool hasManifest) =>
        new(hasManifest ? "ms-appx:///Assets/file-text.svg" : "ms-appx:///Assets/folder.svg");

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync().ConfigureAwait(true);
        UpdateCount();
        PopulateCatalogFilter();
        SelectPending();
    }

    /// <summary>Apply any pending cross-page selection.</summary>
    public void SelectPending()
    {
        if (App.PendingManifestSelection is not { } pending) return;
        App.PendingManifestSelection = null;
        ViewModel.SelectedManifest = ViewModel.Manifests.FirstOrDefault(
            m => string.Equals(m.FilePath, pending.FilePath, StringComparison.OrdinalIgnoreCase))
            ?? pending;
    }

    private const string AllCatalogsLabel = "All catalogs";

    private void PopulateCatalogFilter()
    {
        var items = new List<string> { AllCatalogsLabel };
        items.AddRange(ViewModel.GetKnownCatalogNames());
        CatalogFilterBox.ItemsSource = items;
        CatalogFilterBox.SelectedIndex = 0;
    }

    private void OnCatalogFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        var picked = CatalogFilterBox.SelectedItem as string;
        ViewModel.CatalogFilter = string.Equals(picked, AllCatalogsLabel, StringComparison.Ordinal)
            ? string.Empty
            : (picked ?? string.Empty);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ManifestsViewModel.SelectedManifest):
                Editor.SetManifest(ViewModel.SelectedManifest);
                if (ViewModel.SelectedManifest is { } mf && App.MainWindowInstance is { } window)
                {
                    window.RecordSelection("manifests", mf);
                }
                break;
            case nameof(ManifestsViewModel.Manifests):
                UpdateCount();
                break;
        }
    }

    private void UpdateCount()
    {
        CountText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0} manifest{1}",
            ViewModel.Manifests.Count,
            ViewModel.Manifests.Count == 1 ? string.Empty : "s");
    }

    private void OnNodeInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not ManifestTreeNode node)
        {
            return;
        }

        if (node.HasManifest)
        {
            ViewModel.SelectedManifest = node.Manifest;
        }
        else if (node.Children.Count > 0)
        {
            // Branch click toggles expansion in addition to the chevron.
            node.IsExpanded = !node.IsExpanded;
        }
    }

    private void OnOpenInNewWindow(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string fullPath)
        {
            return;
        }

        var manifest = FindManifestByPath(fullPath);
        if (manifest is not null)
        {
            App.Resolve<ManifestEditorWindow>().Open(manifest);
        }
    }

    private Manifest? FindManifestByPath(string fullPath)
    {
        foreach (var manifest in ViewModel.Manifests)
        {
            if (string.Equals(manifest.Name, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return manifest;
            }
        }
        return null;
    }
}
