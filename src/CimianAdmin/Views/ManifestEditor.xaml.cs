namespace CimianAdmin.Views;

using System.Text;
using CimianAdmin.Core.Models.Manifests;
using CimianAdmin.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ContextOption = CimianAdmin.Views.ContextualChipList.ContextOption;
using ChipEntry = CimianAdmin.Views.ContextualChipList.ChipEntry;

public sealed partial class ManifestEditor : UserControl
{
    private readonly IManifestService _manifestService;
    private readonly ICatalogService _catalogService;
    private readonly IPackageService _packageService;
    private Manifest? _manifest;
    private bool _suppressDirty;
    private IReadOnlyList<string> _knownCatalogs = [];
    private IReadOnlyList<string> _knownPackageNames = [];
    private IReadOnlyList<string> _knownManifestNames = [];

    public ManifestEditor()
        : this(App.Resolve<IManifestService>(), App.Resolve<ICatalogService>(), App.Resolve<IPackageService>())
    {
    }

    public ManifestEditor(IManifestService manifestService, ICatalogService catalogService, IPackageService packageService)
    {
        ArgumentNullException.ThrowIfNull(manifestService);
        ArgumentNullException.ThrowIfNull(catalogService);
        ArgumentNullException.ThrowIfNull(packageService);
        _manifestService = manifestService;
        _catalogService = catalogService;
        _packageService = packageService;
        InitializeComponent();
    }

    public bool IsDirty
    {
        get;
        private set
        {
            field = value;
            DirtyIndicator.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            SaveButton.IsEnabled = value && _manifest is not null;
            RevertButton.IsEnabled = value && _manifest is not null;
        }
    }

    public async void SetManifest(Manifest? manifest)
    {
        _manifest = manifest;

        if (manifest is null)
        {
            EditorRoot.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EditorRoot.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        try
        {
            _knownCatalogs = await _catalogService.GetCatalogNamesAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            _knownCatalogs = [];
        }

        try
        {
            var allPackages = await _packageService.GetAllPackagesAsync().ConfigureAwait(true);
            _knownPackageNames = [.. allPackages
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception)
        {
            _knownPackageNames = [];
        }

        try
        {
            var allManifests = await _manifestService.GetAllManifestsAsync().ConfigureAwait(true);
            _knownManifestNames = [.. allManifests
                .Select(m => m.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception)
        {
            _knownManifestNames = [];
        }

        ManagedInstallsPicker.Suggestions = _knownPackageNames;
        ManagedUninstallsPicker.Suggestions = _knownPackageNames;
        ManagedUpdatesPicker.Suggestions = _knownPackageNames;
        OptionalInstallsPicker.Suggestions = _knownPackageNames;
        DefaultInstallsPicker.Suggestions = _knownPackageNames;
        IncludedPicker.Suggestions = _knownManifestNames;

        Populate(manifest);
        IsDirty = false;
        StatusBar.IsOpen = false;
    }

    private void Populate(Manifest manifest)
    {
        _suppressDirty = true;
        try
        {
            DisplayNameText.Text = string.IsNullOrWhiteSpace(manifest.DisplayName)
                ? manifest.Name ?? string.Empty
                : manifest.DisplayName!;
            NameText.Text = manifest.Name ?? string.Empty;
            FilePathText.Text = ToRepoRelativePath(manifest.FilePath);

            DisplayNameField.Text = manifest.DisplayName ?? string.Empty;
            BuildCatalogChecklist(manifest.Catalogs);
            NotesField.Text = manifest.Notes ?? string.Empty;

            // Build per-picker context lists. A conditional only counts as a possible
            // target for a given picker if it actually defines that field (selector returns
            // non-null). This way Bootstrap.yaml — which has conditional_items: with empty
            // managed_installs bodies and NO managed_apps — shows the conditional dropdown
            // only on the managed_installs picker, not on every chip everywhere.
            var miCtx = BuildContextsForField(manifest.ConditionalItems, c => c.ManagedInstalls);
            var muCtx = BuildContextsForField(manifest.ConditionalItems, c => c.ManagedUninstalls);
            var mupCtx = BuildContextsForField(manifest.ConditionalItems, c => c.ManagedUpdates);
            var optCtx = BuildContextsForField(manifest.ConditionalItems, c => c.OptionalInstalls);
            var incCtx = BuildContextsForField(manifest.ConditionalItems, c => c.IncludedManifests);

            ManagedInstallsPicker.SetContexts(miCtx);
            ManagedUninstallsPicker.SetContexts(muCtx);
            ManagedUpdatesPicker.SetContexts(mupCtx);
            OptionalInstallsPicker.SetContexts(optCtx);
            IncludedPicker.SetContexts(incCtx);

            var anyManagedConditional = miCtx.Count > 1 || muCtx.Count > 1 || mupCtx.Count > 1 || optCtx.Count > 1;
            ManagedConditionalHeader.Visibility = anyManagedConditional ? Visibility.Visible : Visibility.Collapsed;
            IncludedHeaderRow.Visibility = incCtx.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            ManagedInstallsPicker.SetItems(CollectChipEntries(manifest.ConditionalItems, manifest.ManagedInstalls, c => c.ManagedInstalls));
            ManagedUninstallsPicker.SetItems(CollectChipEntries(manifest.ConditionalItems, manifest.ManagedUninstalls, c => c.ManagedUninstalls));
            ManagedUpdatesPicker.SetItems(CollectChipEntries(manifest.ConditionalItems, manifest.ManagedUpdates, c => c.ManagedUpdates));
            OptionalInstallsPicker.SetItems(CollectChipEntries(manifest.ConditionalItems, manifest.OptionalInstalls, c => c.OptionalInstalls));
            IncludedPicker.SetItems(CollectChipEntries(manifest.ConditionalItems, manifest.IncludedManifests, c => c.IncludedManifests));
            DefaultInstallsPicker.SetItems(manifest.DefaultInstalls);
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void BuildCatalogChecklist(IReadOnlyCollection<string>? selected)
    {
        CatalogsContainer.Children.Clear();
        // YAML files commonly emit `catalogs:` with no value, which YamlDotNet binds to
        // null even though the property's default is []. Treat null as "no catalogs
        // selected" so we don't NRE and abort the rest of Populate.
        selected ??= [];

        var union = new HashSet<string>(_knownCatalogs, StringComparer.OrdinalIgnoreCase);
        foreach (var c in selected)
        {
            if (!string.IsNullOrWhiteSpace(c) && !string.Equals(c, "All", StringComparison.OrdinalIgnoreCase))
            {
                union.Add(c);
            }
        }

        var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        foreach (var name in CimianAdmin.Models.CatalogOrdering.Sort(union))
        {
            var box = new CheckBox
            {
                Content = name,
                IsChecked = selectedSet.Contains(name),
                Tag = name,
                MinWidth = 120,
            };
            box.Click += OnFieldChanged;
            CatalogsContainer.Children.Add(box);
        }
    }

    private List<string> ReadCatalogs()
    {
        var result = new List<string>();
        foreach (var child in CatalogsContainer.Children)
        {
            if (child is CheckBox box && box.IsChecked == true && box.Tag is string name)
            {
                result.Add(name);
            }
        }
        return result;
    }

    private void OnFieldChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressDirty)
        {
            return;
        }
        IsDirty = true;
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_manifest is null)
        {
            return;
        }

        ApplyEditsTo(_manifest);

        try
        {
            await _manifestService.SaveManifestAsync(_manifest).ConfigureAwait(true);
            IsDirty = false;
            ShowStatus(InfoBarSeverity.Success, "Saved", $"Wrote {_manifest.FilePath}");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Save failed", ex.Message);
        }
    }

    private void OnRevertClicked(object sender, RoutedEventArgs e)
    {
        if (_manifest is null)
        {
            return;
        }
        Populate(_manifest);
        IsDirty = false;
        StatusBar.IsOpen = false;
    }

    private async void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (_manifest is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete this manifest?",
            Content = $"This permanently deletes:\n\n{_manifest.FilePath}",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _manifestService.DeleteManifestAsync(_manifest).ConfigureAwait(true);
            _manifest = null;
            EditorRoot.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            ShowStatus(InfoBarSeverity.Success, "Deleted", "Manifest removed.");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Delete failed", ex.Message);
        }
    }

    private void ApplyEditsTo(Manifest manifest)
    {
        manifest.DisplayName = NullIfBlank(DisplayNameField.Text);
        manifest.Catalogs = ReadCatalogs();
        manifest.Notes = NullIfBlank(NotesField.Text);
        manifest.DefaultInstalls = NullIfEmpty(DefaultInstallsPicker.GetItems());

        // For each managed list, walk the chip entries and write them either to the
        // top-level manifest or to the conditional item identified by ContextId. Lists
        // are cleared first so deletions stick.
        ClearAllManagedLists(manifest);
        DistributeChipEntries(manifest, ManagedInstallsPicker.GetItems(),
            top => manifest.ManagedInstalls = AppendOrCreate(manifest.ManagedInstalls, top),
            (cond, name) => cond.ManagedInstalls = AppendOrCreate(cond.ManagedInstalls, name));
        DistributeChipEntries(manifest, ManagedUninstallsPicker.GetItems(),
            top => manifest.ManagedUninstalls = AppendOrCreate(manifest.ManagedUninstalls, top),
            (cond, name) => cond.ManagedUninstalls = AppendOrCreate(cond.ManagedUninstalls, name));
        DistributeChipEntries(manifest, ManagedUpdatesPicker.GetItems(),
            top => manifest.ManagedUpdates = AppendOrCreate(manifest.ManagedUpdates, top),
            (cond, name) => cond.ManagedUpdates = AppendOrCreate(cond.ManagedUpdates, name));
        DistributeChipEntries(manifest, OptionalInstallsPicker.GetItems(),
            top => manifest.OptionalInstalls = AppendOrCreate(manifest.OptionalInstalls, top),
            (cond, name) => cond.OptionalInstalls = AppendOrCreate(cond.OptionalInstalls, name));
        DistributeChipEntries(manifest, IncludedPicker.GetItems(),
            top => manifest.IncludedManifests = AppendOrCreate(manifest.IncludedManifests, top),
            (cond, name) => cond.IncludedManifests = AppendOrCreate(cond.IncludedManifests, name));
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private static List<string>? NullIfEmpty(List<string> list) =>
        list.Count == 0 ? null : list;

    private static string? NullIfBlank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string ToRepoRelativePath(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return string.Empty;
        var repo = App.Resolve<IRepositoryService>().CurrentRepository;
        if (repo is null || string.IsNullOrEmpty(repo.RootPath)) return fullPath;
        if (!fullPath.StartsWith(repo.RootPath, StringComparison.OrdinalIgnoreCase)) return fullPath;
        var rel = Path.GetRelativePath(repo.RootPath, fullPath);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    private const string TopLevelContextId = "";

    /// <summary>
    /// Builds the list of contexts for a single field/picker. Top-level is always present;
    /// a conditional is included only if its corresponding field bucket is non-null (i.e.
    /// the key exists in the YAML, even if its list is empty). This avoids polluting every
    /// picker with conditional dropdowns when the conditional has nothing to do with that
    /// particular field.
    /// </summary>
    private static List<ContextOption> BuildContextsForField(
        List<ConditionalItem>? conditionals,
        Func<ConditionalItem, List<string>?> selector)
    {
        var list = new List<ContextOption>
        {
            new(TopLevelContextId, "(top level)", 0),
        };
        if (conditionals is null) return list;

        void Walk(List<ConditionalItem> items, string parentPath, int indent)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var path = parentPath.Length == 0
                    ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : $"{parentPath}.{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

                if (selector(item) is not null)
                {
                    var label = ConditionalLabel(item);
                    list.Add(new ContextOption(path, new string(' ', indent * 4) + label, indent));
                }

                if (item.NestedConditionalItems is { Count: > 0 } nested)
                {
                    Walk(nested, path, indent + 1);
                }
            }
        }

        Walk(conditionals, string.Empty, 1);
        return list;
    }

    private static string ConditionalLabel(ConditionalItem item)
    {
        if (!string.IsNullOrEmpty(item.Condition)) return item.Condition!;
        return $"{item.Fact ?? "?"} {item.Operator ?? "=="} {item.Value}";
    }

    private static IEnumerable<ChipEntry> CollectChipEntries(
        List<ConditionalItem>? conditionals,
        List<string>? topLevel,
        Func<ConditionalItem, List<string>?> selector)
    {
        if (topLevel is { Count: > 0 })
        {
            foreach (var name in topLevel)
            {
                yield return new ChipEntry { Name = name, ContextId = TopLevelContextId };
            }
        }
        if (conditionals is null) yield break;

        IEnumerable<ChipEntry> Walk(List<ConditionalItem> items, string parentPath)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var path = parentPath.Length == 0
                    ? i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : $"{parentPath}.{i.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                var bucket = selector(items[i]);
                if (bucket is { Count: > 0 })
                {
                    foreach (var name in bucket)
                    {
                        yield return new ChipEntry { Name = name, ContextId = path };
                    }
                }
                if (items[i].NestedConditionalItems is { Count: > 0 } nested)
                {
                    foreach (var entry in Walk(nested, path))
                    {
                        yield return entry;
                    }
                }
            }
        }

        foreach (var entry in Walk(conditionals, string.Empty))
        {
            yield return entry;
        }
    }

    private static void DistributeChipEntries(
        Manifest manifest,
        IEnumerable<ChipEntry> entries,
        Action<string> writeTopLevel,
        Action<ConditionalItem, string> writeConditional)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.ContextId))
            {
                writeTopLevel(entry.Name);
                continue;
            }
            var target = ResolveConditional(manifest.ConditionalItems, entry.ContextId);
            if (target is null)
            {
                // Path no longer exists (e.g. someone deleted a conditional in another tool);
                // fall back to top-level so we don't lose the item.
                writeTopLevel(entry.Name);
            }
            else
            {
                writeConditional(target, entry.Name);
            }
        }
    }

    private static ConditionalItem? ResolveConditional(List<ConditionalItem>? root, string path)
    {
        if (root is null || string.IsNullOrEmpty(path)) return null;
        var parts = path.Split('.');
        List<ConditionalItem>? current = root;
        ConditionalItem? node = null;
        foreach (var part in parts)
        {
            if (current is null) return null;
            if (!int.TryParse(part, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var idx)) return null;
            if (idx < 0 || idx >= current.Count) return null;
            node = current[idx];
            current = node.NestedConditionalItems;
        }
        return node;
    }

    private static void ClearAllManagedLists(Manifest manifest)
    {
        manifest.ManagedInstalls = null;
        manifest.ManagedUninstalls = null;
        manifest.ManagedUpdates = null;
        manifest.OptionalInstalls = null;
        manifest.IncludedManifests = null;

        if (manifest.ConditionalItems is null) return;
        WalkClear(manifest.ConditionalItems);

        static void WalkClear(List<ConditionalItem> items)
        {
            foreach (var c in items)
            {
                c.ManagedInstalls = null;
                c.ManagedUninstalls = null;
                c.ManagedUpdates = null;
                c.OptionalInstalls = null;
                c.IncludedManifests = null;
                if (c.NestedConditionalItems is { Count: > 0 } nested)
                {
                    WalkClear(nested);
                }
            }
        }
    }

    private static List<string> AppendOrCreate(List<string>? existing, string value)
    {
        var list = existing ?? [];
        list.Add(value);
        return list;
    }
}
