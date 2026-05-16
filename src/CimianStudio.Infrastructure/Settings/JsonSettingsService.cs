namespace CimianStudio.Infrastructure.Settings;

using System.Text.Json;
using CimianStudio.Shared;
using CimianStudio.Shared.Settings;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON under <c>%LOCALAPPDATA%\CimianStudio\settings.json</c>.
/// On first launch after the CimianAdmin → CimianStudio rename, an existing legacy
/// settings file is copied across so recent repositories and other preferences survive.
/// </summary>
public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile AppSettings? _cache;

    public JsonSettingsService()
        : this(DefaultSettingsPath())
    {
    }

    /// <summary>
    /// Test-friendly constructor that allows overriding the settings file location.
    /// </summary>
    public JsonSettingsService(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = settingsPath;
    }

    public string SettingsPath => _settingsPath;
    public string SettingsFilePath => _settingsPath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings result;
            if (!File.Exists(_settingsPath))
            {
                result = new AppSettings();
            }
            else
            {
                try
                {
                    using var stream = File.OpenRead(_settingsPath);
                    result = await JsonSerializer
                        .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                        .ConfigureAwait(false) ?? new AppSettings();
                }
                catch (JsonException)
                {
                    result = new AppSettings();
                }
            }

            _cache = result;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(_settingsPath);
            await JsonSerializer
                .SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            _cache = settings;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    public async Task RecordRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var settings = await LoadAsync(cancellationToken).ConfigureAwait(false);

        settings.LastRepositoryPath = path;

        var existing = settings.RecentRepositories;
        existing.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        existing.Insert(0, path);

        var limit = settings.MaxRecentRepositories > 0 ? settings.MaxRecentRepositories : 10;
        if (existing.Count > limit)
        {
            existing.RemoveRange(limit, existing.Count - limit);
        }

        await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public T GetSection<T>(string sectionId) where T : class, new()
    {
        var sections = _cache?.Sections;
        if (sections is not null && sections.TryGetValue(sectionId, out var json))
        {
            try
            {
                return JsonSerializer.Deserialize<T>(json) ?? new T();
            }
            catch (JsonException)
            {
            }
        }

        return new T();
    }

    public async Task SetSectionAsync<T>(string sectionId, T value, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);

        var settings = await LoadAsync(cancellationToken).ConfigureAwait(false);
        settings.Sections[sectionId] = JsonSerializer.Serialize(value, SerializerOptions);
        await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        SectionChanged?.Invoke(this, sectionId);
    }

    public event EventHandler<string>? SectionChanged;

    private static string DefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var newPath = Path.Combine(localAppData, Constants.AppName, "settings.json");
        TryMigrateLegacySettings(localAppData, newPath);
        return newPath;
    }

    // One-shot migration from %LOCALAPPDATA%\CimianAdmin\settings.json so users
    // don't lose recent repositories / last-opened path across the rename. Copies
    // (rather than moves) so the legacy file remains untouched if the user reverts.
    // No-ops once the new file exists, so subsequent launches don't repeat the work.
    // Exposed as `internal` so tests can drive it against a temp-dir layout without
    // having to stub Environment.SpecialFolder.LocalApplicationData.
    internal static void TryMigrateLegacySettings(string localAppData, string newPath)
    {
        if (File.Exists(newPath)) return;
        var legacyPath = Path.Combine(localAppData, "CimianAdmin", "settings.json");
        if (!File.Exists(legacyPath)) return;
        try
        {
            var dir = Path.GetDirectoryName(newPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.Copy(legacyPath, newPath, overwrite: false);
        }
        catch (IOException)
        {
            // Best-effort: fall back to a fresh settings file rather than failing startup.
        }
        catch (UnauthorizedAccessException)
        {
            // Same — proceed with defaults if the legacy file can't be read.
        }
    }
}
