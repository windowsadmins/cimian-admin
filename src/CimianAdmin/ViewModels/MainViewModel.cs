namespace CimianAdmin.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using CimianAdmin.Core.Models.Repository;
using CimianAdmin.Core.Services;
using CimianAdmin.Shared.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IRepositoryService _repositoryService;
    private readonly ISearchService _searchService;
    // Serializes search-index lifecycle changes so a fast Open→Close→Open
    // doesn't race two StartAsync calls against one another. Each repo change
    // schedules a continuation onto this task.
    private Task _searchPipeline = Task.CompletedTask;
    private readonly Lock _searchPipelineLock = new();

    [ObservableProperty]
    public partial CimianRepository? CurrentRepository { get; set; }

    [ObservableProperty]
    public partial string CurrentRepositoryPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRepository { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> RecentRepositories { get; set; } = [];

    public MainViewModel(ISettingsService settings, IRepositoryService repositoryService, ISearchService searchService)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(repositoryService);
        ArgumentNullException.ThrowIfNull(searchService);
        _settings = settings;
        _repositoryService = repositoryService;
        _searchService = searchService;
        _repositoryService.RepositoryChanged += OnRepositoryChanged;
    }

    /// <summary>
    /// Performs first-launch routing: re-opens the last repository if available;
    /// otherwise asks the window to show the welcome page and (if no recents) prompt the picker.
    /// </summary>
    public async Task InitializeAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var settings = await _settings.LoadAsync().ConfigureAwait(true);
        RecentRepositories = [.. settings.RecentRepositories];

        if (!string.IsNullOrWhiteSpace(settings.LastRepositoryPath)
            && Directory.Exists(settings.LastRepositoryPath))
        {
            try
            {
                await _repositoryService.OpenRepositoryAsync(settings.LastRepositoryPath).ConfigureAwait(true);
                window.NavigateTo("repository");
                return;
            }
            catch (Exception)
            {
                // Fall through to welcome flow if the saved repo can no longer be opened.
            }
        }

        window.NavigateTo("welcome");
        if (RecentRepositories.Count == 0)
        {
            await window.PromptAndOpenRepositoryAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Opens the supplied repository path, persists it to settings, and refreshes recents.
    /// </summary>
    public async Task<bool> OpenRepositoryAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        await _repositoryService.OpenRepositoryAsync(path).ConfigureAwait(true);
        await _settings.RecordRepositoryAsync(path).ConfigureAwait(true);

        var settings = await _settings.LoadAsync().ConfigureAwait(true);
        RecentRepositories = [.. settings.RecentRepositories];
        return true;
    }

    private void OnRepositoryChanged(object? sender, CimianRepository? repository)
    {
        CurrentRepository = repository;
        CurrentRepositoryPath = repository?.RootPath ?? string.Empty;
        HasRepository = repository is not null;

        // Chain search-index work behind the previous lifecycle task so rapid repo
        // changes can't run two Start/Stop calls in parallel. Exceptions are logged
        // rather than swallowed — they'd be unobservable on a fire-and-forget task.
        lock (_searchPipelineLock)
        {
            _searchPipeline = _searchPipeline.ContinueWith(_ =>
                repository is null
                    ? _searchService.StopAsync()
                    : _searchService.StartAsync(repository.RootPath),
                TaskScheduler.Default).Unwrap();
            _searchPipeline.ContinueWith(
                t => Debug.WriteLine($"[SearchService] lifecycle failed: {t.Exception}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
