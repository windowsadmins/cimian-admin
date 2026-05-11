namespace CimianAdmin.Infrastructure.Tests.Services;

using CimianAdmin.Infrastructure.Services;
using FluentAssertions;

public class PackageServiceTests
{
    [Fact]
    public async Task GetAllPackagesAsync_OnSampleRepo_ReturnsFirefox()
    {
        var (repoService, packageService) = CreateServices();
        var repo = await repoService.OpenRepositoryAsync(TestPaths.SampleRepository);

        var packages = await packageService.GetAllPackagesAsync();

        packages.Should().HaveCount(1);
        packages[0].Name.Should().Be("firefox");
        packages[0].FilePath.Should().NotBeNullOrEmpty();
        packages[0].FilePath!.Should().StartWith(repo.PkgsInfoPath);
    }

    [Fact]
    public async Task SearchPackagesAsync_FiltersByText()
    {
        var (repoService, packageService) = CreateServices();
        await repoService.OpenRepositoryAsync(TestPaths.SampleRepository);

        var hits = await packageService.SearchPackagesAsync("mozilla");
        hits.Should().HaveCount(1);
        hits[0].Name.Should().Be("firefox");

        var misses = await packageService.SearchPackagesAsync("notarealpackage");
        misses.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchPackagesAsync_FiltersByCatalog()
    {
        var (repoService, packageService) = CreateServices();
        await repoService.OpenRepositoryAsync(TestPaths.SampleRepository);

        var production = await packageService.SearchPackagesAsync(string.Empty, catalog: "production");
        production.Should().HaveCount(1);

        var none = await packageService.SearchPackagesAsync(string.Empty, catalog: "nonexistent");
        none.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllPackages_WithoutOpenRepository_Throws()
    {
        var (_, packageService) = CreateServices();

        var act = () => packageService.GetAllPackagesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static (RepositoryService Repo, PackageService Packages) CreateServices()
    {
        var repoService = new RepositoryService();
        return (repoService, new PackageService(repoService));
    }
}
