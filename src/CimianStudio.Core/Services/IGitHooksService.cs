namespace CimianStudio.Core.Services;

using CimianStudio.Core.Models.Git;

/// <summary>
/// Discovers, reads, writes, and toggles git hooks for a repository.
/// Pure file I/O — no LibGit2Sharp dependency.
/// </summary>
public interface IGitHooksService
{
    /// <summary>
    /// Resolves the effective hooks directory using this priority chain:
    /// <list type="number">
    ///   <item><paramref name="overridePath"/> if non-empty (absolute or repo-relative).</item>
    ///   <item><c>git config --get core.hooksPath</c> if set.</item>
    ///   <item><c>.githooks/</c> at <paramref name="gitRoot"/> if it exists.</item>
    ///   <item>Default via <c>git rev-parse --git-path hooks</c>.</item>
    /// </list>
    /// Returns a <see cref="HooksDirInfo"/> with the resolved absolute path and which source was used.
    /// </summary>
    Task<HooksDirInfo> DiscoverHooksDirAsync(
        string gitRoot,
        string? overridePath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every file found in <paramref name="hooksDir"/> classified by state,
    /// plus <see cref="GitHookState.Inactive"/> entries for canonical hooks not present.
    /// </summary>
    Task<IReadOnlyList<GitHook>> GetHooksAsync(
        string hooksDir,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="absolutePath"/> using
    /// UTF-8 with LF line endings. Creates parent directories if necessary.
    /// </summary>
    Task SaveHookAsync(
        string absolutePath,
        string content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates or deactivates a hook by renaming to/from the <c>.disabled</c> suffix.
    /// <paramref name="absolutePath"/> may be the active path or the <c>.disabled</c> path;
    /// the base name is inferred automatically.
    /// </summary>
    Task SetHookActiveAsync(
        string absolutePath,
        bool active,
        CancellationToken cancellationToken = default);
}
