namespace CimianAdmin.Views;

using System.ComponentModel;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Models;
using CimianAdmin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class CatalogsPage : Page
{
    public CatalogsViewModel ViewModel { get; }

    public CatalogsPage(CatalogsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync().ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CatalogsViewModel.SelectedCatalog):
                RefreshTree();
                break;
            case nameof(CatalogsViewModel.RebuildStatus):
                if (!string.IsNullOrEmpty(ViewModel.RebuildStatus))
                {
                    StatusBar.Title = ViewModel.RebuildStatus;
                    StatusBar.IsOpen = true;
                }
                break;
        }
    }

    private void OnPackageSearchChanged(object sender, TextChangedEventArgs e) => RefreshTree();

    private void RefreshTree()
    {
        if (ViewModel.SelectedCatalog is not { Packages: { Count: > 0 } pkgs })
        {
            PackagesTree.ItemsSource = null;
            return;
        }

        var needle = PackageSearchBox?.Text?.Trim() ?? string.Empty;
        IEnumerable<Package> filtered = pkgs;
        if (!string.IsNullOrEmpty(needle))
        {
            filtered = pkgs.Where(p =>
                Contains(p.Name, needle)
                || Contains(p.DisplayName, needle)
                || Contains(p.Category, needle));
        }
        PackagesTree.ItemsSource = BuildCategoryTree(filtered);
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    // Group by Category, then collapse same-named packages under a single
    // expandable parent that lists each version as a child.
    private static List<PackageTreeNode> BuildCategoryTree(IEnumerable<Package> packages)
    {
        const string uncategorized = "(Uncategorized)";

        var byCategory = new SortedDictionary<string, List<Package>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pkg in packages)
        {
            var cat = string.IsNullOrWhiteSpace(pkg.Category) ? uncategorized : pkg.Category!.Trim();
            if (!byCategory.TryGetValue(cat, out var bucket))
            {
                bucket = [];
                byCategory[cat] = bucket;
            }
            bucket.Add(pkg);
        }

        IEnumerable<KeyValuePair<string, List<Package>>> ordered = byCategory
            .Where(kv => !string.Equals(kv.Key, uncategorized, StringComparison.Ordinal))
            .Concat(byCategory.Where(kv => string.Equals(kv.Key, uncategorized, StringComparison.Ordinal)));

        var roots = new List<PackageTreeNode>(byCategory.Count);
        foreach (var (category, bucket) in ordered)
        {
            var node = new PackageTreeNode
            {
                Name = category,
                IsExpanded = true,
            };

            // Group same-name entries under one parent. Single-version names
            // stay flat as a leaf; multi-version names get a folder-like group.
            var byDisplayName = bucket
                .GroupBy(p => p.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

            foreach (var nameGroup in byDisplayName)
            {
                var versions = nameGroup.ToList();
                if (versions.Count == 1)
                {
                    node.Children.Add(new PackageTreeNode
                    {
                        Name = nameGroup.Key,
                        Package = versions[0],
                    });
                    continue;
                }

                var parent = new PackageTreeNode
                {
                    Name = $"{nameGroup.Key} ({versions.Count} versions)",
                    IsExpanded = false,
                };
                // Version-aware sort: compare numeric segments numerically (so "10" > "9"),
                // falling back to ordinal compare on any non-numeric tail. Cimian versions
                // look like "2026.05.11.0458" or "1.2.3" so dotted-numeric is the common case.
                foreach (var pkg in versions.OrderByDescending(p => p.Version, VersionComparer.Instance))
                {
                    parent.Children.Add(new PackageTreeNode
                    {
                        Name = pkg.Version ?? string.Empty,
                        Package = pkg,
                    });
                }
                node.Children.Add(parent);
            }
            roots.Add(node);
        }
        return roots;
    }

    private void OnNodeInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        _ = ViewModel; // touch instance state so CA1822 is satisfied; XAML requires instance handler
        if (args.InvokedItem is not PackageTreeNode node) return;
        if (node.Package is { } pkg && App.MainWindowInstance is { } window)
        {
            window.NavigateToPackage(pkg);
        }
        else if (node.Children.Count > 0)
        {
            node.IsExpanded = !node.IsExpanded;
        }
    }

    private async void OnRebuildClicked(object sender, RoutedEventArgs e)
    {
        // makecatalogs reads pkginfo files from disk, so unsaved edits won't be reflected.
        // Make that explicit before kicking off the rebuild.
        var dialog = new ContentDialog
        {
            Title = "Rebuild catalogs",
            Content = "Catalogs are regenerated from the pkginfo files on disk. Any unsaved changes in open editors won't appear in the new catalogs — save first if you need them included.",
            PrimaryButtonText = "Rebuild",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        await ViewModel.RebuildAsync().ConfigureAwait(true);
    }

    private async void OnCompareClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Catalogs is null || ViewModel.Catalogs.Count < 2)
        {
            StatusBar.Title = "Need at least two catalogs to compare.";
            StatusBar.IsOpen = true;
            return;
        }

        var dialog = new CatalogCompareDialog { XamlRoot = XamlRoot };
        dialog.SetCatalogs([.. ViewModel.Catalogs], preferredA: ViewModel.SelectedCatalog);
        await dialog.ShowAsync();
    }

    private void OnOpenPackageInNewWindow(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.DataContext is PackageTreeNode node
            && node.Package is { } package)
        {
            App.Resolve<PackageEditorWindow>().Open(package);
        }
    }

    private void OnOpenPackageInMain(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.DataContext is PackageTreeNode node
            && node.Package is { } package
            && App.MainWindowInstance is { } window)
        {
            window.NavigateToPackage(package);
        }
    }

    /// <summary>
    /// Compares dotted-numeric version strings segment-by-segment. "10" beats "9",
    /// "2026.05.11" beats "2026.05.09", and non-numeric tails fall back to ordinal
    /// compare. Null/empty sorts last.
    /// </summary>
    private sealed class VersionComparer : IComparer<string?>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 0;
            if (string.IsNullOrEmpty(a)) return -1;
            if (string.IsNullOrEmpty(b)) return 1;

            var aParts = a.Split('.', '-');
            var bParts = b.Split('.', '-');
            var len = Math.Min(aParts.Length, bParts.Length);
            for (int i = 0; i < len; i++)
            {
                var aNum = int.TryParse(aParts[i], out var an);
                var bNum = int.TryParse(bParts[i], out var bn);
                if (aNum && bNum)
                {
                    if (an != bn) return an.CompareTo(bn);
                }
                else
                {
                    var cmp = string.Compare(aParts[i], bParts[i], StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp;
                }
            }
            return aParts.Length.CompareTo(bParts.Length);
        }
    }
}
