namespace CimianAdmin.Core.Services;

using CimianAdmin.Core.Models.Search;

/// <summary>
/// Indexes pkgsinfo + manifest file contents in memory and answers substring queries.
/// Watches the repository for changes and re-indexes affected files in the background.
/// </summary>
public interface ISearchService
{
    /// <summary>Begins watching and indexing the given repository root.</summary>
    Task StartAsync(string repositoryRoot, CancellationToken cancellationToken = default);

    /// <summary>Stops watching and clears the in-memory index.</summary>
    Task StopAsync();

    /// <summary>True once the initial scan has finished. <see cref="SearchAsync"/> works before this, just on a partial index.</summary>
    bool IsReady { get; }

    /// <summary>Fires whenever the indexing progress changes (initial scan or re-index).</summary>
    event EventHandler<SearchIndexProgress>? ProgressChanged;

    /// <summary>
    /// Substring scan across indexed files. Case-insensitive. Returns at most <paramref name="maxResults"/> hits.
    /// Each hit includes the matched line plus one line of context above/below.
    /// </summary>
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults = 50, CancellationToken cancellationToken = default);
}
