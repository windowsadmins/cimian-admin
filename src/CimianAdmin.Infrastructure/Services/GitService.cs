namespace CimianAdmin.Infrastructure.Services;

using CimianAdmin.Core.Models.Git;
using CimianAdmin.Core.Services;
using LibGit2Sharp;

/// <summary>
/// LibGit2Sharp-backed read-only git service. Native binaries ship with the
/// LibGit2Sharp NuGet so no <c>git.exe</c> is required for status queries.
/// All public methods swallow LibGit2Sharp exceptions and return empty/null
/// results — git visibility is best-effort and must not break the editor flow.
/// </summary>
public sealed class GitService : IGitService
{
    public Task<GitRepositoryInfo?> DiscoverAsync(string deploymentRoot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => DiscoverCore(deploymentRoot), cancellationToken);
    }

    public Task<IReadOnlyList<GitStatusEntry>> GetStatusAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run<IReadOnlyList<GitStatusEntry>>(() => GetStatusCore(info), cancellationToken);
    }

    public Task<bool> IsFileModifiedAsync(GitRepositoryInfo info, string absoluteFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        return Task.Run(() => IsFileModifiedCore(info, absoluteFilePath), cancellationToken);
    }

    private static GitRepositoryInfo? DiscoverCore(string deploymentRoot)
    {
        if (string.IsNullOrWhiteSpace(deploymentRoot) || !Directory.Exists(deploymentRoot))
        {
            return null;
        }

        var discovered = Repository.Discover(deploymentRoot);
        if (string.IsNullOrEmpty(discovered))
        {
            return null;
        }

        // Repository.Discover returns the path to the .git directory (with trailing
        // separator). The worktree root is its parent.
        var gitDir = discovered.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workTree = Path.GetDirectoryName(gitDir);
        if (string.IsNullOrEmpty(workTree))
        {
            return null;
        }

        try
        {
            using var repo = new Repository(workTree);
            var head = repo.Head;
            var branchName = head?.FriendlyName == "(no branch)" ? null : head?.FriendlyName;

            int ahead = 0, behind = 0;
            var hasUpstream = head?.TrackedBranch is not null;
            if (hasUpstream)
            {
                ahead = head!.TrackingDetails.AheadBy ?? 0;
                behind = head.TrackingDetails.BehindBy ?? 0;
            }

            return new GitRepositoryInfo(
                GitRoot: workTree,
                RelativeRepoPath: ToRelativeRepoPath(workTree, deploymentRoot),
                Branch: branchName,
                AheadCount: ahead,
                BehindCount: behind,
                HasUpstream: hasUpstream);
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
    }

    private static List<GitStatusEntry> GetStatusCore(GitRepositoryInfo info)
    {
        try
        {
            using var repo = new Repository(info.GitRoot);
            var options = new StatusOptions
            {
                IncludeIgnored = false,
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            };

            // LibGit2Sharp's StatusOptions.PathSpec scopes the walk to a subdirectory,
            // so we don't fan out into the whole parent repo.
            if (!string.IsNullOrEmpty(info.RelativeRepoPath))
            {
                options.PathSpec = [info.RelativeRepoPath];
            }

            var results = new List<GitStatusEntry>();
            foreach (var entry in repo.RetrieveStatus(options))
            {
                if (entry.State == FileStatus.Unaltered || entry.State == FileStatus.Ignored)
                {
                    continue;
                }

                var status = MapStatus(entry.State);
                if (status == GitFileStatus.Unchanged || status == GitFileStatus.Ignored)
                {
                    continue;
                }

                var rel = entry.FilePath.Replace('\\', '/');
                var abs = Path.GetFullPath(Path.Combine(info.GitRoot, entry.FilePath));
                var staged = (entry.State & (FileStatus.NewInIndex | FileStatus.ModifiedInIndex
                    | FileStatus.DeletedFromIndex | FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex)) != 0;

                results.Add(new GitStatusEntry(rel, abs, status, staged));
            }

            return results;
        }
        catch (LibGit2SharpException)
        {
            return [];
        }
    }

    private static bool IsFileModifiedCore(GitRepositoryInfo info, string absoluteFilePath)
    {
        try
        {
            using var repo = new Repository(info.GitRoot);
            var relative = Path.GetRelativePath(info.GitRoot, absoluteFilePath).Replace('\\', '/');
            var status = repo.RetrieveStatus(relative);
            if (status == FileStatus.Unaltered || status == FileStatus.Nonexistent || status == FileStatus.Ignored)
            {
                return false;
            }
            // Treat any working-tree or index difference as "modified on disk" for the
            // purposes of the editor pill — same signal git would show in `status`.
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    private static string ToRelativeRepoPath(string gitRoot, string deploymentRoot)
    {
        var rel = Path.GetRelativePath(gitRoot, deploymentRoot).Replace('\\', '/');
        return rel == "." ? string.Empty : rel.TrimEnd('/');
    }

    private static GitFileStatus MapStatus(FileStatus state)
    {
        // Order matters: check conflict first (it overlaps with other bits), then
        // the most specific working-tree/index categories.
        if ((state & FileStatus.Conflicted) != 0) return GitFileStatus.Conflicted;
        if ((state & (FileStatus.NewInWorkdir | FileStatus.NewInIndex)) != 0)
        {
            return (state & FileStatus.NewInWorkdir) != 0 && (state & FileStatus.NewInIndex) == 0
                ? GitFileStatus.Untracked
                : GitFileStatus.Added;
        }
        if ((state & (FileStatus.DeletedFromIndex | FileStatus.DeletedFromWorkdir)) != 0) return GitFileStatus.Deleted;
        if ((state & (FileStatus.RenamedInIndex | FileStatus.RenamedInWorkdir)) != 0) return GitFileStatus.Renamed;
        if ((state & (FileStatus.ModifiedInIndex | FileStatus.ModifiedInWorkdir | FileStatus.TypeChangeInIndex | FileStatus.TypeChangeInWorkdir)) != 0) return GitFileStatus.Modified;
        return GitFileStatus.Unchanged;
    }
}
