namespace CimianStudio.Core.Models.Git;

public enum GitHookKind
{
    Unknown,
    ClientSide,
    ServerSide,
}

public sealed record GitHookDefinition(string Name, GitHookKind Kind, string Description);

/// <summary>
/// All standard git hooks documented in <c>man githooks</c>, with one-line
/// descriptions and a client/server classification. The defined order is what
/// the hooks list uses as the secondary sort (after Active-vs-Inactive).
/// </summary>
public static class GitHookCatalog
{
    public static IReadOnlyList<GitHookDefinition> AllHooks { get; } =
    [
        // Commit flow (client-side, most commonly customized)
        new("pre-commit", GitHookKind.ClientSide, "Runs before a commit is created — for lint, format, or test gates."),
        new("prepare-commit-msg", GitHookKind.ClientSide, "Edits the default commit message before the editor opens."),
        new("commit-msg", GitHookKind.ClientSide, "Validates or normalizes the commit message before commit completes."),
        new("post-commit", GitHookKind.ClientSide, "Runs after a commit is created — for notifications and bookkeeping."),

        // Working tree / ref updates (client-side)
        new("post-checkout", GitHookKind.ClientSide, "Runs after checkout updates the working tree."),
        new("post-merge", GitHookKind.ClientSide, "Runs after a successful merge updates the working tree."),
        new("post-rewrite", GitHookKind.ClientSide, "Runs after commands that rewrite commits (rebase, amend)."),
        new("pre-merge-commit", GitHookKind.ClientSide, "Runs before a merge commit — can abort the merge."),
        new("pre-rebase", GitHookKind.ClientSide, "Runs before a rebase begins — can abort it."),
        new("pre-push", GitHookKind.ClientSide, "Runs before refs are pushed — for tests or push-protection."),

        // Patch / email workflow (client-side)
        new("applypatch-msg", GitHookKind.ClientSide, "Validates the commit message of a patch applied via `git am`."),
        new("pre-applypatch", GitHookKind.ClientSide, "Runs before `git am` applies a patch — can reject it."),
        new("post-applypatch", GitHookKind.ClientSide, "Runs after `git am` applies a patch; cannot abort."),
        new("sendemail-validate", GitHookKind.ClientSide, "Validates patches before `git send-email` sends them."),

        // Maintenance (client-side)
        new("pre-auto-gc", GitHookKind.ClientSide, "Runs before `git gc --auto` — can abort housekeeping."),

        // Server-side (run on the receiving repository)
        new("pre-receive", GitHookKind.ServerSide, "Runs on the server before any ref is updated by a push."),
        new("update", GitHookKind.ServerSide, "Runs on the server once per ref before that ref is updated."),
        new("post-receive", GitHookKind.ServerSide, "Runs on the server after all refs have been updated."),
        new("post-update", GitHookKind.ServerSide, "Runs on the server after the push completes."),
        new("push-to-checkout", GitHookKind.ServerSide, "Runs on the server when receive.denyCurrentBranch=updateInstead."),
    ];

    public static IReadOnlyList<string> AllNames { get; } =
        [.. AllHooks.Select(h => h.Name)];

    public static GitHookDefinition? Find(string name) =>
        AllHooks.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
}
