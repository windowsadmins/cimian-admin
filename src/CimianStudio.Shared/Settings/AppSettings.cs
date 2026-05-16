namespace CimianStudio.Shared.Settings;

/// <summary>
/// User-level application settings persisted as JSON in <c>%LOCALAPPDATA%\CimianStudio\settings.json</c>.
/// </summary>
public sealed class AppSettings
{
    public string? LastRepositoryPath { get; set; }

    public List<string> RecentRepositories { get; set; } = [];

    public int MaxRecentRepositories { get; set; } = 10;

    public double WindowWidth { get; set; } = 1400;

    public double WindowHeight { get; set; } = 900;

    /// <summary>
    /// Per-feature settings blobs, keyed by section id, stored as JSON strings.
    /// Managed by <see cref="ISettingsService.GetSection{T}"/> / <see cref="ISettingsService.SetSectionAsync{T}"/>.
    /// </summary>
    public Dictionary<string, string> Sections { get; set; } = [];
}
