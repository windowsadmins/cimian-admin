namespace CimianAdmin.Core.Models.Catalogs;

using CimianAdmin.Core.Models.Packages;

/// <summary>
/// Represents a Cimian catalog containing package references.
/// </summary>
public sealed class Catalog
{
    /// <summary>
    /// Gets or sets the catalog name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the catalog file path.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Gets or sets the packages in this catalog.
    /// </summary>
    public List<Package> Packages { get; set; } = [];

    /// <summary>
    /// Gets or sets the last modified timestamp.
    /// </summary>
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Gets the total number of packages in this catalog.
    /// </summary>
    public int PackageCount => Packages.Count;
}
