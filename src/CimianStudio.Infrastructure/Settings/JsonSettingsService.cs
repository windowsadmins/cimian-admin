namespace CimianStudio.Infrastructure.Settings;

using System.Text.Json;
using CimianStudio.Shared;
using CimianStudio.Shared.Settings;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON under <c>%LOCALAPPDATA%\CimianStudio\settings.json</c>.
/// </summary>
public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

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

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            using var stream = File.OpenRead(_settingsPath);
            var settings = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            return settings ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
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

    private static string DefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, Constants.AppName, "settings.json");
    }
}
