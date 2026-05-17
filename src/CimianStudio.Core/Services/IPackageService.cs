namespace CimianStudio.Core.Services;

using CimianStudio.Core.Models.Packages;

/// <summary>
/// Service interface for package operations.
/// </summary>
public interface IPackageService
{
    /// <summary>
    /// Gets all packages in the repository.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Collection of packages.</returns>
    Task<IReadOnlyList<Package>> GetAllPackagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a package by name and version.
    /// </summary>
    /// <param name="name">Package name.</param>
    /// <param name="version">Package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The package if found, null otherwise.</returns>
    Task<Package?> GetPackageAsync(string name, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a package by file path.
    /// </summary>
    /// <param name="filePath">Path to the pkginfo file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The package if found.</returns>
    Task<Package> GetPackageByPathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a package to the repository.
    /// </summary>
    /// <param name="package">Package to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SavePackageAsync(Package package, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new package.
    /// </summary>
    /// <param name="package">Package to create.</param>
    /// <param name="relativePath">Relative path within pkgsinfo directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreatePackageAsync(Package package, string relativePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a package from the repository.
    /// </summary>
    /// <param name="package">Package to delete.</param>
    /// <param name="deleteInstaller">Whether to also delete the installer file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeletePackageAsync(Package package, bool deleteInstaller = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches packages by criteria.
    /// </summary>
    /// <param name="searchText">Text to search for.</param>
    /// <param name="catalog">Optional catalog filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching packages.</returns>
    Task<IReadOnlyList<Package>> SearchPackagesAsync(string searchText, string? catalog = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually fires <see cref="PackagesChanged"/>. Used by the import wizard
    /// after handing the actual disk write off to <c>Cimian.CLI.Cimiimport.Services.ImportService</c>:
    /// since that path bypasses <see cref="CreatePackageAsync"/>, the event
    /// won't fire on its own and downstream views (Packages tab, Catalogs tab)
    /// won't refresh.
    /// </summary>
    void NotifyPackagesChanged();

    /// <summary>
    /// Event raised when packages are modified.
    /// </summary>
    event EventHandler? PackagesChanged;
}
