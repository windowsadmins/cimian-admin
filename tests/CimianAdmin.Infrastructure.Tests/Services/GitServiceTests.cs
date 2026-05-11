namespace CimianAdmin.Infrastructure.Tests.Services;

using CimianAdmin.Core.Models.Git;
using CimianAdmin.Infrastructure.Services;
using FluentAssertions;
using LibGit2Sharp;

public sealed class GitServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public GitServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "CimianAdminGitTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        // LibGit2Sharp leaves a few read-only attrs on .git internals; flip them
        // before deleting so we don't fail teardown on CI.
        if (!Directory.Exists(_tempRoot)) return;
        foreach (var file in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task Discover_OutsideGitRepo_ReturnsNull()
    {
        var service = new GitService();
        var info = await service.DiscoverAsync(_tempRoot);
        info.Should().BeNull();
    }

    [Fact]
    public async Task Discover_AtGitRoot_ReportsBranchAndEmptyRelativePath()
    {
        var workTree = InitRepoWithCommit("worktree");
        var service = new GitService();

        var info = await service.DiscoverAsync(workTree);

        info.Should().NotBeNull();
        info!.GitRoot.Should().Be(Path.GetFullPath(workTree));
        info.RelativeRepoPath.Should().BeEmpty();
        info.Branch.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Discover_InSubdirectory_ReportsRelativePath()
    {
        var workTree = InitRepoWithCommit("worktree-sub");
        var deploymentRoot = Path.Combine(workTree, "Cimian", "deployment");
        Directory.CreateDirectory(deploymentRoot);

        var service = new GitService();
        var info = await service.DiscoverAsync(deploymentRoot);

        info.Should().NotBeNull();
        info!.RelativeRepoPath.Should().Be("Cimian/deployment");
    }

    [Fact]
    public async Task GetStatus_AfterModifyingFile_ReportsModified()
    {
        var workTree = InitRepoWithCommit("worktree-mod");
        var trackedPath = Path.Combine(workTree, "README.md");
        await File.AppendAllTextAsync(trackedPath, "\nextra line\n");

        var service = new GitService();
        var info = await service.DiscoverAsync(workTree);
        info.Should().NotBeNull();

        var status = await service.GetStatusAsync(info!);

        status.Should().ContainSingle(e =>
            e.RelativePath == "README.md" && e.Status == GitFileStatus.Modified);
    }

    [Fact]
    public async Task IsFileModified_ForUnchangedFile_ReturnsFalse()
    {
        var workTree = InitRepoWithCommit("worktree-clean");
        var trackedPath = Path.Combine(workTree, "README.md");

        var service = new GitService();
        var info = await service.DiscoverAsync(workTree);

        var modified = await service.IsFileModifiedAsync(info!, trackedPath);
        modified.Should().BeFalse();
    }

    private string InitRepoWithCommit(string name)
    {
        var workTree = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(workTree);
        Repository.Init(workTree);
        using var repo = new Repository(workTree);
        var readme = Path.Combine(workTree, "README.md");
        File.WriteAllText(readme, "# test\n");
        Commands.Stage(repo, "README.md");
        var sig = new Signature("Test", "test@example.com", DateTimeOffset.UtcNow);
        repo.Commit("Initial commit", sig, sig);
        return workTree;
    }
}
