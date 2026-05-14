namespace CimianStudio.Infrastructure.Tests.Settings;

using CimianStudio.Infrastructure.Settings;
using FluentAssertions;

public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public JsonSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CimianStudioSettings-" + Guid.NewGuid().ToString("N"));
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsDefaults()
    {
        using var service = new JsonSettingsService(_settingsPath);

        var settings = await service.LoadAsync();

        settings.LastRepositoryPath.Should().BeNull();
        settings.RecentRepositories.Should().BeEmpty();
        settings.MaxRecentRepositories.Should().Be(10);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        using var service = new JsonSettingsService(_settingsPath);
        var original = new Shared.Settings.AppSettings
        {
            LastRepositoryPath = @"C:\\repo",
            RecentRepositories = [@"C:\\repo", @"D:\\other"],
            WindowWidth = 1600,
            WindowHeight = 1000,
        };

        await service.SaveAsync(original);
        var loaded = await service.LoadAsync();

        loaded.LastRepositoryPath.Should().Be(@"C:\\repo");
        loaded.RecentRepositories.Should().HaveCount(2);
        loaded.WindowWidth.Should().Be(1600);
        loaded.WindowHeight.Should().Be(1000);
    }

    [Fact]
    public async Task RecordRepositoryAsync_DeduplicatesAndPrepends()
    {
        using var service = new JsonSettingsService(_settingsPath);

        await service.RecordRepositoryAsync(@"C:\\one");
        await service.RecordRepositoryAsync(@"C:\\two");
        await service.RecordRepositoryAsync(@"C:\\one"); // re-record, should bubble to top

        var settings = await service.LoadAsync();
        settings.LastRepositoryPath.Should().Be(@"C:\\one");
        settings.RecentRepositories.Should().HaveCount(2);
        settings.RecentRepositories[0].Should().Be(@"C:\\one");
        settings.RecentRepositories[1].Should().Be(@"C:\\two");
    }

    [Fact]
    public async Task RecordRepositoryAsync_TrimsToMaxRecentRepositories()
    {
        using var service = new JsonSettingsService(_settingsPath);
        await service.SaveAsync(new Shared.Settings.AppSettings { MaxRecentRepositories = 3 });

        for (var i = 0; i < 5; i++)
        {
            await service.RecordRepositoryAsync($@"C:\\repo{i}");
        }

        var settings = await service.LoadAsync();
        settings.RecentRepositories.Should().HaveCount(3);
        settings.RecentRepositories[0].Should().Be(@"C:\\repo4");
        settings.RecentRepositories[2].Should().Be(@"C:\\repo2");
    }
}
