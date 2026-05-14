namespace CimianStudio.Core.Services;

using CimianStudio.Core.Models.Catalogs;

/// <summary>
/// Service interface for catalog operations.
/// </summary>
public interface ICatalogService
{
    /// <summary>
    /// Gets all catalogs in the repository.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of catalogs.</returns>
    Task<IReadOnlyList<Catalog>> GetAllCatalogsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a catalog by name.
    /// </summary>
    /// <param name="name">Catalog name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog if found, null otherwise.</returns>
    Task<Catalog?> GetCatalogAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds all catalogs using the makecatalogs tool.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the rebuild operation.</returns>
    Task<CatalogRebuildResult> RebuildCatalogsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all unique catalog names referenced in packages.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of catalog names.</returns>
    Task<IReadOnlyList<string>> GetCatalogNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when catalogs are modified.
    /// </summary>
    event EventHandler? CatalogsChanged;
}

/// <summary>
/// Result of catalog rebuild operation.
/// </summary>
public sealed class CatalogRebuildResult
{
    /// <summary>
    /// Gets whether the rebuild was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the output from the makecatalogs tool.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// Gets any error message.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the catalogs that were rebuilt.
    /// </summary>
    public IReadOnlyList<string> RebuiltCatalogs { get; init; } = [];
}
