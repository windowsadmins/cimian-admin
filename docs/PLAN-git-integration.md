# Plan — first-class Git integration

Status: planning (not yet implemented)
Owner: CimianStudio
Goal: CimianStudio operates as a GitOps client — fetch, pull, commit, push, hook-aware — for any repository, even when the Cimian deployment tree lives in a subdirectory of a larger git repo.

## Why

The Cimian deployment repo today lives at `Cimian/deployment/` inside a parent git repository. Admins manually `git add deployment/manifests/X.yaml` from the terminal after every CimianStudio save. The friction kills the "edit and ship" loop and invites stale, never-promoted edits. Making CimianStudio git-aware lets us:

- Show pending changes directly in the app (per-file, with diffs).
- Group multiple edits into a single semantic commit ("Promote Office to Production").
- Push without dropping to a terminal.
- Run pre-commit hooks (linters, format checks) the same way `git commit` would on the command line, so policies enforced in the parent repo aren't bypassed.

## Architecture

### Repository model

The current `CimianRepository` model points at the **deployment root** (where `catalogs/`, `manifests/`, `pkgsinfo/` live). Git status, however, is rooted at the **git worktree root**, which may be one or more directories above. Two new concepts:

- `GitRoot` — discovered by walking up from `CimianRepository.RootPath` until a `.git` directory or `.git` file (worktree pointer) is found. Cached on the repository model.
- `RelativeRepoPath` — the deployment root's path relative to `GitRoot` (e.g. `Cimian/deployment`). Used to scope status queries.

If no `GitRoot` is found, the app degrades gracefully: all git UI hides; saving works as today (write to disk, no commit).

### libgit2sharp vs shelling out

**Recommended: `LibGit2Sharp` (NuGet, MIT-licensed).** Reasons:

- No external `git.exe` dependency for the common read operations (status, diff, log).
- Same library MunkiAdmin's analogues use; battle-tested.
- We still **shell out to `git.exe`** for: `git push`, `git pull`, `git commit` when hooks need to run (LibGit2Sharp's commit doesn't trigger filter or pre-commit hooks — see "Hooks" below).

So the split is:
- LibGit2Sharp: open repo, `status`, `diff`, `log`, `branch`, fast UI rendering.
- `git.exe` shell-out: `commit`, `push`, `pull`, `fetch` — anywhere hooks or credentials need to participate.

### Service layout

```
CimianStudio.Core/Services/IGitService.cs
CimianStudio.Infrastructure/Services/GitService.cs
  - DiscoverGitRootAsync(string deploymentRoot)
  - GetStatusAsync(GitRoot root, string scopeRelative)
  - GetDiffAsync(GitRoot root, string filePath)
  - StageAsync(GitRoot root, IEnumerable<string> paths)
  - UnstageAsync(GitRoot root, IEnumerable<string> paths)
  - CommitAsync(GitRoot root, string subject, string? body, bool runHooks)
  - FetchAsync(GitRoot root, string? remote)
  - PullAsync(GitRoot root)
  - PushAsync(GitRoot root, string? remote, string? branch)
  - GetCurrentBranchAsync(GitRoot root)
  - GetRecentCommitsAsync(GitRoot root, int take)
```

All methods report progress via `IProgress<string>` so the UI can show stdout from `git.exe` calls.

## Phase 1 — read-only awareness

Goal: every page shows git context, but writes still go straight to disk.

UI:
- **Title bar**: branch name + ahead/behind indicator after the repository name (e.g. `deployment · main · ↑2 ↓0`).
- **Home page**: a "Git status" card listing modified / staged / untracked files inside the deployment scope, with click-through to the relevant editor.
- **Editor headers**: a small "modified on disk" pill when `git status` says the file differs from `HEAD`, complementing the existing in-memory "Modified" indicator.

No write operations yet. Just visibility.

## Phase 2 — commit on save

Tied to the separate "save → commit flow" plan (`PLAN-save-commit-flow.md`). Save dialog gains a "Commit this change" checkbox that, when on, opens the commit composer pre-filled with the single changed file.

## Phase 3 — full GitOps verbs

- **Fetch** button (title bar) — runs `git fetch --all`, updates ahead/behind.
- **Pull** button — runs `git pull --ff-only` (configurable). Fails loud if not fast-forward; offers "open terminal" escape hatch rather than auto-merging.
- **Push** button — runs `git push`, surfaces credentials prompts via Windows credential helper.
- **Branch picker** — list local branches; switching warns about unsaved editor state.

## Hooks

Pre-commit / commit-msg hooks frequently enforce policies the parent repo cares about (yaml lint, secret scan). Two behaviors:

1. When committing via `git.exe`, hooks run automatically. We honor exit codes and surface stderr to the user.
2. When LibGit2Sharp performs the commit (faster path, used only when the user explicitly opts out of hooks), we add `--no-verify` semantics. **Default is hooks-on.** Never silently skip hooks.

The user-level setting `gitConfigCore.hooksPath` is respected by `git.exe` — we don't try to second-guess it.

If a hook fails:
- Show the hook's stderr in an `InfoBar` with "View output" expander.
- Leave the staged state intact (so the user can fix and retry).
- **Never** auto-retry with `--no-verify`.

## Signing

If `commit.gpgsign` or `gpg.format = ssh` is set, `git.exe` handles it. We don't try to drive GPG/SSH ourselves. If signing fails, surface the error from `git.exe` and don't attempt unsigned commits.

## Credentials

For push/fetch over HTTPS, rely on the Windows credential manager via `git.exe` (it picks up `git-credential-manager`). For SSH, rely on `OpenSSH` agent. CimianStudio should never store credentials itself.

## Subfolder repos — explicit handling

When CimianStudio opens `C:\Cimian\deployment\` and the actual git root is `C:\Cimian\`:

- `GitService.GetStatusAsync(root, "Cimian/deployment")` calls `git status --porcelain -- Cimian/deployment` (pathspec-scoped).
- Diffs and commits also use the pathspec so unrelated changes elsewhere in the parent repo don't leak into our UI.
- The Home page makes the scope explicit: "Git status for `Cimian/deployment` (in `Cimian` repo)".

## Out of scope

- Merge conflict resolution UI (defer; offer "open in $EDITOR" escape hatch).
- Rebase / cherry-pick / bisect.
- Worktree management (`git worktree add` is mentioned in Cimian's CLAUDE.md but is a developer workflow, not an admin one).
- Sub-module operations.

## Open questions

1. Should fetch run automatically on app start, or only on demand? — start on-demand to avoid surprising network calls.
2. Default pull mode: `--ff-only`, `--rebase`, or `--merge`? — start with `--ff-only` and offer a setting.
3. Do we need a "discard local changes" verb? — yes, but gate it behind a confirmation dialog with full file list.
