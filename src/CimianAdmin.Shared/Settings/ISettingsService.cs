namespace CimianAdmin.Shared.Settings;

/// <summary>
/// Reads and writes application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads the current settings, returning defaults if the settings file does not yet exist.
    /// </summary>
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the supplied settings to disk.
    /// </summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a repository as the last-opened repository and prepends it to the recent list,
    /// trimming to <see cref="AppSettings.MaxRecentRepositories"/>.
    /// </summary>
    Task RecordRepositoryAsync(string path, CancellationToken cancellationToken = default);
}
