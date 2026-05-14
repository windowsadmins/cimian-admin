namespace CimianStudio.Core.Models.Git;

/// <summary>
/// Identifies the git worktree that contains the currently-open Cimian deployment.
/// Phase 1 is read-only: this is populated by <c>IGitService.DiscoverAsync</c> and
/// consumed by status queries.
/// </summary>
/// <param name="GitRoot">Absolute path to the directory that contains <c>.git</c>.</param>
/// <param name="RelativeRepoPath">
/// Deployment root expressed relative to <paramref name="GitRoot"/>, with forward
/// slashes. Empty string means the deployment root *is* the git root.
/// </param>
/// <param name="Branch">Current branch name, or null for detached HEAD.</param>
/// <param name="AheadCount">Commits on local branch not yet on upstream.</param>
/// <param name="BehindCount">Commits on upstream not yet on local branch.</param>
/// <param name="HasUpstream">True if the current branch tracks a remote branch.</param>
public sealed record GitRepositoryInfo(
    string GitRoot,
    string RelativeRepoPath,
    string? Branch,
    int AheadCount,
    int BehindCount,
    bool HasUpstream);
