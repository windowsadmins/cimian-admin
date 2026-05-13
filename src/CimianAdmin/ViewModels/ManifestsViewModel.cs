namespace CimianAdmin.ViewModels;

using System.Collections.ObjectModel;
using CimianAdmin.Core.Models.Manifests;
using CimianAdmin.Core.Models.Search;
using CimianAdmin.Core.Services;
using CimianAdmin.Models;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>How the Manifests tree is partitioned.</summary>
public enum ManifestsGroupBy
{
    /// <summary>Folder hierarchy derived from manifest name slashes (default).</summary>
    Directories,
    /// <summary>One bucket per catalog the manifest declares.</summary>
    Catalogs,
    /// <summary>Flat list, no grouping.</summary>
    None,
}

public enum ManifestsSortBy
{
    Name,
    RecentlyModified,
    RecentlyCreated,
}

public sealed partial class ManifestsViewModel : ObservableObject
{
    private readonly IManifestService _manifestService;
    private readonly IRepositoryService _repositoryService;
    private List<Manifest> _all = [];

    [ObservableProperty]
    public partial ObservableCollection<Manifest> Manifests { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<ManifestTreeNode> RootNodes { get; set; } = [];

    [ObservableProperty]
    public partial Manifest? SelectedManifest { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Optional catalog filter. When non-null/empty, manifests are excluded unless their
    /// own <c>catalogs</c> list contains this value. Empty string = "All catalogs".
    /// </summary>
    [ObservableProperty]
    public partial string CatalogFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial ManifestsGroupBy GroupBy { get; set; } = ManifestsGroupBy.Directories;

    [ObservableProperty]
    public partial ManifestsSortBy SortBy { get; set; } = ManifestsSortBy.Name;

    /// <summary>Optional criteria predicate from the "Find manifests" dialog.</summary>
    [ObservableProperty]
    public partial ManifestSearchPredicate? SmartPredicate { get; set; }

    public ManifestsViewModel(IManifestService manifestService, IRepositoryService repositoryService)
    {
        ArgumentNullException.ThrowIfNull(manifestService);
        ArgumentNullException.ThrowIfNull(repositoryService);
        _manifestService = manifestService;
        _repositoryService = repositoryService;
        _manifestService.ManifestsChanged += OnManifestsChanged;
    }

    public async Task LoadAsync()
    {
        if (_repositoryService.CurrentRepository is null)
        {
            _all = [];
            Manifests = [];
            RootNodes = [];
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var loaded = await _manifestService.GetAllManifestsAsync().ConfigureAwait(true);
            _all = [.. loaded];
            ApplyFilter();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _all = [];
            Manifests = [];
            RootNodes = [];
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCatalogFilterChanged(string value) => ApplyFilter();
    partial void OnGroupByChanged(ManifestsGroupBy value) => ApplyFilter();
    partial void OnSortByChanged(ManifestsSortBy value) => ApplyFilter();
    partial void OnSmartPredicateChanged(ManifestSearchPredicate? value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<Manifest> filtered = _all;
        var needle = SearchText?.Trim() ?? string.Empty;
        var hasSearch = !string.IsNullOrEmpty(needle);
        if (hasSearch)
        {
            filtered = filtered.Where(m =>
                Contains(m.Name, needle) ||
                Contains(m.DisplayName, needle) ||
                Contains(m.Notes, needle));
        }

        var catalog = CatalogFilter?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(catalog))
        {
            filtered = filtered.Where(m =>
                m.Catalogs is { Count: > 0 }
                && m.Catalogs.Any(c => string.Equals(c, catalog, StringComparison.OrdinalIgnoreCase)));
        }

        if (SmartPredicate is { } pred && !pred.IsEmpty)
        {
            filtered = filtered.Where(m => ManifestSmartFilter.Matches(m, pred));
        }

        var list = filtered.ToList();
        Manifests = [.. list];
        RootNodes = GroupBy switch
        {
            ManifestsGroupBy.Catalogs => [.. BuildCatalogTree(list, SortBy, hasSearch)],
            ManifestsGroupBy.None => [.. BuildFlatTree(list, SortBy)],
            _ => [.. ReorderDirectoryTree(BuildTree(list, expandAll: hasSearch), SortBy)],
        };
    }

    /// <summary>
    /// Walks the directory tree and reorders leaves at every level by the chosen
    /// sort. Folders-first is preserved; only the leaf order changes. For Name
    /// sort this is a no-op since BuildTree already sorts that way.
    /// </summary>
    private static List<ManifestTreeNode> ReorderDirectoryTree(List<ManifestTreeNode> roots, ManifestsSortBy sortBy)
    {
        if (sortBy == ManifestsSortBy.Name) return roots;
        foreach (var root in roots) ReorderNode(root, sortBy);
        return roots;
    }

    private static void ReorderNode(ManifestTreeNode node, ManifestsSortBy sortBy)
    {
        if (node.Children.Count > 1)
        {
            var folders = node.Children.Where(c => c.Children.Count > 0).ToList();
            var leaves = node.Children.Where(c => c.Children.Count == 0).ToList();
            var orderedLeaves = ApplySortNodes(leaves, sortBy);
            node.Children.Clear();
            foreach (var f in folders) node.Children.Add(f);
            foreach (var l in orderedLeaves) node.Children.Add(l);
        }
        foreach (var child in node.Children) ReorderNode(child, sortBy);
    }

    private static IEnumerable<ManifestTreeNode> ApplySortNodes(IEnumerable<ManifestTreeNode> nodes, ManifestsSortBy sortBy) => sortBy switch
    {
        ManifestsSortBy.RecentlyModified => nodes.OrderByDescending(n => n.Manifest?.LastModified ?? DateTime.MinValue),
        ManifestsSortBy.RecentlyCreated => nodes.OrderByDescending(n => n.Manifest?.Created ?? DateTime.MinValue),
        _ => nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase),
    };

    private static List<ManifestTreeNode> BuildFlatTree(IEnumerable<Manifest> manifests, ManifestsSortBy sortBy)
    {
        var all = new ManifestTreeNode { Name = "All manifests", FullPath = string.Empty, IsExpanded = true };
        foreach (var m in ApplySortManifests(manifests, sortBy))
        {
            all.Children.Add(new ManifestTreeNode
            {
                Name = m.Name ?? string.Empty,
                FullPath = m.Name ?? string.Empty,
                Manifest = m,
            });
        }
        return [all];
    }

    private static List<ManifestTreeNode> BuildCatalogTree(IEnumerable<Manifest> manifests, ManifestsSortBy sortBy, bool expandAll)
    {
        const string sentinel = "￿sentinel";
        var buckets = new SortedDictionary<string, List<Manifest>>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in manifests)
        {
            if (m.Catalogs is { Count: > 0 } cs)
            {
                foreach (var c in cs)
                {
                    if (string.IsNullOrWhiteSpace(c)) continue;
                    var key = c.Trim();
                    if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = [];
                    bucket.Add(m);
                }
            }
            else
            {
                if (!buckets.TryGetValue(sentinel, out var bucket)) buckets[sentinel] = bucket = [];
                bucket.Add(m);
            }
        }

        var roots = new List<ManifestTreeNode>(buckets.Count);
        IEnumerable<KeyValuePair<string, List<Manifest>>> ordered = buckets
            .Where(kv => !string.Equals(kv.Key, sentinel, StringComparison.Ordinal))
            .Concat(buckets.Where(kv => string.Equals(kv.Key, sentinel, StringComparison.Ordinal)));

        foreach (var (key, list) in ordered)
        {
            var label = string.Equals(key, sentinel, StringComparison.Ordinal) ? "(No catalogs)" : key;
            var node = new ManifestTreeNode { Name = label, FullPath = string.Empty, IsExpanded = expandAll };
            foreach (var m in ApplySortManifests(list, sortBy))
            {
                node.Children.Add(new ManifestTreeNode
                {
                    Name = m.Name ?? string.Empty,
                    FullPath = m.Name ?? string.Empty,
                    Manifest = m,
                });
            }
            roots.Add(node);
        }
        return roots;
    }

    private static IEnumerable<Manifest> ApplySortManifests(IEnumerable<Manifest> source, ManifestsSortBy sortBy) => sortBy switch
    {
        ManifestsSortBy.RecentlyModified => source.OrderByDescending(m => m.LastModified ?? DateTime.MinValue),
        ManifestsSortBy.RecentlyCreated => source.OrderByDescending(m => m.Created ?? DateTime.MinValue),
        _ => source.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Distinct catalog names referenced by any manifest, in preferred order.</summary>
    public List<string> GetKnownCatalogNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _all)
        {
            if (m.Catalogs is { Count: > 0 } cs)
            {
                foreach (var c in cs) if (!string.IsNullOrWhiteSpace(c)) set.Add(c);
            }
        }
        return [.. CimianAdmin.Models.CatalogOrdering.Sort(set)];
    }

    private static List<ManifestTreeNode> BuildTree(IEnumerable<Manifest> manifests, bool expandAll)
    {
        var roots = new SortedDictionary<string, ManifestTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifest in manifests)
        {
            var name = manifest.Name ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            ManifestTreeNode? parent = null;
            var path = string.Empty;
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                path = string.IsNullOrEmpty(path) ? segment : path + "/" + segment;
                var children = parent is null
                    ? roots
                    : null;

                ManifestTreeNode? node;
                if (children is not null)
                {
                    if (!children.TryGetValue(segment, out node))
                    {
                        node = new ManifestTreeNode
                        {
                            Name = segment,
                            FullPath = path,
                            IsExpanded = expandAll,
                        };
                        children[segment] = node;
                    }
                }
                else
                {
                    node = parent!.Children.FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase));
                    if (node is null)
                    {
                        node = new ManifestTreeNode
                        {
                            Name = segment,
                            FullPath = path,
                            IsExpanded = expandAll,
                        };
                        InsertSorted(parent.Children, node);
                    }
                }

                if (i == segments.Length - 1)
                {
                    node.Manifest = manifest;
                }

                parent = node;
            }
        }

        // Fold "node has both a manifest AND child manifests" into "folder with a self-leaf
        // inside it". Otherwise a Faculty.yaml that lives next to a Faculty/ folder gets
        // visually hidden by the folder node. We push the manifest down into a new leaf so
        // the folder is pure and the user can still see/click Faculty.yaml.
        foreach (var root in roots.Values)
        {
            DemoteFolderManifests(root);
        }

        // macOS-Finder "Keep folders on top" — folders before leaves at every level,
        // alphabetical within each group. Done as a post-pass so we don't have to thread
        // the comparison through the insertion logic above.
        var sortedRoots = SortFoldersFirst(roots.Values);
        foreach (var root in sortedRoots)
        {
            SortChildrenFoldersFirst(root);
        }

        return sortedRoots;
    }

    private static List<ManifestTreeNode> SortFoldersFirst(IEnumerable<ManifestTreeNode> nodes) =>
        [.. nodes
            .OrderBy(n => n.Children.Count > 0 ? 0 : 1)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)];

    private static void SortChildrenFoldersFirst(ManifestTreeNode node)
    {
        if (node.Children.Count > 1)
        {
            var sorted = SortFoldersFirst(node.Children);
            node.Children.Clear();
            foreach (var c in sorted)
            {
                node.Children.Add(c);
            }
        }
        foreach (var child in node.Children)
        {
            SortChildrenFoldersFirst(child);
        }
    }

    private static void DemoteFolderManifests(ManifestTreeNode node)
    {
        if (node.HasManifest && node.Children.Count > 0)
        {
            var manifest = node.Manifest;
            node.Manifest = null;

            var leaf = new ManifestTreeNode
            {
                Name = node.Name,
                FullPath = node.FullPath,
                Manifest = manifest,
                IsExpanded = false,
            };
            // Place the self-leaf first so it's obvious which manifest the folder is named after.
            node.Children.Insert(0, leaf);
        }

        foreach (var child in node.Children)
        {
            DemoteFolderManifests(child);
        }
    }

    private static void InsertSorted(ObservableCollection<ManifestTreeNode> children, ManifestTreeNode node)
    {
        var index = 0;
        while (index < children.Count
               && string.Compare(children[index].Name, node.Name, StringComparison.OrdinalIgnoreCase) < 0)
        {
            index++;
        }
        children.Insert(index, node);
    }

    private void OnManifestsChanged(object? sender, EventArgs e)
    {
        _ = LoadAsync();
    }

    private static bool Contains(string? haystack, string needle)
    {
        return !string.IsNullOrEmpty(haystack)
            && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
