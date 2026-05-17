namespace CimianStudio.Core.Models.Git;

public enum HooksDirSource
{
    SettingsOverride,
    GitConfigHooksPath,
    VersionControlled,
    Default,
}

public sealed record HooksDirInfo(string AbsolutePath, HooksDirSource Source)
{
    public string SourceLabel => Source switch
    {
        HooksDirSource.SettingsOverride => "Settings override",
        HooksDirSource.GitConfigHooksPath => "core.hooksPath",
        HooksDirSource.VersionControlled => "Version-controlled (.githooks)",
        HooksDirSource.Default => "Default (.git/hooks)",
        _ => "Unknown",
    };
}
