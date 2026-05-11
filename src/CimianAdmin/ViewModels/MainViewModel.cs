namespace CimianAdmin.ViewModels;

using System.Collections.ObjectModel;
using CimianAdmin.Core.Models.Repository;
using CimianAdmin.Core.Services;
using CimianAdmin.Shared.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IRepositoryService _repositoryService;

    [ObservableProperty]
    public partial CimianRepository? CurrentRepository { get; set; }

    [ObservableProperty]
    public partial string CurrentRepositoryPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasRepository { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> RecentRepositories { get; set; } = [];

    public MainViewModel(ISettingsService settings, IRepositoryService repositoryService)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(repositoryService);
        _settings = settings;
        _repositoryService = repositoryService;
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
    }
}
