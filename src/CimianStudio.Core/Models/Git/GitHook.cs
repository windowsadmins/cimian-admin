namespace CimianStudio.Core.Models.Git;

public enum GitHookState
{
    Inactive,
    SampleOnly,
    Disabled,
    Active,
}

public enum GitHookLanguage
{
    Pwsh,
    Sh,
    Bash,
    Unknown,
}

public sealed class GitHook
{
    public string Name { get; init; } = "";
    public string AbsolutePath { get; init; } = "";
    public GitHookState State { get; init; }
    public GitHookLanguage Language { get; init; }
    public string? Content { get; set; }
}
