namespace CimianStudio.Infrastructure.Services;

using System.Collections.Concurrent;
using System.Globalization;
using CimianStudio.Core.Models.Packages;
using CimianStudio.Core.Models.Repository;
using CimianStudio.Core.Services;
using CimianStudio.Infrastructure.Yaml;
using CimianStudio.Shared;

/// <summary>
/// Filesystem-backed implementation of <see cref="IPackageService"/>.
/// </summary>
public sealed class PackageService : IPackageService
{
    private readonly IRepositoryService _repositoryService;

    public PackageService(IRepositoryService repositoryService)
    {
        ArgumentNullException.ThrowIfNull(repositoryService);
        _repositoryService = repositoryService;
    }

    public event EventHandler? PackagesChanged;

    public async Task<IReadOnlyList<Package>> GetAllPackagesAsync(CancellationToken cancellationToken = default)
    {
        var repo = RequireRepository();
        if (!Directory.Exists(repo.PkgsInfoPath))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(repo.PkgsInfoPath, "*" + Constants.FileExtensions.Yaml, SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(repo.PkgsInfoPath, "*" + Constants.FileExtensions.Yml, SearchOption.AllDirectories))
            .ToArray();

        var bag = new ConcurrentBag<Package>();
        await Parallel.ForEachAsync(files, cancellationToken, async (file, ct) =>
        {
            var package = await ReadPackageAsync(file, ct).ConfigureAwait(false);
            if (package is not null)
            {
                bag.Add(package);
            }
        }).ConfigureAwait(false);

        return [.. bag.OrderBy(p => p.EffectiveDisplayName, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<Package?> GetPackageAsync(string name, string version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var all = await GetAllPackagesAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Version, version, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Package> GetPackageByPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var package = await ReadPackageAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            throw new InvalidOperationException($"Failed to load package from {filePath}");
        }

        return package;
    }

    public async Task SavePackageAsync(Package package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(package.FilePath))
        {
            throw new InvalidOperationException("Package.FilePath must be set before saving. Use CreatePackageAsync for new packages.");
        }

        await WritePackageAsync(package, package.FilePath, cancellationToken).ConfigureAwait(false);
        PackagesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task CreatePackageAsync(Package package, string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        relativePath ??= string.Empty;

        var repo = RequireRepository();
        var directory = Path.Combine(repo.PkgsInfoPath, relativePath);
        Directory.CreateDirectory(directory);

        var fileName = !string.IsNullOrWhiteSpace(package.Version)
            ? string.Format(CultureInfo.InvariantCulture, "{0}-{1}{2}", package.Name, package.Version, Constants.FileExtensions.Yaml)
            : package.Name + Constants.FileExtensions.Yaml;

        var filePath = Path.Combine(directory, fileName);
        await WritePackageAsync(package, filePath, cancellationToken).ConfigureAwait(false);
        PackagesChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task DeletePackageAsync(Package package, bool deleteInstaller = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (string.IsNullOrWhiteSpace(package.FilePath) || !File.Exists(package.FilePath))
        {
            throw new InvalidOperationException("Package has no file on disk to delete.");
        }

        File.Delete(package.FilePath);

        if (deleteInstaller && package.Installer is { Location: { Length: > 0 } location })
        {
            var repo = RequireRepository();
            var installerPath = Path.IsPathRooted(location)
                ? location
                : Path.Combine(repo.RootPath, location);

            if (File.Exists(installerPath))
            {
                File.Delete(installerPath);
            }
        }

        PackagesChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Package>> SearchPackagesAsync(string searchText, string? catalog = null, CancellationToken cancellationToken = default)
    {
        var all = await GetAllPackagesAsync(cancellationToken).ConfigureAwait(false);

        IEnumerable<Package> filtered = all;
        if (!string.IsNullOrWhiteSpace(catalog))
        {
            filtered = filtered.Where(p => p.Catalogs.Any(c => string.Equals(c, catalog, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var needle = searchText.Trim();
            filtered = filtered.Where(p =>
                Contains(p.Name, needle) ||
                Contains(p.DisplayName, needle) ||
                Contains(p.Description, needle) ||
                Contains(p.Developer, needle));
        }

        return [.. filtered];
    }

    /// <summary>
    /// Fires <see cref="PackagesChanged"/>. The import wizard hands disk writes
    /// off to <c>ImportService.ImportAsync</c>, which bypasses
    /// <see cref="CreatePackageAsync"/> and so wouldn't trigger the event on its
    /// own — this is how the wizard pokes downstream views to refresh.
    /// </summary>
    public void NotifyPackagesChanged()
    {
        PackagesChanged?.Invoke(this, EventArgs.Empty);
    }

    private CimianRepository RequireRepository()
    {
        return _repositoryService.CurrentRepository
            ?? throw new InvalidOperationException("No repository is currently open.");
    }

    private static async Task<Package?> ReadPackageAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var package = PackageYaml.Deserialize(text);
            if (package is null)
            {
                return null;
            }

            package.FilePath = filePath;
            package.LastModified = File.GetLastWriteTimeUtc(filePath);
            package.Created = File.GetCreationTimeUtc(filePath);
            return package;
        }
        catch (Exception ex) when (ex is IOException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static async Task WritePackageAsync(Package package, string filePath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var yaml = PackageYaml.Serialize(package);
        await File.WriteAllTextAsync(filePath, yaml, cancellationToken).ConfigureAwait(false);
        package.FilePath = filePath;
        package.LastModified = File.GetLastWriteTimeUtc(filePath);
        // Preserve Created across writes — File.GetCreationTime survives content edits
        // on NTFS so just re-read it.
        package.Created = File.GetCreationTimeUtc(filePath);
    }

    private static bool Contains(string? haystack, string needle)
    {
        return !string.IsNullOrEmpty(haystack)
            && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
