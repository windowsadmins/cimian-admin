namespace CimianAdmin.Core.Services;

using CimianAdmin.Core.Models.Git;

/// <summary>
/// Read-only git operations for the currently-open Cimian deployment. Phase 1 of the
/// git-integration plan: title-bar / home-card awareness only — no write verbs yet.
/// All methods return null / empty results (never throw) when no git root is present;
/// callers should hide UI when <see cref="DiscoverAsync"/> returns null.
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Walks up from <paramref name="deploymentRoot"/> looking for a <c>.git</c>
    /// directory or worktree pointer. Returns null when no git root is found.
    /// </summary>
    Task<GitRepositoryInfo?> DiscoverAsync(string deploymentRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every changed file inside <see cref="GitRepositoryInfo.RelativeRepoPath"/>.
    /// Unchanged and ignored files are excluded.
    /// </summary>
    Task<IReadOnlyList<GitStatusEntry>> GetStatusAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="absoluteFilePath"/> differs from HEAD in the working tree.
    /// Returns false (without throwing) if the file is outside the git scope or unchanged.
    /// </summary>
    Task<bool> IsFileModifiedAsync(GitRepositoryInfo info, string absoluteFilePath, CancellationToken cancellationToken = default);
}
