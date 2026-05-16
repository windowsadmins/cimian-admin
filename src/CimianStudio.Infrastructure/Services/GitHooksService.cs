namespace CimianStudio.Infrastructure.Services;

using System.Text;
using CimianStudio.Core.Models.Git;
using CimianStudio.Core.Services;

public sealed class GitHooksService : IGitHooksService
{
    private static readonly string[] CanonicalHooks =
    [
        "pre-commit",
        "prepare-commit-msg",
        "commit-msg",
        "pre-push",
        "post-commit",
    ];

    public Task<string> DiscoverHooksDirAsync(string gitRoot, CancellationToken cancellationToken = default)
    {
        // 1. core.hooksPath in .git/config
        var configured = ReadCoreHooksPath(gitRoot);
        if (configured is not null)
        {
            var resolved = Path.IsPathRooted(configured)
                ? configured
                : Path.GetFullPath(Path.Combine(gitRoot, configured));
            return Task.FromResult(resolved);
        }

        // 2. .githooks/ at repo root (version-controlled convention)
        var versioned = Path.Combine(gitRoot, ".githooks");
        if (Directory.Exists(versioned))
            return Task.FromResult(versioned);

        // 3. Default .git/hooks/
        return Task.FromResult(Path.Combine(gitRoot, ".git", "hooks"));
    }

    public async Task<IReadOnlyList<GitHook>> GetHooksAsync(string hooksDir, CancellationToken cancellationToken = default)
    {
        var hooks = new List<GitHook>(CanonicalHooks.Length);

        foreach (var name in CanonicalHooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hooks.Add(await ClassifyHookAsync(hooksDir, name, cancellationToken).ConfigureAwait(false));
        }

        return hooks;
    }

    private static async Task<GitHook> ClassifyHookAsync(string hooksDir, string name, CancellationToken ct)
    {
        var activePath = Path.Combine(hooksDir, name);
        var disabledPath = activePath + ".disabled";
        var samplePath = activePath + ".sample";

        if (File.Exists(activePath))
        {
            var content = await ReadUtf8Async(activePath, ct).ConfigureAwait(false);
            return new GitHook
            {
                Name = name,
                AbsolutePath = activePath,
                State = GitHookState.Active,
                Language = DetectLanguage(content),
                Content = content,
            };
        }

        if (File.Exists(disabledPath))
        {
            var content = await ReadUtf8Async(disabledPath, ct).ConfigureAwait(false);
            return new GitHook
            {
                Name = name,
                AbsolutePath = disabledPath,
                State = GitHookState.Disabled,
                Language = DetectLanguage(content),
                Content = content,
            };
        }

        if (File.Exists(samplePath))
        {
            var content = await ReadUtf8Async(samplePath, ct).ConfigureAwait(false);
            return new GitHook
            {
                Name = name,
                AbsolutePath = samplePath,
                State = GitHookState.SampleOnly,
                Language = DetectLanguage(content),
                Content = content,
            };
        }

        return new GitHook
        {
            Name = name,
            AbsolutePath = activePath,
            State = GitHookState.Missing,
            Language = GitHookLanguage.Unknown,
            Content = null,
        };
    }

    // Reads [core] hooksPath from .git/config without shelling out to git.
    // Returns null if not set. The ini-style format is simple enough to parse directly.
    private static string? ReadCoreHooksPath(string gitRoot)
    {
        var configPath = Path.Combine(gitRoot, ".git", "config");
        if (!File.Exists(configPath)) return null;

        try
        {
            var inCore = false;
            foreach (var line in File.ReadAllLines(configPath, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('['))
                {
                    inCore = trimmed.StartsWith("[core]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inCore) continue;

                var eqIdx = trimmed.IndexOf('=', StringComparison.Ordinal);
                if (eqIdx < 0) continue;

                var key = trimmed[..eqIdx].Trim();
                if (!key.Equals("hooksPath", StringComparison.OrdinalIgnoreCase)) continue;

                var value = trimmed[(eqIdx + 1)..].Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }
        catch (IOException)
        {
        }

        return null;
    }

    private static async Task<string> ReadUtf8Async(string path, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static GitHookLanguage DetectLanguage(string content)
    {
        if (string.IsNullOrEmpty(content)) return GitHookLanguage.Unknown;

        var firstLine = content.Split('\n', 2)[0].Trim();
        if (!firstLine.StartsWith("#!", StringComparison.Ordinal)) return GitHookLanguage.Unknown;

        if (firstLine.Contains("pwsh", StringComparison.OrdinalIgnoreCase) ||
            firstLine.Contains("powershell", StringComparison.OrdinalIgnoreCase))
            return GitHookLanguage.Pwsh;

        if (firstLine.Contains("/bash", StringComparison.OrdinalIgnoreCase))
            return GitHookLanguage.Bash;

        if (firstLine.Contains("/sh", StringComparison.OrdinalIgnoreCase))
            return GitHookLanguage.Sh;

        return GitHookLanguage.Unknown;
    }
}
