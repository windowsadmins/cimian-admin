namespace CimianAdmin.ViewModels;

using System.Collections.ObjectModel;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Core.Models.Search;
using CimianAdmin.Core.Services;
using CimianAdmin.Models;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>How the Packages tree is partitioned.</summary>
public enum PackagesGroupBy
{
    Categories,
    Developers,
    /// <summary>Subdirectory under pkgsinfo/ — handy when packages are filed by team/folder.</summary>
    Directories,
    /// <summary>Smart buckets keyed off the installer / script slots a pkginfo carries.</summary>
    Types,
    /// <summary>Flat list, no grouping.</summary>
    None,
}

/// <summary>Order applied within each group bucket.</summary>
public enum PackagesSortBy
{
    Name,
    RecentlyModified,
    RecentlyCreated,
}

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

    [ObservableProperty]
    public partial PackagesGroupBy GroupBy { get; set; } = PackagesGroupBy.Categories;

    [ObservableProperty]
    public partial PackagesSortBy SortBy { get; set; } = PackagesSortBy.Name;

    /// <summary>
    /// Optional criteria predicate from the "Find packages" smart-search dialog.
    /// Null or empty = no predicate filter. Composes with SearchText (both must
    /// pass) so users can refine a typed search further.
    /// </summary>
    [ObservableProperty]
    public partial SmartSearchPredicate? SmartPredicate { get; set; }

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
    partial void OnGroupByChanged(PackagesGroupBy value) => ApplyFilter();
    partial void OnSortByChanged(PackagesSortBy value) => ApplyFilter();
    partial void OnSmartPredicateChanged(SmartSearchPredicate? value) => ApplyFilter();

    private void ApplyFilter()
    {
        var needle = SearchText?.Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrEmpty(needle);

        IEnumerable<Package> filtered = _all;
        if (hasSearch)
        {
            filtered = filtered.Where(p =>
                Contains(p.Name, needle) ||
                Contains(p.DisplayName, needle) ||
                Contains(p.Description, needle) ||
                Contains(p.Developer, needle) ||
                Contains(p.Category, needle));
        }

        if (SmartPredicate is { } pred && !pred.IsEmpty)
        {
            filtered = filtered.Where(p => PackageSmartFilter.Matches(p, pred));
        }

        var list = filtered.ToList();
        Packages = [.. list];
        RootNodes = [.. BuildTree(list, expandAll: hasSearch, GroupBy, SortBy, _repositoryService.CurrentRepository?.PkgsInfoPath)];
    }

    /// <summary>
    /// Builds the two-level tree per the current group + sort choice. Categories
    /// keep "(Uncategorized)" pinned to the bottom; Developers do the same with
    /// "(Unknown)"; Directories collapse to "(Top level)" for files directly in
    /// pkgsinfo/. Types is a multi-bucket view (a package appears in every
    /// bucket whose criterion it matches — Munki-style smart groups).
    /// </summary>
    private static List<PackageTreeNode> BuildTree(
        IEnumerable<Package> packages,
        bool expandAll,
        PackagesGroupBy groupBy,
        PackagesSortBy sortBy,
        string? pkgsInfoRoot)
    {
        var materialised = packages.ToList();

        if (groupBy == PackagesGroupBy.None)
        {
            var flat = new PackageTreeNode { Name = "All packages", IsExpanded = true };
            foreach (var pkg in ApplySort(materialised, sortBy))
            {
                flat.Children.Add(MakeLeaf(pkg));
            }
            return [flat];
        }

        if (groupBy == PackagesGroupBy.Types)
        {
            return BuildTypeBuckets(materialised, sortBy, expandAll);
        }

        // Categories / Developers / Directories share the "one bucket per key" shape.
        const string sentinel = "￿sentinel";
        var buckets = new SortedDictionary<string, List<Package>>(StringComparer.OrdinalIgnoreCase);

        foreach (var pkg in materialised)
        {
            var key = KeyFor(pkg, groupBy, pkgsInfoRoot);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = [];
                buckets[key] = bucket;
            }
            bucket.Add(pkg);
        }

        var orphanLabel = groupBy switch
        {
            PackagesGroupBy.Categories => "(Uncategorized)",
            PackagesGroupBy.Developers => "(Unknown developer)",
            PackagesGroupBy.Directories => "(Top level)",
            _ => "(Other)",
        };

        var roots = new List<PackageTreeNode>(buckets.Count);
        IEnumerable<KeyValuePair<string, List<Package>>> ordered = buckets
            .Where(kv => !string.Equals(kv.Key, sentinel, StringComparison.Ordinal))
            .Concat(buckets.Where(kv => string.Equals(kv.Key, sentinel, StringComparison.Ordinal)));

        foreach (var (key, bucket) in ordered)
        {
            var label = string.Equals(key, sentinel, StringComparison.Ordinal) ? orphanLabel : key;
            var node = new PackageTreeNode { Name = label, IsExpanded = expandAll };
            foreach (var pkg in ApplySort(bucket, sortBy))
            {
                node.Children.Add(MakeLeaf(pkg));
            }
            roots.Add(node);
        }

        return roots;
    }

    private static string KeyFor(Package pkg, PackagesGroupBy groupBy, string? pkgsInfoRoot)
    {
        const string sentinel = "￿sentinel";
        switch (groupBy)
        {
            case PackagesGroupBy.Categories:
                return string.IsNullOrWhiteSpace(pkg.Category) ? sentinel : pkg.Category!.Trim();
            case PackagesGroupBy.Developers:
                return string.IsNullOrWhiteSpace(pkg.Developer) ? sentinel : pkg.Developer!.Trim();
            case PackagesGroupBy.Directories:
                if (string.IsNullOrEmpty(pkg.FilePath) || string.IsNullOrEmpty(pkgsInfoRoot)) return sentinel;
                if (!pkg.FilePath.StartsWith(pkgsInfoRoot, StringComparison.OrdinalIgnoreCase)) return sentinel;
                var rel = Path.GetRelativePath(pkgsInfoRoot, pkg.FilePath);
                var dir = Path.GetDirectoryName(rel);
                return string.IsNullOrEmpty(dir) || dir == "." ? sentinel : dir.Replace(Path.DirectorySeparatorChar, '/');
            default:
                return sentinel;
        }
    }

    /// <summary>
    /// Smart groups based on what the pkginfo carries. A package can appear in
    /// multiple buckets — e.g. an MSI with a postinstall script lands in both
    /// "Has installer" and "Has scripts". Mirrors MunkiAdmin's Types view.
    /// </summary>
    private static List<PackageTreeNode> BuildTypeBuckets(
        List<Package> packages,
        PackagesSortBy sortBy,
        bool expandAll)
    {
        var rules = new (string Label, Func<Package, bool> Matches)[]
        {
            ("Has installer", p => p.Installer is not null),
            ("Has uninstaller", p => p.Uninstaller is { Count: > 0 } || !string.IsNullOrEmpty(p.UninstallerPath)),
            ("Has install_check", p => !string.IsNullOrEmpty(p.InstallCheckScript)),
            ("Has uninstall_check", p => !string.IsNullOrEmpty(p.UninstallCheckScript)),
            ("Has pre/post-install script", p => !string.IsNullOrEmpty(p.PreinstallScript) || !string.IsNullOrEmpty(p.PostinstallScript)),
            ("Has blocking applications", p => p.BlockingApplications is { Count: > 0 }),
            ("Unattended", p => p.UnattendedInstall || p.UnattendedUninstall),
        };

        var roots = new List<PackageTreeNode>(rules.Length);
        foreach (var (label, matches) in rules)
        {
            var matched = packages.Where(matches).ToList();
            if (matched.Count == 0) continue;
            var node = new PackageTreeNode { Name = $"{label} ({matched.Count})", IsExpanded = expandAll };
            foreach (var pkg in ApplySort(matched, sortBy))
            {
                node.Children.Add(MakeLeaf(pkg));
            }
            roots.Add(node);
        }
        return roots;
    }

    private static IEnumerable<Package> ApplySort(IEnumerable<Package> source, PackagesSortBy sortBy) => sortBy switch
    {
        PackagesSortBy.RecentlyModified => source.OrderByDescending(p => p.LastModified ?? DateTime.MinValue),
        PackagesSortBy.RecentlyCreated => source.OrderByDescending(p => p.Created ?? DateTime.MinValue),
        _ => source.OrderBy(p => p.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase),
    };

    private static PackageTreeNode MakeLeaf(Package pkg) => new()
    {
        Name = pkg.EffectiveDisplayName,
        Package = pkg,
    };

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
