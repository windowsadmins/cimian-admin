namespace CimianAdmin.ViewModels;

using System.Collections.ObjectModel;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Core.Services;
using CimianAdmin.Models;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class PackagesViewModel : ObservableObject
{
    private readonly IPackageService _packageService;
    private readonly IRepositoryService _repositoryService;
    private List<Package> _all = [];

    [ObservableProperty]
    public partial ObservableCollection<Package> Packages { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<PackageTreeNode> RootNodes { get; set; } = [];

    [ObservableProperty]
    public partial Package? SelectedPackage { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public PackagesViewModel(IPackageService packageService, IRepositoryService repositoryService)
    {
        ArgumentNullException.ThrowIfNull(packageService);
        ArgumentNullException.ThrowIfNull(repositoryService);
        _packageService = packageService;
        _repositoryService = repositoryService;
        _packageService.PackagesChanged += OnPackagesChanged;
    }

    public async Task LoadAsync()
    {
        if (_repositoryService.CurrentRepository is null)
        {
            _all = [];
            Packages = [];
            RootNodes = [];
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var loaded = await _packageService.GetAllPackagesAsync().ConfigureAwait(true);
            _all = [.. loaded];
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = [];
            Packages = [];
            RootNodes = [];
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var needle = SearchText?.Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrEmpty(needle);

        IEnumerable<Package> filtered = _all;
        if (hasSearch)
        {
            filtered = _all.Where(p =>
                Contains(p.Name, needle) ||
                Contains(p.DisplayName, needle) ||
                Contains(p.Description, needle) ||
                Contains(p.Developer, needle) ||
                Contains(p.Category, needle));
        }

        var list = filtered.ToList();
        Packages = [.. list];
        RootNodes = [.. BuildTree(list, expandAll: hasSearch)];
    }

    private static List<PackageTreeNode> BuildTree(IEnumerable<Package> packages, bool expandAll)
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

        var roots = new List<PackageTreeNode>(byCategory.Count);

        // Place "(Uncategorized)" last to keep the alphabetic categories on top.
        IEnumerable<KeyValuePair<string, List<Package>>> ordered = byCategory
            .Where(kv => !string.Equals(kv.Key, uncategorized, StringComparison.Ordinal))
            .Concat(byCategory.Where(kv => string.Equals(kv.Key, uncategorized, StringComparison.Ordinal)));

        foreach (var (category, bucket) in ordered)
        {
            var node = new PackageTreeNode
            {
                Name = category,
                IsExpanded = expandAll,
            };
            foreach (var pkg in bucket.OrderBy(p => p.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                node.Children.Add(new PackageTreeNode
                {
                    Name = pkg.EffectiveDisplayName,
                    Package = pkg,
                });
            }
            roots.Add(node);
        }

        return roots;
    }

    private void OnPackagesChanged(object? sender, EventArgs e)
    {
        _ = LoadAsync();
    }

    private static bool Contains(string? haystack, string needle)
    {
        return !string.IsNullOrEmpty(haystack)
            && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
