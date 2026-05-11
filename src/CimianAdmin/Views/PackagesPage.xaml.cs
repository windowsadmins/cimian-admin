namespace CimianAdmin.Views;

using System.ComponentModel;
using System.Globalization;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Models;
using CimianAdmin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    // Segoe Fluent: Package (E7B8) for package leaves, Tag (E8EC) for category groups.
    public static string NodeGlyph(bool hasPackage) => hasPackage ? "\uE7B8" : "\uE8EC";

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync().ConfigureAwait(true);
        UpdateCount();
        SelectPending();
    }

    /// <summary>Apply any pending cross-page selection (set by NavigateToPackage or back/forward).</summary>
    public void SelectPending()
    {
        if (App.PendingPackageSelection is not { } pending) return;
        App.PendingPackageSelection = null;
        ViewModel.SelectedPackage = ViewModel.Packages.FirstOrDefault(
            p => string.Equals(p.FilePath, pending.FilePath, StringComparison.OrdinalIgnoreCase))
            ?? pending;
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
}
