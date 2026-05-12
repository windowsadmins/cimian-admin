namespace CimianAdmin.Views;

using System.Globalization;
using CimianAdmin.Core.Models.Git;
using CimianAdmin.Core.Models.Manifests;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Core.Models.Repository;
using CimianAdmin.Core.Models.Search;
using CimianAdmin.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

public sealed partial class RepositoryPage : Page
{
    private const int RecentLimit = 10;
    private const int SearchDebounceMs = 150;
    private const int SearchMaxResults = 50;

    private readonly IRepositoryService _repositoryService;
    private readonly IPackageService _packageService;
    private readonly IManifestService _manifestService;
    private readonly IGitService _gitService;
    private readonly ISearchService _searchService;
    private readonly DispatcherQueue _dispatcher;

    private List<Package> _recentPackages = [];
    private List<Manifest> _recentManifests = [];
    private GitRepositoryInfo? _gitInfo;
    private List<GitStatusEntry> _gitEntries = [];

    private int _searchEpoch;
    private List<SearchHit> _currentHits = [];

    public RepositoryPage(
        IRepositoryService repositoryService,
        IPackageService packageService,
        IManifestService manifestService,
        IGitService gitService,
        ISearchService searchService)
    {
        ArgumentNullException.ThrowIfNull(repositoryService);
        ArgumentNullException.ThrowIfNull(packageService);
        ArgumentNullException.ThrowIfNull(manifestService);
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(searchService);
        _repositoryService = repositoryService;
        _packageService = packageService;
        _manifestService = manifestService;
        _gitService = gitService;
        _searchService = searchService;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var repo = _repositoryService.CurrentRepository;
        if (repo is null)
        {
            RepoNameText.Text = "No repository";
            RepoPathText.Text = string.Empty;
            return;
        }

        RepoNameText.Text = repo.Name;
        RepoPathText.Text = repo.RootPath;
        BuildStatCards(repo);
        _searchService.ProgressChanged += OnSearchProgress;
        UpdateIndexingPill(_searchService.IsReady ? null : new SearchIndexProgress(0, 0, false));
        await LoadRecentsAsync().ConfigureAwait(true);
        await LoadGitStatusAsync(repo).ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _searchService.ProgressChanged -= OnSearchProgress;
        Interlocked.Increment(ref _searchEpoch);
    }

    private void OnSearchProgress(object? sender, SearchIndexProgress progress)
    {
        _dispatcher.TryEnqueue(() => UpdateIndexingPill(progress));
    }

    private void UpdateIndexingPill(SearchIndexProgress? progress)
    {
        if (progress is null || progress.IsComplete)
        {
            IndexingPill.Visibility = Visibility.Collapsed;
            return;
        }
        IndexingPill.Visibility = Visibility.Visible;
        IndexingPillText.Text = progress.Total > 0
            ? string.Create(CultureInfo.InvariantCulture, $"indexing {progress.Indexed}/{progress.Total}…")
            : "indexing…";
    }

    private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var epoch = Interlocked.Increment(ref _searchEpoch);
        var query = SearchBox.Text;

        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResultsCard.Visibility = Visibility.Collapsed;
            _currentHits = [];
            return;
        }

        await Task.Delay(SearchDebounceMs).ConfigureAwait(true);
        if (epoch != Volatile.Read(ref _searchEpoch)) return;

        IReadOnlyList<SearchHit> hits;
        try
        {
            hits = await _searchService.SearchAsync(query, SearchMaxResults).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (epoch != Volatile.Read(ref _searchEpoch)) return;
        ShowResults(query, hits);
    }

    private void ShowResults(string query, IReadOnlyList<SearchHit> hits)
    {
        _currentHits = [.. hits];
        SearchResultsCard.Visibility = Visibility.Visible;
        SearchResultsHeader.Text = string.Create(CultureInfo.InvariantCulture, $"Results for “{query.Trim()}”");
        SearchResultsCount.Text = hits.Count switch
        {
            0 => "no matches",
            SearchMaxResults => string.Create(CultureInfo.InvariantCulture, $"showing first {SearchMaxResults}"),
            1 => "1 match",
            _ => string.Create(CultureInfo.InvariantCulture, $"{hits.Count} matches"),
        };
        SearchResultsList.ItemsSource = hits.Select(BuildResultRow).ToList();
    }

    private static FrameworkElement BuildResultRow(SearchHit hit)
    {
        var stack = new StackPanel { Spacing = 2 };
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        headerStack.Children.Add(new TextBlock
        {
            Text = hit.DisplayName,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = hit.Kind == SearchHitKind.Package ? "package" : "manifest",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = string.Create(CultureInfo.InvariantCulture, $"line {hit.LineNumber}"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(headerStack);
        stack.Children.Add(new TextBlock
        {
            Text = hit.Snippet,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            Child = stack,
        };
    }

    private async void OnSearchResultClicked(object sender, ItemClickEventArgs e)
    {
        if (sender is not ListView view) return;
        var index = view.Items.IndexOf(e.ClickedItem);
        if (index < 0 || index >= _currentHits.Count) return;
        await OpenHitAsync(_currentHits[index]).ConfigureAwait(true);
    }

    private async Task OpenHitAsync(SearchHit hit)
    {
        if (App.MainWindowInstance is not { } window) return;
        var normalized = System.IO.Path.GetFullPath(hit.AbsolutePath);

        if (hit.Kind == SearchHitKind.Package)
        {
            try
            {
                var packages = await _packageService.GetAllPackagesAsync().ConfigureAwait(true);
                var match = packages.FirstOrDefault(p =>
                    !string.IsNullOrEmpty(p.FilePath) &&
                    string.Equals(System.IO.Path.GetFullPath(p.FilePath), normalized, StringComparison.OrdinalIgnoreCase));
                if (match is not null) window.NavigateToPackage(match);
            }
            catch { }
        }
        else
        {
            try
            {
                var manifests = await _manifestService.GetAllManifestsAsync().ConfigureAwait(true);
                var match = manifests.FirstOrDefault(m =>
                    !string.IsNullOrEmpty(m.FilePath) &&
                    string.Equals(System.IO.Path.GetFullPath(m.FilePath), normalized, StringComparison.OrdinalIgnoreCase));
                if (match is not null) window.NavigateToManifest(match);
            }
            catch { }
        }
    }

    private async Task LoadGitStatusAsync(CimianRepository repo)
    {
        try
        {
            _gitInfo = await _gitService.DiscoverAsync(repo.RootPath).ConfigureAwait(true);
        }
        catch
        {
            _gitInfo = null;
        }

        if (_gitInfo is null)
        {
            GitStatusCard.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var entries = await _gitService.GetStatusAsync(_gitInfo).ConfigureAwait(true);
            _gitEntries = [.. entries];
        }
        catch
        {
            _gitEntries = [];
        }

        GitStatusCard.Visibility = Visibility.Visible;
        GitStatusTitle.Text = _gitEntries.Count == 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"Git · {_gitInfo.Branch ?? "detached"}  ·  Working tree clean")
            : string.Create(CultureInfo.InvariantCulture,
                $"Git · {_gitInfo.Branch ?? "detached"}  ·  {_gitEntries.Count} change(s) pending");

        var rootName = System.IO.Path.GetFileName(
            _gitInfo.GitRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        GitStatusScope.Text = string.IsNullOrEmpty(_gitInfo.RelativeRepoPath)
            ? $"Scoped to {rootName}"
            : $"Scoped to {_gitInfo.RelativeRepoPath} (in {rootName})";
    }

    private void OnOpenGitTabClicked(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("git");
    }

    private async Task LoadRecentsAsync()
    {
        try
        {
            var packages = await _packageService.GetAllPackagesAsync().ConfigureAwait(true);
            _recentPackages = [.. packages
                .Where(p => p.LastModified.HasValue)
                .OrderByDescending(p => p.LastModified)
                .Take(RecentLimit)];

            var manifests = await _manifestService.GetAllManifestsAsync().ConfigureAwait(true);
            _recentManifests = [.. manifests
                .Where(m => m.LastModified.HasValue)
                .OrderByDescending(m => m.LastModified)
                .Take(RecentLimit)];
        }
        catch (Exception)
        {
            _recentPackages = [];
            _recentManifests = [];
        }

        RecentPackagesList.ItemsSource = _recentPackages
            .Select(p => string.IsNullOrEmpty(p.Version) ? p.Name : $"{p.Name}  ({p.Version})")
            .ToList();
        NoRecentPackagesText.Visibility = _recentPackages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentPackagesList.Visibility = _recentPackages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        RecentManifestsList.ItemsSource = _recentManifests.Select(m => m.Name ?? string.Empty).ToList();
        NoRecentManifestsText.Visibility = _recentManifests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentManifestsList.Visibility = _recentManifests.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnRecentPackageClicked(object sender, ItemClickEventArgs e)
    {
        // Map the clicked label back to its index in _recentPackages — string lookup
        // would be fragile because of the "  (version)" suffix.
        var view = sender as ListView;
        var index = view?.Items.IndexOf(e.ClickedItem) ?? -1;
        if (index < 0 || index >= _recentPackages.Count) return;
        var pkg = _recentPackages[index];
        if (App.MainWindowInstance is { } window)
        {
            window.NavigateToPackage(pkg);
        }
    }

    private void OnRecentManifestClicked(object sender, ItemClickEventArgs e)
    {
        var view = sender as ListView;
        var index = view?.Items.IndexOf(e.ClickedItem) ?? -1;
        if (index < 0 || index >= _recentManifests.Count) return;
        var manifest = _recentManifests[index];
        if (App.MainWindowInstance is { } window)
        {
            window.NavigateToManifest(manifest);
        }
    }

    private void BuildStatCards(CimianRepository repo)
    {
        StatsHost.Items.Clear();
        StatsHost.Items.Add(BuildCard("Packages", repo.PackageCount));
        StatsHost.Items.Add(BuildCard("Manifests", repo.ManifestCount));
        StatsHost.Items.Add(BuildCard("Catalogs", repo.CatalogCount));
    }

    private static Border BuildCard(string label, int count)
    {
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        stack.Children.Add(new TextBlock
        {
            Text = count.ToString(CultureInfo.InvariantCulture),
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"],
        });

        return new Border
        {
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 16, 20, 16),
            MinWidth = 160,
            Child = stack,
        };
    }

    private async void OnValidateClicked(object sender, RoutedEventArgs e)
    {
        var repo = _repositoryService.CurrentRepository;
        if (repo is null)
        {
            return;
        }

        var result = await _repositoryService.ValidateRepositoryAsync(repo).ConfigureAwait(true);

        ValidationBar.IsOpen = true;
        if (result.Errors.Count > 0)
        {
            ValidationBar.Severity = InfoBarSeverity.Error;
            ValidationBar.Title = "Repository has errors";
            ValidationBar.Message = $"{result.Errors.Count} error(s), {result.Warnings.Count} warning(s).";
        }
        else if (result.Warnings.Count > 0)
        {
            ValidationBar.Severity = InfoBarSeverity.Warning;
            ValidationBar.Title = "Repository is valid with warnings";
            ValidationBar.Message = $"{result.Warnings.Count} warning(s).";
        }
        else
        {
            ValidationBar.Severity = InfoBarSeverity.Success;
            ValidationBar.Title = "Repository looks good";
            ValidationBar.Message = "All required directories are present.";
        }

        var details = new List<string>();
        details.AddRange(result.Errors.Select(static e => "Error: " + e));
        details.AddRange(result.Warnings.Select(static w => "Warning: " + w));
        ValidationDetails.ItemsSource = details;
    }
}
