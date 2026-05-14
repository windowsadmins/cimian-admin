namespace CimianStudio.Infrastructure.Tests.Services;

using CimianStudio.Core.Models.Search;
using CimianStudio.Infrastructure.Services;
using FluentAssertions;

public class SearchServiceTests : IDisposable
{
    private readonly SearchService _service = new();
    private readonly string _root;

    public SearchServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CimianStudioSearchTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "pkgsinfo", "Mozilla"));
        Directory.CreateDirectory(Path.Combine(_root, "manifests"));
        File.WriteAllText(Path.Combine(_root, "pkgsinfo", "Mozilla", "Firefox.yaml"),
            """
            name: firefox
            display_name: Mozilla Firefox
            version: "120.0"
            description: A free and open-source web browser developed by Mozilla Foundation
            blocking_applications:
              - firefox.exe
            """);
        File.WriteAllText(Path.Combine(_root, "manifests", "site_default.yaml"),
            """
            display_name: Site Default
            catalogs:
              - production
            managed_installs:
              - firefox
            notes: |
              Engineering machines get additional development tools.
            """);
    }

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SearchAsync_FindsTermInPackageBody_NotJustName()
    {
        await _service.StartAsync(_root);

        var hits = await _service.SearchAsync("blocking_applications");

        hits.Should().ContainSingle();
        hits[0].Kind.Should().Be(SearchHitKind.Package);
        hits[0].DisplayName.Should().Be("Firefox");
        hits[0].Snippet.Should().Contain("blocking_applications");
    }

    [Fact]
    public async Task SearchAsync_IsCaseInsensitive()
    {
        await _service.StartAsync(_root);

        var hits = await _service.SearchAsync("MOZILLA");

        hits.Should().HaveCountGreaterThan(0);
        hits.Should().Contain(h => h.Kind == SearchHitKind.Package);
    }

    [Fact]
    public async Task SearchAsync_FindsTermInManifest()
    {
        await _service.StartAsync(_root);

        var hits = await _service.SearchAsync("Engineering machines");

        hits.Should().ContainSingle(h => h.Kind == SearchHitKind.Manifest);
        hits[0].DisplayName.Should().Be("site_default");
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        await _service.StartAsync(_root);

        var hits = await _service.SearchAsync("   ");

        hits.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_AfterFileEdit_PicksUpNewContent()
    {
        await _service.StartAsync(_root);
        var path = Path.Combine(_root, "pkgsinfo", "Mozilla", "Firefox.yaml");

        // initial query should miss
        (await _service.SearchAsync("freshly_added_marker")).Should().BeEmpty();

        await File.AppendAllTextAsync(path, "\nfreshly_added_marker: true\n");
        // Wait for debounce + filesystem watcher; give it generous slack.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(100);
            var hits = await _service.SearchAsync("freshly_added_marker");
            if (hits.Count > 0)
            {
                hits[0].DisplayName.Should().Be("Firefox");
                return;
            }
        }
        Assert.Fail("FileSystemWatcher did not re-index within 4 seconds.");
    }
}
