namespace CimianAdmin.Core.Models.Search;

/// <summary>What kind of file produced a search hit.</summary>
public enum SearchHitKind
{
    Package,
    Manifest,
}

/// <summary>
/// A single match in the search index.
/// <see cref="Snippet"/> is the matched line plus one line of context above and below, joined with newlines.
/// </summary>
public sealed record SearchHit(
    string AbsolutePath,
    string DisplayName,
    SearchHitKind Kind,
    int LineNumber,
    string Snippet);

/// <summary>Progress signal emitted while the index is being built or refreshed.</summary>
public sealed record SearchIndexProgress(int Indexed, int Total, bool IsComplete);
