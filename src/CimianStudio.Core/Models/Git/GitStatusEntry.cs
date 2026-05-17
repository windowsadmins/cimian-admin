namespace CimianStudio.Core.Models.Git;

/// <summary>
/// One file's status in the working tree, scoped to the deployment subdirectory.
/// </summary>
/// <param name="RelativePath">Path relative to the git root (forward-slash form).</param>
/// <param name="AbsolutePath">Absolute path on disk.</param>
/// <param name="Status">Working-tree status.</param>
/// <param name="IsStaged">True if the change is currently staged for commit.</param>
public sealed record GitStatusEntry(
    string RelativePath,
    string AbsolutePath,
    GitFileStatus Status,
    bool IsStaged);
