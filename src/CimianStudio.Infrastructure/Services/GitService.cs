namespace CimianStudio.Infrastructure.Services;

using System.Diagnostics;
using System.Text;
using CultureInfo = System.Globalization.CultureInfo;
using CimianStudio.Core.Models.Git;
using CimianStudio.Core.Services;
using LibGit2Sharp;

/// <summary>
/// LibGit2Sharp-backed read-only git service. Native binaries ship with the
/// LibGit2Sharp NuGet so no <c>git.exe</c> is required for status queries.
/// All public methods swallow LibGit2Sharp exceptions and return empty/null
/// results — git visibility is best-effort and must not break the editor flow.
/// </summary>
public sealed class GitService : IGitService
{
    public Task<GitRepositoryInfo?> DiscoverAsync(string deploymentRoot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => DiscoverCore(deploymentRoot), cancellationToken);
    }

    public Task<IReadOnlyList<GitStatusEntry>> GetStatusAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run<IReadOnlyList<GitStatusEntry>>(() => GetStatusCore(info), cancellationToken);
    }

    public Task<bool> IsFileModifiedAsync(GitRepositoryInfo info, string absoluteFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        return Task.Run(() => IsFileModifiedCore(info, absoluteFilePath), cancellationToken);
    }

    public Task StageAsync(GitRepositoryInfo info, IEnumerable<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(relativePaths);
        var paths = relativePaths.ToList();
        return Task.Run(() => StageCore(info, paths), cancellationToken);
    }

    public Task<GitCommitResult> CommitAsync(GitRepositoryInfo info, string subject, string? body, bool runHooks, bool amend = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return Task.Run(() => CommitCore(info, subject, body, runHooks, amend, progress), cancellationToken);
    }

    public Task<GitPushResult> PushAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() => PushCore(info, progress), cancellationToken);
    }

    public Task<GitIdentity> GetIdentityAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            // `git config --get` exits 1 when the key is unset and writes nothing to
            // stdout — but other failures (git missing, locked .gitconfig) also
            // produce non-zero exits with error text in stderr. Treat any non-zero
            // exit as "unset" so we never surface a stderr string as the user's name.
            var nameResult = RunGit(info.GitRoot, ["config", "--get", "user.name"]);
            var emailResult = RunGit(info.GitRoot, ["config", "--get", "user.email"]);
            var name = nameResult.ExitCode == 0 ? nameResult.Output.Trim() : string.Empty;
            var email = emailResult.ExitCode == 0 ? emailResult.Output.Trim() : string.Empty;
            return new GitIdentity(name, email);
        }, cancellationToken);
    }

    public Task SetIdentityAsync(GitRepositoryInfo info, string name, string email, GitConfigScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return Task.Run(() =>
        {
            var scopeFlag = scope == GitConfigScope.Global ? "--global" : "--local";
            // Check each git config exit code — RunGit doesn't throw on non-zero, so
            // without these we'd silently tell the UI "saved" when nothing wrote.
            var nameResult = RunGit(info.GitRoot, ["config", scopeFlag, "user.name", name]);
            if (nameResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"git config user.name failed (exit {nameResult.ExitCode}): {nameResult.Output}");
            }
            var emailResult = RunGit(info.GitRoot, ["config", scopeFlag, "user.email", email]);
            if (emailResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"git config user.email failed (exit {emailResult.ExitCode}): {emailResult.Output}");
            }
        }, cancellationToken);
    }

    public Task<GitAuthResult> TestAuthAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            // ls-remote is the lightest-weight network probe that still exercises auth
            // and TLS — no commits or refs are written.
            var (exit, output) = RunGitStreaming(info.GitRoot, ["ls-remote", "--heads", "origin"], progress);
            return new GitAuthResult(exit == 0, output);
        }, cancellationToken);
    }

    public Task<string> GetDiffAsync(GitRepositoryInfo info, string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Task.Run(() => GetDiffCore(info, relativePath), cancellationToken);
    }

    public Task<IReadOnlyList<GitBranch>> GetBranchesAsync(GitRepositoryInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run<IReadOnlyList<GitBranch>>(() => GetBranchesCore(info), cancellationToken);
    }

    public Task<GitCheckoutResult> CheckoutBranchAsync(GitRepositoryInfo info, string branchName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        return Task.Run(() => CheckoutBranchCore(info, branchName), cancellationToken);
    }

    public Task<IReadOnlyList<GitCommit>> GetHistoryAsync(GitRepositoryInfo info, int limit = 200, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run<IReadOnlyList<GitCommit>>(() => GetHistoryCore(info, limit), cancellationToken);
    }

    public Task<string> GetCommitDiffAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        return Task.Run(() => GetCommitDiffCore(info, sha), cancellationToken);
    }

    public Task<string> FormatPatchAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        return Task.Run(() => FormatPatchCore(info, sha), cancellationToken);
    }

    public Task<GitFetchResult> FetchAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var (code, output) = RunGitStreaming(info.GitRoot, ["fetch", "--all", "--prune", "--progress"], progress);
            return new GitFetchResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitPullResult> PullAsync(GitRepositoryInfo info, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            // --rebase + --autostash is the user's preferred default: keeps history
            // linear and survives a dirty working tree without manual stash gymnastics.
            var (code, output) = RunGitStreaming(info.GitRoot, ["pull", "--rebase", "--autostash", "--progress"], progress);
            return new GitPullResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> TagCommitAsync(GitRepositoryInfo info, string sha, string tagName, string? annotation = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var args = string.IsNullOrWhiteSpace(annotation)
                ? (IEnumerable<string>)["tag", tagName, sha]
                : ["tag", "-a", tagName, sha, "-m", annotation];
            var (code, output) = RunGit(info.GitRoot, args);
            return new GitSimpleResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> CreateBranchAtAsync(GitRepositoryInfo info, string sha, string branchName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var (code, output) = RunGit(info.GitRoot, ["branch", branchName, sha]);
            return new GitSimpleResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> CheckoutCommitAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var (code, output) = RunGit(info.GitRoot, ["checkout", sha]);
            return new GitSimpleResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> CherryPickAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var (code, output) = RunGit(info.GitRoot, ["cherry-pick", sha]);
            return new GitSimpleResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> RevertCommitAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var (code, output) = RunGit(info.GitRoot, ["revert", "--no-edit", sha]);
            return new GitSimpleResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> MergeCommitAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var (code, output) = RunGit(info.GitRoot, ["merge", sha]);
            return new GitSimpleResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> RebaseOntoAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var (code, output) = RunGit(info.GitRoot, ["rebase", sha]);
            return new GitSimpleResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> ResetToAsync(GitRepositoryInfo info, string sha, GitResetMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var modeFlag = mode switch
            {
                GitResetMode.Soft => "--soft",
                GitResetMode.Hard => "--hard",
                _ => "--mixed",
            };
            var (code, output) = RunGit(info.GitRoot, ["reset", modeFlag, sha]);
            return new GitSimpleResult(code == 0, output);
        }, cancellationToken);
    }

    public Task<GitSimpleResult> EditCommitMessageAsync(GitRepositoryInfo info, string sha, string newMessage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(newMessage);
        return Task.Run(() =>
        {
            // Is this the HEAD commit? If so, amend is simpler and safer.
            var (headCode, headOutput) = RunGit(info.GitRoot, ["rev-parse", "HEAD"]);
            if (headCode == 0)
            {
                var headSha = headOutput.Trim();
                if (headSha.StartsWith(sha, StringComparison.OrdinalIgnoreCase) ||
                    sha.StartsWith(headSha, StringComparison.OrdinalIgnoreCase))
                {
                    var (code, output) = RunGit(info.GitRoot, ["commit", "--amend", "-m", newMessage]);
                    return new GitSimpleResult(code == 0, output);
                }
            }

            // Older commit: drive rebase -i non-interactively via temp PowerShell scripts.
            var tmpDir = Path.Combine(Path.GetTempPath(), $"cimian_reword_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpDir);
            try
            {
                var msgFile = Path.Combine(tmpDir, "message.txt");
                var seqScript = Path.Combine(tmpDir, "seq_editor.ps1");
                var msgScript = Path.Combine(tmpDir, "msg_editor.ps1");

                // LF-normalized message so git doesn't warn about CR.
                var normalized = newMessage.Replace("\r\n", "\n", StringComparison.Ordinal)
                                           .Replace('\r', '\n');
                File.WriteAllText(msgFile, normalized, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                // Sequence editor: for lines whose abbreviated SHA is a prefix of our full SHA,
                // flip "pick" → "reword" so git will stop and let us supply the new message.
                var escapedMsgForRegex = msgFile.Replace("'", "''", StringComparison.Ordinal);
                File.WriteAllText(seqScript,
                    $"$fullSha='{sha}'; $f=$args[0]; " +
                    "(Get-Content $f) | ForEach-Object { " +
                    "if ($_ -match '^pick ([0-9a-f]+)' -and $fullSha.StartsWith($Matches[1])) { $_ -replace '^pick','reword' } else { $_ } " +
                    "} | Set-Content $f\n",
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                // Message editor: replace the commit-msg file git opens with our prepared message.
                File.WriteAllText(msgScript,
                    $"Set-Content -Encoding UTF8 $args[0] (Get-Content -Raw '{escapedMsgForRegex}')\n",
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                // Resolve the commit's first parent (fails if this is the root commit).
                var (pCode, pOut) = RunGit(info.GitRoot, ["rev-parse", $"{sha}^"]);
                if (pCode != 0)
                    return new GitSimpleResult(false, $"Cannot find parent of {sha[..7]}: {pOut}");
                var parentSha = pOut.Trim();

                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = info.GitRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.Environment["GIT_SEQUENCE_EDITOR"] = $"pwsh -NonInteractive -File \"{seqScript}\"";
                psi.Environment["GIT_EDITOR"] = $"pwsh -NonInteractive -File \"{msgScript}\"";
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
                psi.ArgumentList.Add("rebase");
                psi.ArgumentList.Add("-i");
                psi.ArgumentList.Add(parentSha);

                var combined = new System.Text.StringBuilder();
                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) => { if (e.Data is not null) combined.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) combined.AppendLine(e.Data); };
                if (!proc.Start()) return new GitSimpleResult(false, "git rebase failed to start");
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                return new GitSimpleResult(proc.ExitCode == 0, combined.ToString().TrimEnd());
            }
            finally
            {
                try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort cleanup */ }
            }
        }, cancellationToken);
    }

    public Task<string> GetCommitMessageAsync(GitRepositoryInfo info, string sha, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        return Task.Run(() =>
        {
            var (code, output) = RunGit(info.GitRoot, ["log", "--format=%B", "-1", sha]);
            return code == 0 ? output.TrimEnd() : string.Empty;
        }, cancellationToken);
    }

    private static GitRepositoryInfo? DiscoverCore(string deploymentRoot)
    {
        if (string.IsNullOrWhiteSpace(deploymentRoot) || !Directory.Exists(deploymentRoot))
        {
            return null;
        }

        var discovered = Repository.Discover(deploymentRoot);
        if (string.IsNullOrEmpty(discovered))
        {
            return null;
        }

        // Repository.Discover returns the path to the .git directory (with trailing
        // separator). The worktree root is its parent.
        var gitDir = discovered.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var workTree = Path.GetDirectoryName(gitDir);
        if (string.IsNullOrEmpty(workTree))
        {
            return null;
        }

        try
        {
            using var repo = new Repository(workTree);
            var head = repo.Head;
            var branchName = head?.FriendlyName == "(no branch)" ? null : head?.FriendlyName;

            int ahead = 0, behind = 0;
            var hasUpstream = head?.TrackedBranch is not null;
            if (hasUpstream)
            {
                ahead = head!.TrackingDetails.AheadBy ?? 0;
                behind = head.TrackingDetails.BehindBy ?? 0;
            }

            return new GitRepositoryInfo(
                GitRoot: workTree,
                RelativeRepoPath: ToRelativeRepoPath(workTree, deploymentRoot),
                Branch: branchName,
                AheadCount: ahead,
                BehindCount: behind,
                HasUpstream: hasUpstream);
        }
        catch (LibGit2SharpException)
        {
            return null;
        }
    }

    private static List<GitStatusEntry> GetStatusCore(GitRepositoryInfo info)
    {
        try
        {
            using var repo = new Repository(info.GitRoot);
            var options = new StatusOptions
            {
                IncludeIgnored = false,
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            };

            // LibGit2Sharp's StatusOptions.PathSpec scopes the walk to a subdirectory,
            // so we don't fan out into the whole parent repo.
            if (!string.IsNullOrEmpty(info.RelativeRepoPath))
            {
                options.PathSpec = [info.RelativeRepoPath];
            }

            var results = new List<GitStatusEntry>();
            foreach (var entry in repo.RetrieveStatus(options))
            {
                if (entry.State == FileStatus.Unaltered || entry.State == FileStatus.Ignored)
                {
                    continue;
                }

                var status = MapStatus(entry.State);
                if (status == GitFileStatus.Unchanged || status == GitFileStatus.Ignored)
                {
                    continue;
                }

                var rel = entry.FilePath.Replace('\\', '/');
                var abs = Path.GetFullPath(Path.Combine(info.GitRoot, entry.FilePath));
                var staged = (entry.State & (FileStatus.NewInIndex | FileStatus.ModifiedInIndex
                    | FileStatus.DeletedFromIndex | FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex)) != 0;

                results.Add(new GitStatusEntry(rel, abs, status, staged));
            }

            return results;
        }
        catch (LibGit2SharpException)
        {
            return [];
        }
    }

    private static bool IsFileModifiedCore(GitRepositoryInfo info, string absoluteFilePath)
    {
        try
        {
            var relative = Path.GetRelativePath(info.GitRoot, absoluteFilePath).Replace('\\', '/');

            // Reject paths that climb out of the worktree — passing "../foo" to
            // LibGit2Sharp would query a file the repo can't see and confuses status.
            if (relative.StartsWith("../", StringComparison.Ordinal) || relative == "..")
            {
                return false;
            }
            // If the deployment root is a subdirectory of the git root, the file must
            // sit under it; otherwise we'd report status for unrelated parent-repo work.
            if (!string.IsNullOrEmpty(info.RelativeRepoPath) &&
                !relative.StartsWith(info.RelativeRepoPath + "/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(relative, info.RelativeRepoPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var repo = new Repository(info.GitRoot);
            var status = repo.RetrieveStatus(relative);
            if (status == FileStatus.Unaltered || status == FileStatus.Nonexistent || status == FileStatus.Ignored)
            {
                return false;
            }
            // Treat any working-tree or index difference as "modified on disk" for the
            // purposes of the editor pill — same signal git would show in `status`.
            return true;
        }
        catch (LibGit2SharpException)
        {
            return false;
        }
    }

    private static void StageCore(GitRepositoryInfo info, List<string> relativePaths)
    {
        if (relativePaths.Count == 0) return;
        using var repo = new Repository(info.GitRoot);
        // Commands.Stage handles add, modify, and delete by reading the current
        // working-tree state for each pathspec — no need to branch on status.
        Commands.Stage(repo, relativePaths);
    }

    private static GitCommitResult CommitCore(GitRepositoryInfo info, string subject, string? body, bool runHooks, bool amend, IProgress<string>? progress)
    {
        var args = new List<string> { "commit" };
        if (!runHooks) args.Add("--no-verify");
        if (amend) args.Add("--amend");
        args.Add("-m");
        args.Add(subject);
        if (!string.IsNullOrWhiteSpace(body))
        {
            args.Add("-m");
            args.Add(body);
        }

        var (exit, output) = RunGitStreaming(info.GitRoot, args, progress);
        if (exit != 0)
        {
            return new GitCommitResult(false, null, output);
        }

        string? sha = null;
        try
        {
            using var repo = new Repository(info.GitRoot);
            sha = repo.Head?.Tip?.Sha[..12];
        }
        catch (LibGit2SharpException)
        {
            // Couldn't read tip — commit still succeeded.
        }
        return new GitCommitResult(true, sha, output);
    }

    private static GitPushResult PushCore(GitRepositoryInfo info, IProgress<string>? progress)
    {
        // GIT_PROGRESS_NO_FORCE_UPDATE is the closest thing to a "give me steady
        // updates" knob on Windows git; combined with progress=true this gives us
        // periodic counter lines.
        var (exit, output) = RunGitStreaming(info.GitRoot, ["push", "--progress"], progress);
        return new GitPushResult(exit == 0, output);
    }

    private static string GetDiffCore(GitRepositoryInfo info, string relativePath)
    {
        try
        {
            using var repo = new Repository(info.GitRoot);
            var status = repo.RetrieveStatus(relativePath);

            // Untracked: show the file contents prefixed with "+ " (capped to keep huge
            // binaries from blowing up the UI).
            if ((status & FileStatus.NewInWorkdir) != 0 && (status & FileStatus.NewInIndex) == 0)
            {
                var abs = Path.GetFullPath(Path.Combine(info.GitRoot, relativePath));
                return RenderUntrackedFile(abs);
            }

            var options = new CompareOptions
            {
                ContextLines = 3,
                InterhunkLines = 1,
            };
            var paths = new[] { relativePath };
            // Compare HEAD tree to working dir so the user sees both staged and unstaged
            // edits together — that matches what `git diff HEAD -- <path>` shows.
            using var patch = repo.Diff.Compare<Patch>(repo.Head?.Tip?.Tree, DiffTargets.WorkingDirectory, paths, null, options);
            var entry = patch.FirstOrDefault();
            if (entry is null) return string.Empty;
            if (entry.IsBinaryComparison) return "(binary file changed)";
            return entry.Patch;
        }
        catch (LibGit2SharpException ex)
        {
            return $"(failed to diff: {ex.Message})";
        }
    }

    private static string RenderUntrackedFile(string absolutePath)
    {
        const int maxBytes = 64 * 1024; // 64 KB cap for the diff panel.
        try
        {
            var info = new FileInfo(absolutePath);
            if (!info.Exists) return "(file no longer exists)";
            if (info.Length == 0) return "(new file, empty)";
            if (LooksBinary(absolutePath)) return $"(new binary file, {info.Length:N0} bytes)";

            var bytesToRead = (int)Math.Min(info.Length, maxBytes);
            using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[bytesToRead];
            var read = stream.Read(buffer, 0, bytesToRead);
            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);

            var sb = new StringBuilder(text.Length + 128);
            sb.Append("(new file: ").Append(Path.GetFileName(absolutePath)).Append(", ")
              .Append(info.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" bytes)");
            foreach (var line in text.Split('\n'))
            {
                sb.Append("+ ").Append(line.TrimEnd('\r')).Append('\n');
            }
            if (info.Length > maxBytes) sb.AppendLine("…(truncated)");
            return sb.ToString();
        }
        catch (IOException ex)
        {
            return $"(failed to read new file: {ex.Message})";
        }
    }

    private static bool LooksBinary(string path)
    {
        // Cheap heuristic: any NUL byte in the first 8 KB makes it binary.
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> buf = stackalloc byte[8192];
            var read = fs.Read(buf);
            for (var i = 0; i < read; i++)
            {
                if (buf[i] == 0) return true;
            }
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string GetCommitDiffCore(GitRepositoryInfo info, string sha)
    {
        try
        {
            using var repo = new Repository(info.GitRoot);
            var commit = repo.Lookup<Commit>(sha);
            if (commit is null)
            {
                return string.Empty;
            }

            // Root commits have no parent — diff against the empty tree so the entire
            // commit shows as additions, matching what `git show <root-sha>` does.
            var parentTree = commit.Parents.FirstOrDefault()?.Tree;
            var patch = parentTree is null
                ? repo.Diff.Compare<Patch>(null, commit.Tree, new CompareOptions { ContextLines = 3 })
                : repo.Diff.Compare<Patch>(parentTree, commit.Tree, new CompareOptions { ContextLines = 3 });
            return patch.Content;
        }
        catch (LibGit2SharpException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Mirrors <c>git format-patch -1 --stdout &lt;sha&gt;</c>: an mbox-style header
    /// (From line + author + date + subject), the commit body, a <c>---</c> separator,
    /// then the unified diff. Returns empty when the sha can't be resolved.
    /// </summary>
    private static string FormatPatchCore(GitRepositoryInfo info, string sha)
    {
        try
        {
            using var repo = new Repository(info.GitRoot);
            var commit = repo.Lookup<Commit>(sha);
            if (commit is null)
            {
                return string.Empty;
            }

            var parentTree = commit.Parents.FirstOrDefault()?.Tree;
            var patch = parentTree is null
                ? repo.Diff.Compare<Patch>(null, commit.Tree, new CompareOptions { ContextLines = 3 })
                : repo.Diff.Compare<Patch>(parentTree, commit.Tree, new CompareOptions { ContextLines = 3 });

            var message = (commit.Message ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
            var subjectEnd = message.IndexOf('\n', StringComparison.Ordinal);
            var subject = (subjectEnd < 0 ? message : message[..subjectEnd]).TrimEnd();
            var body = subjectEnd < 0 ? string.Empty : message[(subjectEnd + 1)..].TrimStart('\n');

            // git's "From <sha> Mon Sep 17 00:00:00 2001" line is a fixed marker date,
            // not the commit date — copying that exactly lets `git am` recognise the
            // patch as a mailbox. The Date: header carries the real author timestamp,
            // formatted to match git's RFC-2822-ish layout (timezone as +HHMM, no colon).
            var author = commit.Author;
            var when = author.When.ToString("ddd, d MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture)
                     + author.When.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", string.Empty, StringComparison.Ordinal);

            var sb = new System.Text.StringBuilder();
            sb.Append("From ").Append(commit.Sha).Append(" Mon Sep 17 00:00:00 2001\n");
            sb.Append("From: ").Append(author.Name).Append(" <").Append(author.Email).Append(">\n");
            sb.Append("Date: ").Append(when).Append('\n');
            sb.Append("Subject: [PATCH] ").Append(subject).Append("\n\n");
            if (!string.IsNullOrEmpty(body))
            {
                sb.Append(body.TrimEnd('\n')).Append("\n\n");
            }
            sb.Append("---\n");
            sb.Append(patch.Content);
            sb.Append("\n-- \n");
            // LibGit2Sharp doesn't expose libgit2's build version cleanly; stamp a
            // generic trailer so the patch parses as mbox without lying about a tool.
            sb.Append("2.0\n");
            return sb.ToString();
        }
        catch (LibGit2SharpException)
        {
            return string.Empty;
        }
    }

    // Field separator (ASCII Unit Separator) and record separator used in git log format.
    private const char FieldSep = '\x1f';
    private const char RecordSep = '\x1e';

    private static List<GitCommit> GetHistoryCore(GitRepositoryInfo info, int limit)
    {
        if (limit <= 0) return [];

        // %H=full sha, %an=author name, %ae=author email, %aI=ISO8601 author date,
        // %P=parent shas (space-separated), %D=ref names, %s=subject
        var format = $"%H{FieldSep}%an{FieldSep}%ae{FieldSep}%aI{FieldSep}%P{FieldSep}%D{FieldSep}%s{RecordSep}";
        var (code, output) = RunGit(info.GitRoot,
            ["log", "--all", "--topo-order", "--decorate=full",
             $"--max-count={limit}", $"--pretty=format:{format}"]);

        if (code != 0 || string.IsNullOrEmpty(output)) return [];

        var result = new List<GitCommit>();
        foreach (var record in output.Split(RecordSep, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = record.Trim('\n', '\r', ' ');
            if (string.IsNullOrEmpty(line)) continue;

            var fields = line.Split(FieldSep);
            if (fields.Length < 7) continue;

            var fullSha = fields[0].Trim();
            if (fullSha.Length < 12) continue;

            var authorName = fields[1];
            var authorEmail = fields[2];
            DateTimeOffset when = DateTimeOffset.TryParse(
                fields[3], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt) ? dt : DateTimeOffset.MinValue;

            var parentShas = fields[4].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var refsStr = fields[5];
            var subject = fields[6];

            result.Add(new GitCommit(
                Sha: fullSha[..12],
                Subject: subject,
                AuthorName: authorName,
                AuthorEmail: authorEmail,
                When: when)
            {
                FullSha = fullSha,
                ParentShas = parentShas,
                Refs = ParseDecoratedRefs(refsStr),
            });
        }
        return result;
    }

    private static List<CommitRef> ParseDecoratedRefs(string refsStr)
    {
        if (string.IsNullOrWhiteSpace(refsStr)) return [];
        var result = new List<CommitRef>();
        foreach (var part in refsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "HEAD -> refs/heads/main"
            if (part.StartsWith("HEAD -> ", StringComparison.Ordinal))
            {
                result.Add(new CommitRef("HEAD", CommitRefKind.Head, IsHeadTarget: false));
                var target = part["HEAD -> ".Length..];
                result.Add(new CommitRef(FriendlyRef(target), CommitRefKind.LocalBranch, IsHeadTarget: true));
                continue;
            }
            // Detached HEAD
            if (part == "HEAD")
            {
                result.Add(new CommitRef("HEAD", CommitRefKind.Head, IsHeadTarget: false));
                continue;
            }
            // "tag: refs/tags/v1.0"
            if (part.StartsWith("tag: ", StringComparison.Ordinal))
            {
                result.Add(new CommitRef(FriendlyRef(part["tag: ".Length..]), CommitRefKind.Tag, IsHeadTarget: false));
                continue;
            }
            // "refs/remotes/origin/main"
            if (part.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                result.Add(new CommitRef(part["refs/remotes/".Length..], CommitRefKind.RemoteBranch, IsHeadTarget: false));
                continue;
            }
            // "refs/heads/main"
            if (part.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                result.Add(new CommitRef(part["refs/heads/".Length..], CommitRefKind.LocalBranch, IsHeadTarget: false));
                continue;
            }
        }
        return result;
    }

    private static string FriendlyRef(string refName)
    {
        if (refName.StartsWith("refs/heads/", StringComparison.Ordinal))  return refName["refs/heads/".Length..];
        if (refName.StartsWith("refs/remotes/", StringComparison.Ordinal)) return refName["refs/remotes/".Length..];
        if (refName.StartsWith("refs/tags/", StringComparison.Ordinal))   return refName["refs/tags/".Length..];
        return refName;
    }

    private static List<GitBranch> GetBranchesCore(GitRepositoryInfo info)
    {
        try
        {
            using var repo = new Repository(info.GitRoot);
            var head = repo.Head?.FriendlyName;
            return [.. repo.Branches
                .Where(b => !b.IsRemote)
                .OrderByDescending(b => b.Tip?.Committer.When ?? DateTimeOffset.MinValue)
                .Select(b => new GitBranch(
                    Name: b.FriendlyName,
                    IsCurrent: string.Equals(b.FriendlyName, head, StringComparison.Ordinal),
                    TipSha: b.Tip?.Sha[..12]))];
        }
        catch (LibGit2SharpException)
        {
            return [];
        }
    }

    private static GitCheckoutResult CheckoutBranchCore(GitRepositoryInfo info, string branchName)
    {
        try
        {
            using var repo = new Repository(info.GitRoot);

            // Refuse if the working tree has dirty changes — git would overwrite them
            // silently otherwise, which is data loss from the user's point of view.
            var dirty = repo.RetrieveStatus(new StatusOptions { IncludeIgnored = false, IncludeUntracked = false })
                .Any(e => (e.State & (FileStatus.ModifiedInWorkdir | FileStatus.DeletedFromWorkdir
                    | FileStatus.RenamedInWorkdir | FileStatus.TypeChangeInWorkdir
                    | FileStatus.ModifiedInIndex | FileStatus.NewInIndex | FileStatus.DeletedFromIndex
                    | FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex)) != 0);
            if (dirty)
            {
                return new GitCheckoutResult(false,
                    "Working tree has uncommitted changes. Commit or stash them before switching branches.");
            }

            var branch = repo.Branches[branchName];
            if (branch is null)
            {
                return new GitCheckoutResult(false, $"Branch '{branchName}' not found.");
            }

            Commands.Checkout(repo, branch);
            return new GitCheckoutResult(true, null);
        }
        catch (CheckoutConflictException ex)
        {
            return new GitCheckoutResult(false, $"Checkout conflict: {ex.Message}");
        }
        catch (LibGit2SharpException ex)
        {
            return new GitCheckoutResult(false, ex.Message);
        }
    }

    private static (int ExitCode, string Output) RunGit(string workingDir, IEnumerable<string> args) =>
        RunGitStreaming(workingDir, args, progress: null);

    /// <summary>
    /// Runs <c>git</c> with arguments, streaming each stdout/stderr line to
    /// <paramref name="progress"/> while also accumulating into a final combined
    /// output string. Used for long operations (commit with hooks, push) where the
    /// UI needs live feedback.
    /// </summary>
    private static (int ExitCode, string Output) RunGitStreaming(
        string workingDir,
        IEnumerable<string> args,
        IProgress<string>? progress)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        var combined = new StringBuilder();
        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            combined.AppendLine(e.Data);
            progress?.Report(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            combined.AppendLine(e.Data);
            progress?.Report(e.Data);
        };

        try
        {
            if (!proc.Start())
            {
                return (-1, "git failed to start");
            }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            proc.WaitForExit();
            return (proc.ExitCode, combined.ToString().TrimEnd());
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // git.exe missing from PATH.
            return (-1, $"git not found on PATH: {ex.Message}");
        }
    }

    private static string ToRelativeRepoPath(string gitRoot, string deploymentRoot)
    {
        var rel = Path.GetRelativePath(gitRoot, deploymentRoot).Replace('\\', '/');
        return rel == "." ? string.Empty : rel.TrimEnd('/');
    }

    private static GitFileStatus MapStatus(FileStatus state)
    {
        // Order matters: check conflict first (it overlaps with other bits), then
        // the most specific working-tree/index categories.
        if ((state & FileStatus.Conflicted) != 0) return GitFileStatus.Conflicted;
        if ((state & (FileStatus.NewInWorkdir | FileStatus.NewInIndex)) != 0)
        {
            return (state & FileStatus.NewInWorkdir) != 0 && (state & FileStatus.NewInIndex) == 0
                ? GitFileStatus.Untracked
                : GitFileStatus.Added;
        }
        if ((state & (FileStatus.DeletedFromIndex | FileStatus.DeletedFromWorkdir)) != 0) return GitFileStatus.Deleted;
        if ((state & (FileStatus.RenamedInIndex | FileStatus.RenamedInWorkdir)) != 0) return GitFileStatus.Renamed;
        if ((state & (FileStatus.ModifiedInIndex | FileStatus.ModifiedInWorkdir | FileStatus.TypeChangeInIndex | FileStatus.TypeChangeInWorkdir)) != 0) return GitFileStatus.Modified;
        return GitFileStatus.Unchanged;
    }
}
