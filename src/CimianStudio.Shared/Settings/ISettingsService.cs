namespace CimianStudio.Shared.Settings;

/// <summary>
/// Reads and writes application settings.
/// </summary>
public interface ISettingsService
{
    /// <summary>Absolute path to the JSON settings file on disk.</summary>
    string SettingsFilePath { get; }

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

    /// <summary>
    /// Returns the named settings section, deserialised to <typeparamref name="T"/>.
    /// Returns <c>new T()</c> if the section has never been saved.
    /// Reads from an in-memory cache; call <see cref="LoadAsync"/> at least once first.
    /// </summary>
    T GetSection<T>(string sectionId) where T : class, new();

    /// <summary>
    /// Persists the named settings section and raises <see cref="SectionChanged"/>.
    /// </summary>
    Task SetSectionAsync<T>(string sectionId, T value, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Raised after <see cref="SetSectionAsync{T}"/> completes; argument is the <c>sectionId</c>.
    /// </summary>
    event EventHandler<string>? SectionChanged;
}
