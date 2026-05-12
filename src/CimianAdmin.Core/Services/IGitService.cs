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

    /// <summary>
    /// Stages the supplied repo-relative paths (works for adds, modifies and deletes).
    /// Uses LibGit2Sharp directly — no hooks run on staging, matching <c>git add</c>.
    /// </summary>
    Task StageAsync(GitRepositoryInfo info, IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new commit with the given subject + optional body. Shells out to
    /// <c>git.exe</c> so pre-commit and commit-msg hooks fire by default; pass
    /// <paramref name="runHooks"/> = false to add <c>--no-verify</c>. The optional
    /// <paramref name="progress"/> receives each line of stdout/stderr as it streams,
    /// so the UI can show live hook output instead of sitting silent.
    /// </summary>
    Task<GitCommitResult> CommitAsync(GitRepositoryInfo info, string subject, string? body, bool runHooks, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the current branch to its configured upstream. Shells out to
    /// <c>git.exe</c> so the Windows credential manager and any SSH agent are used.
    /// Streams progress lines to <paramref name="progress"/> when supplied.
    /// </summary>
    Task<GitPushResult> PushAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the configured <c>user.name</c> and <c>user.email</c> for the repo
    /// (falling back to global config). Either field may be empty if unconfigured.
    /// </summary>
    Task<GitIdentity> GetIdentityAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <c>user.name</c> and <c>user.email</c> at the supplied scope.
    /// </summary>
    Task SetIdentityAsync(GitRepositoryInfo info, string name, string email, GitConfigScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight credential probe — runs <c>git ls-remote --heads origin</c> and
    /// reports success/failure with the git output, so the user can validate their
    /// credentials before they spend time crafting a commit that won't push.
    /// </summary>
    Task<GitAuthResult> TestAuthAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a unified-diff text representation of the working-tree change for one
    /// file. Empty string if the path is unchanged. Untracked files are emitted as
    /// "+ "-prefixed line dumps. Binary blobs return a short marker rather than bytes.
    /// </summary>
    Task<string> GetDiffAsync(GitRepositoryInfo info, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists local branches in commit-time order (most recent tip first).
    /// </summary>
    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks out an existing local branch. Fails (returns Success=false with an
    /// error message) when the working tree has uncommitted changes that would
    /// conflict with the target branch.
    /// </summary>
    Task<GitCheckoutResult> CheckoutBranchAsync(GitRepositoryInfo info, string branchName, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IGitService.CommitAsync"/>.</summary>
/// <param name="Success">True if <c>git commit</c> exited 0.</param>
/// <param name="CommitSha">First 12 chars of the new commit, when known.</param>
/// <param name="Output">Combined stdout+stderr (hook output lands here when it fails).</param>
public sealed record GitCommitResult(bool Success, string? CommitSha, string Output);

/// <summary>Outcome of <see cref="IGitService.PushAsync"/>.</summary>
/// <param name="Success">True if <c>git push</c> exited 0.</param>
/// <param name="Output">Combined stdout+stderr.</param>
public sealed record GitPushResult(bool Success, string Output);

/// <summary>One local branch.</summary>
/// <param name="Name">Friendly branch name (e.g. <c>main</c>).</param>
/// <param name="IsCurrent">True if this branch is currently checked out.</param>
/// <param name="TipSha">First 12 chars of the branch tip commit, or null if unknown.</param>
public sealed record GitBranch(string Name, bool IsCurrent, string? TipSha);

/// <summary>Outcome of <see cref="IGitService.CheckoutBranchAsync"/>.</summary>
public sealed record GitCheckoutResult(bool Success, string? ErrorMessage);

/// <summary>git user.name / user.email pair.</summary>
public sealed record GitIdentity(string Name, string Email);

/// <summary>Where to write a git config value.</summary>
public enum GitConfigScope
{
    /// <summary>Repository-local <c>.git/config</c>.</summary>
    Local,
    /// <summary>The user's global <c>.gitconfig</c>.</summary>
    Global,
}

/// <summary>Outcome of <see cref="IGitService.TestAuthAsync"/>.</summary>
/// <param name="Success">True if the remote responded successfully.</param>
/// <param name="Output">Combined stdout+stderr from <c>git ls-remote</c>.</param>
public sealed record GitAuthResult(bool Success, string Output);
