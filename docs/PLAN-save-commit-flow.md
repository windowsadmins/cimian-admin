# Plan — save → commit flow with multi-file composer

Status: planning (not yet implemented)
Depends on: `PLAN-git-integration.md` (phases 1 & 2)

## Goal

When the user saves a pkginfo or manifest edit, offer to commit it to the git working tree. Allow batching multiple pending file changes into a single commit with a subject + body, via a checkbox list. This replaces the manual `git add … && git commit -m …` loop after every CimianStudio save.

## Trigger surfaces

The composer is reachable three ways:

1. **Save button** in `PackageEditor` / `ManifestEditor` → after writing to disk, surface an InfoBar: "Saved. **Commit this change?**" (link). Clicking opens the composer with the just-saved file pre-checked.
2. **Title bar git indicator** (from `PLAN-git-integration.md`) → clicking the modified-files badge opens the composer with all currently-modified files visible.
3. **Home page git status card** → has a "Commit…" button alongside the file list.

## Composer UI

A `ContentDialog` (`CommitComposerDialog.xaml`), sized like `CatalogCompareDialog`. Three regions:

```
┌─────────────────────────────────────────────────────────────────┐
│ Compose commit                                              [×]│
├─────────────────────────────────────────────────────────────────┤
│ Branch: main                            Pull / Push status: ↑3 │
├─────────────────────────────────────────────────────────────────┤
│ Files (3 modified, 1 untracked)                                 │
│  ▸ [✓] M  deployment/pkgsinfo/mgmt/BootstrapMate-…2332.yaml    │
│  ▸ [✓] M  deployment/manifests/Bootstrap.yaml                  │
│  ▸ [ ] M  deployment/manifests/CoreApps.yaml                   │
│  ▸ [ ] ?? deployment/pkgs/Office-365…msi                       │
├─────────────────────────────────────────────────────────────────┤
│ Subject (≤72 chars)                                             │
│ [ Promote Office and Bootstrap to Production              ]    │
│ Body (optional)                                                 │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ Bootstrap now installs Defender Onboarding by default;     │ │
│ │ Office 365 24Q2 promoted to the Production catalog.        │ │
│ │                                                            │ │
│ │ Refs: #1234                                                │ │
│ └─────────────────────────────────────────────────────────────┘ │
├─────────────────────────────────────────────────────────────────┤
│ [✓] Run pre-commit hooks   [ ] Push after commit               │
│                                                                 │
│           [ Cancel ]   [ Commit ]   [ Commit & push ]          │
└─────────────────────────────────────────────────────────────────┘
```

### Behaviors

- **File rows** are expandable: clicking the `▸` reveals an inline diff (read-only) using a monospaced text view. Diff comes from `GitService.GetDiffAsync`. Long diffs cap at ~500 lines with "Show full diff" link that opens the file in the OS's default merge tool (or just `git difftool` shell-out).
- **Status letters** match `git status --porcelain`: `M`, `A`, `D`, `R`, `??`.
- **Checkbox** controls whether the file is staged. We don't pre-stage on disk until the user hits Commit; selection state is kept in the dialog VM.
- **Subject** is a single-line `TextBox` with a max-length hint (warning at 72 chars, hard limit at 100). Suggested subjects:
  - If exactly one file is checked: pre-fill with `Update <basename without ext>`.
  - If multiple files share a manifest folder: `Update <foldername>`.
  - Else blank.
- **Body** is a multi-line `TextBox`, `AcceptsReturn=True`, monospaced for those who care about commit-message column wrapping.
- **Run pre-commit hooks** defaults to on. Turning it off adds `--no-verify` to the underlying `git commit`. Hidden behind a "Show advanced" expander if we want to discourage it (probably yes).
- **Push after commit** queues a `git push` immediately after the commit succeeds. Disabled if no upstream is configured for the current branch (with a tooltip saying so).

## Service flow

```
User clicks Commit
  → CommitComposerDialog reads:
      checked file paths, subject, body, runHooks, pushAfter
  → GitService.StageAsync(root, checkedPaths)
  → GitService.CommitAsync(root, subject, body, runHooks)
       (shells out to git.exe so hooks fire by default)
  → if pushAfter: GitService.PushAsync(root, remote: origin, branch: current)
  → on success: dialog closes, Home page git status card refreshes
  → on failure: dialog stays open, InfoBar shows stderr; nothing is staged or committed
       (unstage anything we staged to leave the tree clean — best-effort)
```

## Edge cases

- **Pre-commit hook fails** → stash nothing, leave files staged, surface stderr in the dialog. User can fix the issue (often a YAML lint) and re-click Commit. Never auto-retry with `--no-verify`.
- **Concurrent edits** → if the file changed on disk between when we showed the dialog and when the user clicks Commit, diff in the dialog is stale. Re-run `git status` right before staging; if the file's content hash differs from what we showed, abort with a "files changed on disk — refresh and try again" InfoBar.
- **Detached HEAD** → block commit; show "You're on a detached HEAD (no branch). Create or check out a branch first."
- **No upstream for current branch** → Commit works; Push button disabled with tooltip.
- **Hooks path doesn't exist** → `git commit` will still succeed (it just doesn't run hooks). We don't second-guess.
- **Subject empty** → Commit button disabled.

## What we don't build (yet)

- Squash / amend support. (Plan rule: always create a new commit.)
- Sign-off automation (`-s`). Optional later.
- Interactive rebase or history rewrite.
- Conflict resolution UI for `pull`.
- Per-file partial staging (hunks). All-or-nothing per file.

## UI assets

- New `Views/CommitComposerDialog.xaml(.cs)`.
- New `ViewModels/CommitComposerViewModel.cs` (use CommunityToolkit MVVM partial properties like the rest of the codebase).
- New `Models/PendingChange.cs`: `{ Path, Status (enum: Modified, Added, Deleted, Renamed, Untracked), IsSelected, Diff? }`.
- New `Models/CommitDraft.cs`: `{ Subject, Body, RunHooks, PushAfter, SelectedPaths }`.

## Open questions

1. Should the composer pre-stage `pkgs/` binaries? — yes when the matching pkginfo is also checked (they're a unit). Maybe add a setting.
2. After a successful commit, should we auto-fetch to refresh ahead/behind? — yes, async, non-blocking.
3. How do we render binary diffs for `.msi`/`.intunewin`? — show file size delta + "binary file changed" instead of a text diff.
