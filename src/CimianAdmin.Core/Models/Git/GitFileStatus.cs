namespace CimianAdmin.Core.Models.Git;

/// <summary>
/// File-level git status, mirroring the porcelain-v1 letters we care about.
/// </summary>
public enum GitFileStatus
{
    Unchanged,
    Modified,
    Added,
    Deleted,
    Renamed,
    Untracked,
    Ignored,
    Conflicted,
}
