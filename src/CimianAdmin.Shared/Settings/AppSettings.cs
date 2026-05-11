namespace CimianAdmin.Shared.Settings;

/// <summary>
/// User-level application settings persisted as JSON in <c>%LOCALAPPDATA%\CimianAdmin\settings.json</c>.
/// </summary>
public sealed class AppSettings
{
    public string? LastRepositoryPath { get; set; }

    public List<string> RecentRepositories { get; set; } = [];

    public int MaxRecentRepositories { get; set; } = 10;

    public double WindowWidth { get; set; } = 1400;

    public double WindowHeight { get; set; } = 900;
}
