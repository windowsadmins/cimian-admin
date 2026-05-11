namespace CimianAdmin.ViewModels;

using System.Collections.ObjectModel;
using CimianAdmin.Core.Models.Manifests;
using CimianAdmin.Core.Services;
using CimianAdmin.Models;
using CommunityToolkit.Mvvm.ComponentModel;

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

        var list = filtered.ToList();
        Manifests = [.. list];
        RootNodes = [.. BuildTree(list, expandAll: hasSearch)];
    }

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
