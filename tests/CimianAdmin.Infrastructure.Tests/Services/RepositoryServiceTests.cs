namespace CimianAdmin.Infrastructure.Tests.Services;

using CimianAdmin.Infrastructure.Services;
using FluentAssertions;

public class RepositoryServiceTests
{
    [Fact]
    public async Task OpenRepository_OnSampleRepo_ReportsValidWithExpectedCounts()
    {
        var service = new RepositoryService();

        var repo = await service.OpenRepositoryAsync(TestPaths.SampleRepository);

        repo.IsValid.Should().BeTrue("pkgsinfo and manifests are present");
        repo.Name.Should().Be("SampleRepository");
        repo.PackageCount.Should().Be(1);
        repo.ManifestCount.Should().Be(1);
        repo.CatalogCount.Should().Be(0);
        repo.LastScanned.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateRepository_OnSampleRepo_WarnsAboutOptionalDirectories()
    {
        var service = new RepositoryService();
        var repo = await service.OpenRepositoryAsync(TestPaths.SampleRepository);

        var validation = await service.ValidateRepositoryAsync(repo);

        validation.IsValid.Should().BeTrue();
        validation.Errors.Should().BeEmpty();
        validation.Warnings.Should().Contain(w => w.Contains("catalogs", StringComparison.OrdinalIgnoreCase));
        validation.Warnings.Should().Contain(w => w.Contains("pkgs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateRepository_InTempDir_CreatesAllFourSubdirectories()
    {
        var temp = Path.Combine(Path.GetTempPath(), "CimianAdminTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new RepositoryService();
            var repo = await service.CreateRepositoryAsync(temp);

            Directory.Exists(repo.CatalogsPath).Should().BeTrue();
            Directory.Exists(repo.ManifestsPath).Should().BeTrue();
            Directory.Exists(repo.PkgsInfoPath).Should().BeTrue();
            Directory.Exists(repo.PkgsPath).Should().BeTrue();
            repo.IsValid.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OpenRepository_RaisesRepositoryChangedEvent()
    {
        var service = new RepositoryService();
        var raised = false;
        service.RepositoryChanged += (_, _) => raised = true;

        await service.OpenRepositoryAsync(TestPaths.SampleRepository);

        raised.Should().BeTrue();
        service.CurrentRepository.Should().NotBeNull();
    }
}
