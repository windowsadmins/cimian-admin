namespace CimianStudio.Core.Models.Git;

public enum CommitRefKind
{
    Head,
    LocalBranch,
    RemoteBranch,
    Tag,
}

public sealed record CommitRef(string Label, CommitRefKind Kind, bool IsHeadTarget);
