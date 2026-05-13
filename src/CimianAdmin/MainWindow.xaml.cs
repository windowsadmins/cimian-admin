namespace CimianAdmin;

using System.Globalization;
using CimianAdmin.Core.Models.Git;
using CimianAdmin.Core.Models.Repository;
using CimianAdmin.Core.Services;
using CimianAdmin.Shared;
using CimianAdmin.ViewModels;
using CimianAdmin.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

public sealed partial class MainWindow : Window
{
    private readonly IRepositoryService _repositoryService;
    private readonly IGitService _gitService;
    private readonly IPackageService _packageService;
    private readonly IManifestService _manifestService;
    private readonly ISessionState _sessionState;
    private readonly MainViewModel _mainViewModel;
    private bool _suppressNavSelection;
    private string? _currentTag;

    // Linear browser-style history. `_historyIndex` points at the *current* entry;
    // Back/Forward move the index and replay the entry. _suppressHistoryPush is set
    // during replay so we don't re-record those navigations.
    private sealed record NavEntry(string Tag, object? Selection);

    private readonly List<NavEntry> _history = [];
    private int _historyIndex = -1;
    private bool _suppressHistoryPush;

    public MainWindow(
        IRepositoryService repositoryService,
        IGitService gitService,
        IPackageService packageService,
        IManifestService manifestService,
        ISessionState sessionState,
        MainViewModel mainViewModel)
    {
        ArgumentNullException.ThrowIfNull(repositoryService);
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(packageService);
        ArgumentNullException.ThrowIfNull(manifestService);
        ArgumentNullException.ThrowIfNull(sessionState);
        ArgumentNullException.ThrowIfNull(mainViewModel);
        _repositoryService = repositoryService;
        _gitService = gitService;
        _packageService = packageService;
        _manifestService = manifestService;
        _sessionState = sessionState;
        _mainViewModel = mainViewModel;

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = Constants.AppName;

        SystemBackdrop = new MicaBackdrop();

        _repositoryService.RepositoryChanged += OnRepositoryChanged;
        _sessionState.Changed += OnSessionStateChanged;
        UpdateRepoTitle(_repositoryService.CurrentRepository);
        UpdateNavEnabled(_repositoryService.CurrentRepository is not null);
        UpdateSaveAllButton();
        _ = RefreshGitIndicatorAsync(_repositoryService.CurrentRepository);
    }

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue is null || DispatcherQueue.HasThreadAccess) UpdateSaveAllButton();
        else DispatcherQueue.TryEnqueue(UpdateSaveAllButton);
    }

    private void UpdateSaveAllButton()
    {
        var n = _sessionState.TotalDirtyCount;
        if (n == 0)
        {
            SaveAllButton.Visibility = Visibility.Collapsed;
            return;
        }
        SaveAllButton.Visibility = Visibility.Visible;
        SaveAllText.Text = string.Create(CultureInfo.InvariantCulture, $"Save all ({n})");
    }

    private async void OnSaveAllClicked(object sender, RoutedEventArgs e)
    {
        SaveAllButton.IsEnabled = false;
        var savedCount = 0;
        var failures = new List<(string Path, string Reason)>();
        try
        {
            // Snapshot so we can iterate without worrying about the underlying
            // set mutating as MarkClean fires Changed events back at us. Each
            // save is independent — one failure must not abort the rest.
            foreach (var pkg in _sessionState.DirtyPackages)
            {
                try
                {
                    await _packageService.SavePackageAsync(pkg).ConfigureAwait(true);
                    _sessionState.MarkPackageClean(pkg);
                    savedCount++;
                }
                catch (Exception ex)
                {
                    failures.Add((pkg.FilePath ?? pkg.Name ?? "(unknown package)", ex.Message));
                }
            }
            foreach (var mf in _sessionState.DirtyManifests)
            {
                try
                {
                    await _manifestService.SaveManifestAsync(mf).ConfigureAwait(true);
                    _sessionState.MarkManifestClean(mf);
                    savedCount++;
                }
                catch (Exception ex)
                {
                    failures.Add((mf.FilePath ?? mf.Name ?? "(unknown manifest)", ex.Message));
                }
            }
        }
        finally
        {
            SaveAllButton.IsEnabled = true;
            UpdateSaveAllButton();
        }

        if (failures.Count > 0)
        {
            await ShowSaveAllFailuresAsync(savedCount, failures).ConfigureAwait(true);
        }
    }

    private async Task ShowSaveAllFailuresAsync(int savedCount, List<(string Path, string Reason)> failures)
    {
        // Cap detail to ~5 entries so the dialog stays scannable; the unfailed
        // ones remain marked dirty in session state for retry.
        var detail = string.Join(
            Environment.NewLine,
            failures.Take(5).Select(f => $"• {f.Path} — {f.Reason}"));
        if (failures.Count > 5)
        {
            detail += $"{Environment.NewLine}…and {failures.Count - 5} more.";
        }

        var dialog = new ContentDialog
        {
            Title = $"Save all: {savedCount} saved, {failures.Count} failed",
            Content = $"These items remain dirty and can be retried:{Environment.NewLine}{Environment.NewLine}{detail}",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// Switches the content frame to the page identified by <paramref name="tag"/>
    /// and selects the corresponding nav item without re-firing SelectionChanged.
    /// </summary>
    public void NavigateTo(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        if (string.Equals(_currentTag, tag, StringComparison.Ordinal))
        {
            return;
        }

        var page = ResolvePage(tag);
        if (page is null)
        {
            return;
        }

        ContentFrame.Content = page;
        _currentTag = tag;

        var item = FindNavItem(tag);
        if (item is not null && !ReferenceEquals(NavView.SelectedItem, item))
        {
            _suppressNavSelection = true;
            try
            {
                NavView.SelectedItem = item;
            }
            finally
            {
                _suppressNavSelection = false;
            }
        }

        PushHistory(new NavEntry(tag, null));
    }

    /// <summary>
    /// Records the current selection on the active page. Called by PackagesPage /
    /// ManifestsPage when the user picks a row, so back/forward replays the selection.
    /// </summary>
    public void RecordSelection(string tag, object? selection)
    {
        if (_suppressHistoryPush) return;
        // If the last entry is for the same tag, replace its selection rather than
        // pushing a new entry — otherwise scrolling through rows pollutes history.
        if (_historyIndex >= 0 && _historyIndex < _history.Count
            && string.Equals(_history[_historyIndex].Tag, tag, StringComparison.Ordinal)
            && _history[_historyIndex].Selection is null)
        {
            _history[_historyIndex] = new NavEntry(tag, selection);
            UpdateBackForwardEnabled();
            return;
        }
        PushHistory(new NavEntry(tag, selection));
    }

    private void PushHistory(NavEntry entry)
    {
        if (_suppressHistoryPush) return;
        // Trim any forward history once the user takes a new action.
        if (_historyIndex < _history.Count - 1)
        {
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        }
        _history.Add(entry);
        _historyIndex = _history.Count - 1;
        UpdateBackForwardEnabled();
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (_historyIndex <= 0) return;
        _historyIndex--;
        ReplayCurrentEntry();
    }

    private void OnForwardClicked(object sender, RoutedEventArgs e)
    {
        if (_historyIndex >= _history.Count - 1) return;
        _historyIndex++;
        ReplayCurrentEntry();
    }

    private void ReplayCurrentEntry()
    {
        var entry = _history[_historyIndex];
        _suppressHistoryPush = true;
        try
        {
            if (!string.Equals(_currentTag, entry.Tag, StringComparison.Ordinal))
            {
                NavigateTo(entry.Tag);
            }

            switch (entry.Selection)
            {
                case CimianAdmin.Core.Models.Packages.Package pkg:
                    App.PendingPackageSelection = pkg;
                    if (ContentFrame.Content is Views.PackagesPage pp)
                    {
                        pp.SelectPending();
                    }
                    break;
                case CimianAdmin.Core.Models.Manifests.Manifest mf:
                    App.PendingManifestSelection = mf;
                    if (ContentFrame.Content is Views.ManifestsPage mp)
                    {
                        mp.SelectPending();
                    }
                    break;
            }
        }
        finally
        {
            _suppressHistoryPush = false;
            UpdateBackForwardEnabled();
        }
    }

    private void UpdateBackForwardEnabled()
    {
        BackButton.IsEnabled = _historyIndex > 0;
        ForwardButton.IsEnabled = _historyIndex < _history.Count - 1;
    }

    /// <summary>
    /// Navigates to the Packages page and asks it to select the given package once it loads.
    /// </summary>
    public void NavigateToPackage(CimianAdmin.Core.Models.Packages.Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        App.PendingPackageSelection = package;
        NavigateTo("packages");
    }

    /// <summary>
    /// Navigates to the Manifests page and asks it to select the given manifest once it loads.
    /// </summary>
    public void NavigateToManifest(CimianAdmin.Core.Models.Manifests.Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        App.PendingManifestSelection = manifest;
        NavigateTo("manifests");
    }

    /// <summary>
    /// Pops the WinUI 3 FolderPicker and, if the user picks a folder, opens it as the
    /// current repository.  Returns true if a repository was opened.
    /// </summary>
    public async Task<bool> PromptAndOpenRepositoryAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return false;
            }

            return await _mainViewModel.OpenRepositoryAsync(folder.Path).ConfigureAwait(true);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNavSelection || args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        NavigateTo(tag);
    }

    private void OnRepositoryChanged(object? sender, CimianRepository? repository)
    {
        if (DispatcherQueue is null || DispatcherQueue.HasThreadAccess)
        {
            ApplyRepositoryChange(repository);
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => ApplyRepositoryChange(repository));
        }
    }

    private async void ApplyRepositoryChange(CimianRepository? repository)
    {
        UpdateRepoTitle(repository);
        UpdateNavEnabled(repository is not null);

        if (repository is not null)
        {
            NavigateTo("repository");
        }

        await RefreshGitIndicatorAsync(repository).ConfigureAwait(true);
    }

    private void UpdateRepoTitle(CimianRepository? repository)
    {
        RepoTitleText.Text = repository is null ? string.Empty : repository.Name;
    }

    /// <summary>
    /// Refreshes the title-bar branch / ahead-behind chip. Best-effort: any failure
    /// just hides the chip — we never want a git problem to break navigation.
    /// </summary>
    public async Task RefreshGitIndicatorAsync(CimianRepository? repository)
    {
        if (repository is null)
        {
            GitIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        GitRepositoryInfo? info;
        try
        {
            info = await _gitService.DiscoverAsync(repository.RootPath).ConfigureAwait(true);
        }
        catch
        {
            info = null;
        }

        if (info is null || string.IsNullOrEmpty(info.Branch))
        {
            GitIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        GitBranchText.Text = info.Branch;
        GitAheadBehindText.Text = FormatAheadBehind(info);
        GitIndicatorTooltip.Text = FormatGitTooltip(repository, info);
        GitIndicator.Visibility = Visibility.Visible;
    }

    private static string FormatAheadBehind(GitRepositoryInfo info)
    {
        if (!info.HasUpstream) return "(no upstream)";
        if (info.AheadCount == 0 && info.BehindCount == 0) return "↑0 ↓0";
        return string.Create(CultureInfo.InvariantCulture, $"↑{info.AheadCount} ↓{info.BehindCount}");
    }

    private static string FormatGitTooltip(CimianRepository repository, GitRepositoryInfo info)
    {
        var scope = string.IsNullOrEmpty(info.RelativeRepoPath) ? repository.Name : info.RelativeRepoPath;
        var rootName = Path.GetFileName(info.GitRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(info.RelativeRepoPath)
            ? $"Git repository · branch {info.Branch}"
            : $"Git status scoped to {scope} (in {rootName} repo) · branch {info.Branch}";
    }

    private void UpdateNavEnabled(bool enabled)
    {
        NavRepository.IsEnabled = enabled;
        NavPackages.IsEnabled = enabled;
        NavImport.IsEnabled = enabled;
        NavManifests.IsEnabled = enabled;
        NavCatalogs.IsEnabled = enabled;
        NavGit.IsEnabled = enabled;
    }

    private NavigationViewItem? FindNavItem(string tag)
    {
        return tag switch
        {
            "repository" => NavRepository,
            "packages" => NavPackages,
            "import" => NavImport,
            "manifests" => NavManifests,
            "catalogs" => NavCatalogs,
            "git" => NavGit,
            _ => null,
        };
    }

    private static Page? ResolvePage(string tag)
    {
        return tag switch
        {
            "welcome" => App.Resolve<WelcomePage>(),
            "repository" => App.Resolve<RepositoryPage>(),
            "packages" => App.Resolve<PackagesPage>(),
            "manifests" => App.Resolve<ManifestsPage>(),
            "catalogs" => App.Resolve<CatalogsPage>(),
            "import" => App.Resolve<Views.Import.ImportPage>(),
            "git" => App.Resolve<GitPage>(),
            _ => null,
        };
    }
}
