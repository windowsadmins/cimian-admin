namespace CimianAdmin.Views;

using System.Globalization;
using CimianAdmin.Core.Models.Git;
using CimianAdmin.Core.Models.Manifests;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Core.Models.Repository;
using CimianAdmin.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

public sealed partial class RepositoryPage : Page
{
    private const int RecentLimit = 10;
    private const int GitStatusDisplayLimit = 50;

    private readonly IRepositoryService _repositoryService;
    private readonly IPackageService _packageService;
    private readonly IManifestService _manifestService;
    private readonly IGitService _gitService;

    private List<Package> _recentPackages = [];
    private List<Manifest> _recentManifests = [];
    private GitRepositoryInfo? _gitInfo;
    private List<GitStatusEntry> _gitEntries = [];

    public RepositoryPage(
        IRepositoryService repositoryService,
        IPackageService packageService,
        IManifestService manifestService,
        IGitService gitService)
    {
        ArgumentNullException.ThrowIfNull(repositoryService);
        ArgumentNullException.ThrowIfNull(packageService);
        ArgumentNullException.ThrowIfNull(manifestService);
        ArgumentNullException.ThrowIfNull(gitService);
        _repositoryService = repositoryService;
        _packageService = packageService;
        _manifestService = manifestService;
        _gitService = gitService;
        InitializeComponent();
        Loaded += OnLoaded;
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
        await LoadRecentsAsync().ConfigureAwait(true);
        await LoadGitStatusAsync(repo).ConfigureAwait(true);
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

        GitStatusCard.Visibility = Visibility.Visible;
        GitStatusTitle.Text = string.IsNullOrEmpty(_gitInfo.Branch)
            ? "Git status"
            : string.Create(CultureInfo.InvariantCulture, $"Git status · {_gitInfo.Branch}");

        var rootName = System.IO.Path.GetFileName(
            _gitInfo.GitRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        GitStatusScope.Text = string.IsNullOrEmpty(_gitInfo.RelativeRepoPath)
            ? $"Scoped to {rootName}"
            : $"Scoped to {_gitInfo.RelativeRepoPath} (in {rootName})";

        try
        {
            var entries = await _gitService.GetStatusAsync(_gitInfo).ConfigureAwait(true);
            _gitEntries = [.. entries];
        }
        catch
        {
            _gitEntries = [];
        }

        RenderGitStatus();
    }

    private void RenderGitStatus()
    {
        if (_gitEntries.Count == 0)
        {
            GitStatusEmpty.Visibility = Visibility.Visible;
            GitStatusList.Visibility = Visibility.Collapsed;
            GitStatusList.ItemsSource = null;
            return;
        }

        GitStatusEmpty.Visibility = Visibility.Collapsed;
        GitStatusList.Visibility = Visibility.Visible;

        var rows = _gitEntries
            .Take(GitStatusDisplayLimit)
            .Select(static e => string.Create(
                CultureInfo.InvariantCulture,
                $"{StatusLetter(e.Status)}  {e.RelativePath}"))
            .ToList();
        if (_gitEntries.Count > GitStatusDisplayLimit)
        {
            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"… and {_gitEntries.Count - GitStatusDisplayLimit} more"));
        }
        GitStatusList.ItemsSource = rows;
    }

    private static string StatusLetter(GitFileStatus status) => status switch
    {
        GitFileStatus.Modified => "M ",
        GitFileStatus.Added => "A ",
        GitFileStatus.Deleted => "D ",
        GitFileStatus.Renamed => "R ",
        GitFileStatus.Untracked => "??",
        GitFileStatus.Conflicted => "U ",
        _ => "  ",
    };

    private async void OnGitRefreshClicked(object sender, RoutedEventArgs e)
    {
        var repo = _repositoryService.CurrentRepository;
        if (repo is null) return;
        await LoadGitStatusAsync(repo).ConfigureAwait(true);
    }

    private void OnGitStatusItemClicked(object sender, ItemClickEventArgs e)
    {
        var view = sender as ListView;
        var index = view?.Items.IndexOf(e.ClickedItem) ?? -1;
        if (index < 0 || index >= _gitEntries.Count)
        {
            return;
        }

        var entry = _gitEntries[index];
        if (App.MainWindowInstance is not { } window) return;

        // Map a changed file back to its editor when possible. Heuristic: if it lives
        // under pkgsinfo/, jump to Packages; under manifests/, jump to Manifests;
        // otherwise no-op (user can still see it in the list).
        var rel = entry.RelativePath.Replace('\\', '/');
        if (rel.Contains("/pkgsinfo/", StringComparison.OrdinalIgnoreCase))
        {
            window.NavigateTo("packages");
        }
        else if (rel.Contains("/manifests/", StringComparison.OrdinalIgnoreCase))
        {
            window.NavigateTo("manifests");
        }
        else if (rel.Contains("/catalogs/", StringComparison.OrdinalIgnoreCase))
        {
            window.NavigateTo("catalogs");
        }
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
