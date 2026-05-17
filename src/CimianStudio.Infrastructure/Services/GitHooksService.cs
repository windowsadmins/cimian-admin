namespace CimianStudio.Infrastructure.Services;

using System.Diagnostics;
using System.Text;
using CimianStudio.Core.Models.Git;
using CimianStudio.Core.Services;

public sealed class GitHooksService : IGitHooksService
{
    // Single source of truth for the "all standard hooks" list lives in
    // GitHookCatalog (Core layer). Local array copy lets us use Array.IndexOf
    // for the canonical-order tie-break in sort without per-call allocation.
    private static readonly string[] CanonicalHooks = [.. GitHookCatalog.AllNames];

    public Task<HooksDirInfo> DiscoverHooksDirAsync(
        string gitRoot,
        string? overridePath = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Explicit settings override.
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var resolved = Path.IsPathRooted(overridePath)
                ? overridePath
                : Path.GetFullPath(Path.Combine(gitRoot, overridePath));
            return Task.FromResult(new HooksDirInfo(resolved, HooksDirSource.SettingsOverride));
        }

        // 2. core.hooksPath from git config (handles includes that direct .git/config parse misses).
        var fromConfig = ReadCoreHooksPathViaGit(gitRoot);
        if (fromConfig is not null)
        {
            var resolved = Path.IsPathRooted(fromConfig)
                ? fromConfig
                : Path.GetFullPath(Path.Combine(gitRoot, fromConfig));
            return Task.FromResult(new HooksDirInfo(resolved, HooksDirSource.GitConfigHooksPath));
        }

        // 3. Version-controlled .githooks/ at repo root.
        var versioned = Path.Combine(gitRoot, ".githooks");
        if (Directory.Exists(versioned))
            return Task.FromResult(new HooksDirInfo(versioned, HooksDirSource.VersionControlled));

        // 4. Default via git rev-parse --git-path hooks.
        var defaultPath = ResolveDefaultHooksPath(gitRoot);
        return Task.FromResult(new HooksDirInfo(defaultPath, HooksDirSource.Default));
    }

    public async Task<IReadOnlyList<GitHook>> GetHooksAsync(
        string hooksDir,
        CancellationToken cancellationToken = default)
    {
        var foundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hooks = new List<GitHook>();

        if (Directory.Exists(hooksDir))
        {
            foreach (var filePath in Directory.EnumerateFiles(hooksDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(filePath);

                // Skip .sample files that shadow a canonical hook — we'll
                // add them under the canonical hook classification below.
                GitHookState state;
                string baseName;
                if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = fileName[..^".disabled".Length];
                    state = GitHookState.Disabled;
                }
                else if (fileName.EndsWith(".sample", StringComparison.OrdinalIgnoreCase))
                {
                    baseName = fileName[..^".sample".Length];
                    state = GitHookState.SampleOnly;
                }
                else
                {
                    baseName = fileName;
                    state = GitHookState.Active;
                }

                // Prefer Active over Disabled over SampleOnly when multiple variants exist.
                if (foundNames.Contains(baseName))
                {
                    var existing = hooks.FindIndex(h => string.Equals(h.Name, baseName, StringComparison.OrdinalIgnoreCase));
                    if (existing >= 0 && state < hooks[existing].State)
                        continue; // keep higher-priority state
                }

                foundNames.Add(baseName);
                var content = await ReadUtf8Async(filePath, cancellationToken).ConfigureAwait(false);
                var hook = new GitHook
                {
                    Name = baseName,
                    AbsolutePath = filePath,
                    State = state,
                    Language = DetectLanguage(content),
                    Content = content,
                };
                var idx = hooks.FindIndex(h => string.Equals(h.Name, baseName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) hooks[idx] = hook;
                else hooks.Add(hook);
            }
        }

        // Ensure all canonical hooks appear (as Inactive if not present on disk).
        foreach (var name in CanonicalHooks)
        {
            if (!foundNames.Contains(name))
            {
                hooks.Add(new GitHook
                {
                    Name = name,
                    AbsolutePath = Path.Combine(hooksDir, name),
                    State = GitHookState.Inactive,
                    Language = GitHookLanguage.Unknown,
                    Content = null,
                });
            }
        }

        // Sort: Active first (then Disabled, SampleOnly, Inactive), tie-break by
        // canonical order, then alphabetical. The enum is Inactive=0 .. Active=3,
        // so descending on state value floats Active to the top.
        hooks.Sort((a, b) =>
        {
            var stateCmp = b.State.CompareTo(a.State);
            if (stateCmp != 0) return stateCmp;
            var ai = Array.IndexOf(CanonicalHooks, a.Name);
            var bi = Array.IndexOf(CanonicalHooks, b.Name);
            if (ai >= 0 && bi >= 0) return ai.CompareTo(bi);
            if (ai >= 0) return -1;
            if (bi >= 0) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return hooks;
    }

    public async Task SaveHookAsync(
        string absolutePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        ArgumentNullException.ThrowIfNull(content);

        var dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Normalize to LF line endings.
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal)
                                .Replace('\r', '\n');
        await File.WriteAllTextAsync(absolutePath, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken)
                  .ConfigureAwait(false);
    }

    public Task SetHookActiveAsync(
        string absolutePath,
        bool active,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        // Derive the base name regardless of which variant was passed.
        string basePath;
        if (absolutePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            basePath = absolutePath[..^".disabled".Length];
        else if (absolutePath.EndsWith(".sample", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask; // samples are immutable
        else
            basePath = absolutePath;

        var activePath = basePath;
        var disabledPath = basePath + ".disabled";

        if (active)
        {
            if (File.Exists(disabledPath) && !File.Exists(activePath))
                File.Move(disabledPath, activePath);
        }
        else
        {
            if (File.Exists(activePath))
                File.Move(activePath, disabledPath, overwrite: false);
        }

        return Task.CompletedTask;
    }

    // Shells out to git config to get core.hooksPath, handling git includes.
    private static string? ReadCoreHooksPathViaGit(string gitRoot)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = gitRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("config");
            psi.ArgumentList.Add("--get");
            psi.ArgumentList.Add("core.hooksPath");

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            return proc.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Resolves the default hooks dir via git rev-parse --git-path hooks (worktree-aware).
    private static string ResolveDefaultHooksPath(string gitRoot)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = gitRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("--git-path");
            psi.ArgumentList.Add("hooks");

            using var proc = Process.Start(psi);
            if (proc is null) return Path.Combine(gitRoot, ".git", "hooks");
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                return Path.IsPathRooted(output)
                    ? output
                    : Path.GetFullPath(Path.Combine(gitRoot, output));
            }
        }
        catch (Exception) { }

        return Path.Combine(gitRoot, ".git", "hooks");
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
