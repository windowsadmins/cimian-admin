namespace CimianAdmin.Core.Models.Repository;

/// <summary>
/// Represents a Cimian repository with its structure and metadata.
/// </summary>
public sealed class CimianRepository
{
    /// <summary>
    /// Gets or sets the repository root path.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repository name (derived from folder name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the repository structure is valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets any validation errors.
    /// </summary>
    public List<string> ValidationErrors { get; set; } = [];

    /// <summary>
    /// Gets the path to the catalogs directory.
    /// </summary>
    public string CatalogsPath => Path.Combine(RootPath, "catalogs");

    /// <summary>
    /// Gets the path to the manifests directory.
    /// </summary>
    public string ManifestsPath => Path.Combine(RootPath, "manifests");

    /// <summary>
    /// Gets the path to the pkgsinfo directory.
    /// </summary>
    public string PkgsInfoPath => Path.Combine(RootPath, "pkgsinfo");

    /// <summary>
    /// Gets the path to the pkgs directory.
    /// </summary>
    public string PkgsPath => Path.Combine(RootPath, "pkgs");

    /// <summary>
    /// Gets or sets the total package count.
    /// </summary>
    public int PackageCount { get; set; }

    /// <summary>
    /// Gets or sets the total manifest count.
    /// </summary>
    public int ManifestCount { get; set; }

    /// <summary>
    /// Gets or sets the total catalog count.
    /// </summary>
    public int CatalogCount { get; set; }

    /// <summary>
    /// Gets or sets the last scan timestamp.
    /// </summary>
    public DateTime? LastScanned { get; set; }
}
