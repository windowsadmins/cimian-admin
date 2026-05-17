namespace CimianStudio.Infrastructure.Tests.Services;

using CimianStudio.Infrastructure.Services;
using FluentAssertions;

public class ManifestServiceTests
{
    [Fact]
    public async Task GetAllManifestsAsync_OnSampleRepo_ReturnsSiteDefault()
    {
        var repoService = new RepositoryService();
        var manifests = new ManifestService(repoService);
        await repoService.OpenRepositoryAsync(TestPaths.SampleRepository);

        var all = await manifests.GetAllManifestsAsync();

        all.Should().HaveCount(1);
        all[0].Name.Should().Be("site_default");
        all[0].DisplayName.Should().Be("Site Default");
        all[0].ManagedInstalls.Should().BeEquivalentTo("firefox");
        all[0].ConditionalItems.Should().NotBeNull().And.HaveCount(2);
    }

    [Fact]
    public async Task GetManifestsForPackage_FindsConditionalReferences()
    {
        var repoService = new RepositoryService();
        var manifests = new ManifestService(repoService);
        await repoService.OpenRepositoryAsync(TestPaths.SampleRepository);

        var refs = await manifests.GetManifestsForPackageAsync("vscode");

        refs.Should().HaveCount(1);
        refs[0].Name.Should().Be("site_default");
    }
}
