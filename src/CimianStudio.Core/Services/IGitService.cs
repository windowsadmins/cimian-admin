namespace CimianStudio.Core.Services;

using CimianStudio.Core.Models.Git;

/// <summary>
/// Git operations for the currently-open Cimian deployment.
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
    /// </summary>
    Task StageAsync(GitRepositoryInfo info, IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new commit. Shells out to <c>git.exe</c> so hooks fire by default;
    /// pass <paramref name="runHooks"/> = false to add <c>--no-verify</c>.
    /// </summary>
    Task<GitCommitResult> CommitAsync(GitRepositoryInfo info, string subject, string? body, bool runHooks, bool amend = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes the current branch to its configured upstream.
    /// </summary>
    Task<GitPushResult> PushAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the configured <c>user.name</c> and <c>user.email</c> for the repo.
    /// </summary>
    Task<GitIdentity> GetIdentityAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <c>user.name</c> and <c>user.email</c> at the supplied scope.
    /// </summary>
    Task SetIdentityAsync(GitRepositoryInfo info, string name, string email, GitConfigScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>git ls-remote --heads origin</c> to validate remote credentials.
    /// </summary>
    Task<GitAuthResult> TestAuthAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the unified-diff for one working-tree file.
    /// </summary>
    Task<string> GetDiffAsync(GitRepositoryInfo info, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists local branches in commit-time order (most recent tip first).
    /// </summary>
    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks out an existing local branch.
    /// </summary>
    Task<GitCheckoutResult> CheckoutBranchAsync(GitRepositoryInfo info, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the commit history for all branches (topo-order), enriched with
    /// parent SHAs and ref decorations for graph and badge rendering.
    /// </summary>
    Task<IReadOnlyList<GitCommit>> GetHistoryAsync(GitRepositoryInfo info, int limit = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the unified-diff text for a commit (diff against its first parent).
    /// </summary>
    Task<string> GetCommitDiffAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full <c>git format-patch -1 --stdout</c> output for a single commit.
    /// </summary>
    Task<string> FormatPatchAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>git fetch --all --prune</c>.
    /// </summary>
    Task<GitFetchResult> FetchAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>git pull --rebase --autostash</c>.
    /// </summary>
    Task<GitPullResult> PullAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a lightweight or annotated tag at the given commit SHA.</summary>
    Task<GitSimpleResult> TagCommitAsync(GitRepositoryInfo info, string sha, string tagName, string? annotation = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a new branch pointing at the given commit SHA.</summary>
    Task<GitSimpleResult> CreateBranchAtAsync(GitRepositoryInfo info, string sha, string branchName, CancellationToken cancellationToken = default);

    /// <summary>Checks out a commit in detached-HEAD mode.</summary>
    Task<GitSimpleResult> CheckoutCommitAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default);

    /// <summary>Cherry-picks the given commit onto HEAD.</summary>
    Task<GitSimpleResult> CherryPickAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default);

    /// <summary>Reverts the given commit (creates a new revert commit).</summary>
    Task<GitSimpleResult> RevertCommitAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default);

    /// <summary>Merges the given commit into HEAD.</summary>
    Task<GitSimpleResult> MergeCommitAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default);

    /// <summary>Rebases the current branch onto the given commit.</summary>
    Task<GitSimpleResult> RebaseOntoAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default);

    /// <summary>Resets HEAD to the given commit with the specified mode.</summary>
    Task<GitSimpleResult> ResetToAsync(GitRepositoryInfo info, string sha, GitResetMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites the commit message for the given SHA. Uses amend for HEAD;
    /// uses interactive rebase with a non-interactive sequence+message editor for any older commit.
    /// </summary>
    Task<GitSimpleResult> EditCommitMessageAsync(GitRepositoryInfo info, string sha, string newMessage, CancellationToken cancellationToken = default);

    /// <summary>Returns the full commit message body (subject + body) for the given SHA.</summary>
    Task<string> GetCommitMessageAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IGitService.CommitAsync"/>.</summary>
public sealed record GitCommitResult(bool Success, string? CommitSha, string Output);

/// <summary>Outcome of <see cref="IGitService.PushAsync"/>.</summary>
public sealed record GitPushResult(bool Success, string Output);

/// <summary>One local branch.</summary>
public sealed record GitBranch(string Name, bool IsCurrent, string? TipSha);

/// <summary>Outcome of <see cref="IGitService.FetchAsync"/>.</summary>
public sealed record GitFetchResult(bool Success, string Output);

/// <summary>Outcome of <see cref="IGitService.PullAsync"/>.</summary>
public sealed record GitPullResult(bool Success, string Output);

/// <summary>
/// One commit entry from the history walk.
/// The positional constructor preserves backward compatibility; the init properties
/// carry enriched data populated by the git-CLI history parser.
/// </summary>
public sealed record GitCommit(
    string Sha,
    string Subject,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset When)
{
    /// <summary>Full 40-char SHA.</summary>
    public string FullSha { get; init; } = "";

    /// <summary>Full SHAs of parent commits (empty for root commits).</summary>
    public IReadOnlyList<string> ParentShas { get; init; } = [];

    /// <summary>Ref decorations (branches, tags, HEAD) pointing at this commit.</summary>
    public IReadOnlyList<CommitRef> Refs { get; init; } = [];
}

/// <summary>Outcome of <see cref="IGitService.CheckoutBranchAsync"/>.</summary>
public sealed record GitCheckoutResult(bool Success, string? ErrorMessage);

/// <summary>git user.name / user.email pair.</summary>
public sealed record GitIdentity(string Name, string Email);

/// <summary>Where to write a git config value.</summary>
public enum GitConfigScope
{
    Local,
    Global,
}

/// <summary>Outcome of <see cref="IGitService.TestAuthAsync"/>.</summary>
public sealed record GitAuthResult(bool Success, string Output);

/// <summary>Generic success/output result for simple git operations.</summary>
public sealed record GitSimpleResult(bool Success, string Output);

/// <summary>Reset mode for <see cref="IGitService.ResetToAsync"/>.</summary>
public enum GitResetMode
{
    Soft,
    Mixed,
    Hard,
}
