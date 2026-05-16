namespace CimianStudio.Core.Services;

using CimianStudio.Core.Models.Git;

/// <summary>
/// Discovers and reads the canonical git hooks for a repository.
/// Pure file I/O — no LibGit2Sharp dependency.
/// </summary>
public interface IGitHooksService
{
    /// <summary>
    /// Resolves the effective hooks directory for <paramref name="gitRoot"/> using this priority:
    /// <list type="number">
    ///   <item><c>core.hooksPath</c> from <c>.git/config</c> (relative paths resolved against <paramref name="gitRoot"/>)</item>
    ///   <item><c>.githooks/</c> at <paramref name="gitRoot"/> if it exists (version-controlled convention)</item>
    ///   <item><c>.git/hooks/</c> (git default)</item>
    /// </list>
    /// </summary>
    Task<string> DiscoverHooksDirAsync(string gitRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the five canonical hooks found under <paramref name="hooksDir"/>,
    /// with state classification and content pre-loaded.
    /// </summary>
    Task<IReadOnlyList<GitHook>> GetHooksAsync(string hooksDir, CancellationToken cancellationToken = default);
}
