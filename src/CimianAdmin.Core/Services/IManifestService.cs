namespace CimianAdmin.Core.Services;

using CimianAdmin.Core.Models.Manifests;

/// <summary>
/// Service interface for manifest operations.
/// </summary>
public interface IManifestService
{
    /// <summary>
    /// Gets all manifests in the repository.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of manifests.</returns>
    Task<IReadOnlyList<Manifest>> GetAllManifestsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a manifest by name.
    /// </summary>
    /// <param name="name">Manifest name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The manifest if found, null otherwise.</returns>
    Task<Manifest?> GetManifestAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a manifest by file path.
    /// </summary>
    /// <param name="filePath">Path to the manifest file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The manifest if found.</returns>
    Task<Manifest> GetManifestByPathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a manifest to the repository.
    /// </summary>
    /// <param name="manifest">Manifest to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveManifestAsync(Manifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new manifest.
    /// </summary>
    /// <param name="manifest">Manifest to create.</param>
    /// <param name="name">Manifest name (file name without extension).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateManifestAsync(Manifest manifest, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a manifest from the repository.
    /// </summary>
    /// <param name="manifest">Manifest to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteManifestAsync(Manifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches manifests by criteria.
    /// </summary>
    /// <param name="searchText">Text to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching manifests.</returns>
    Task<IReadOnlyList<Manifest>> SearchManifestsAsync(string searchText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets manifests that include a specific package.
    /// </summary>
    /// <param name="packageName">Package name to search for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Manifests referencing the package.</returns>
    Task<IReadOnlyList<Manifest>> GetManifestsForPackageAsync(string packageName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when manifests are modified.
    /// </summary>
    event EventHandler? ManifestsChanged;
}
