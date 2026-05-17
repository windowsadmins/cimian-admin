namespace CimianStudio.Infrastructure.Services;

using CimianStudio.Core.Models.Repository;
using CimianStudio.Core.Services;
using CimianStudio.Shared;

/// <summary>
/// Filesystem-backed implementation of <see cref="IRepositoryService"/>.
/// </summary>
public sealed class RepositoryService : IRepositoryService
{
    private CimianRepository? _current;

    public CimianRepository? CurrentRepository => _current;

    public event EventHandler<CimianRepository?>? RepositoryChanged;

    public async Task<CimianRepository> OpenRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var repository = new CimianRepository
        {
            RootPath = fullPath,
            Name = new DirectoryInfo(fullPath).Name,
        };

        var validation = await ValidateRepositoryAsync(repository, cancellationToken).ConfigureAwait(false);
        repository.IsValid = validation.IsValid;
        repository.ValidationErrors = [.. validation.Errors];

        await RefreshStatisticsAsync(repository, cancellationToken).ConfigureAwait(false);

        _current = repository;
        RepositoryChanged?.Invoke(this, repository);
        return repository;
    }

    public Task<CimianRepository> CreateRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        Directory.CreateDirectory(Path.Combine(fullPath, Constants.RepositoryDirectories.Catalogs));
        Directory.CreateDirectory(Path.Combine(fullPath, Constants.RepositoryDirectories.Manifests));
        Directory.CreateDirectory(Path.Combine(fullPath, Constants.RepositoryDirectories.PkgsInfo));
        Directory.CreateDirectory(Path.Combine(fullPath, Constants.RepositoryDirectories.Pkgs));

        return OpenRepositoryAsync(fullPath, cancellationToken);
    }

    public Task<RepositoryValidationResult> ValidateRepositoryAsync(CimianRepository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var errors = new List<string>();
        var warnings = new List<string>();

        if (!Directory.Exists(repository.RootPath))
        {
            errors.Add($"Repository root does not exist: {repository.RootPath}");
        }
        else
        {
            // pkgsinfo and manifests are required; catalogs and pkgs are conventionally present
            // but not strictly required (catalogs are generated, pkgs may live on remote storage).
            if (!Directory.Exists(repository.PkgsInfoPath))
            {
                errors.Add($"Missing required directory: {Constants.RepositoryDirectories.PkgsInfo}");
            }

            if (!Directory.Exists(repository.ManifestsPath))
            {
                errors.Add($"Missing required directory: {Constants.RepositoryDirectories.Manifests}");
            }

            if (!Directory.Exists(repository.CatalogsPath))
            {
                warnings.Add($"Missing directory: {Constants.RepositoryDirectories.Catalogs} (will be created when catalogs are rebuilt)");
            }

            if (!Directory.Exists(repository.PkgsPath))
            {
                warnings.Add($"Missing directory: {Constants.RepositoryDirectories.Pkgs}");
            }
        }

        return Task.FromResult(new RepositoryValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings,
        });
    }

    public Task RefreshStatisticsAsync(CimianRepository repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        repository.PackageCount = CountYaml(repository.PkgsInfoPath, recursive: true);
        repository.ManifestCount = CountYaml(repository.ManifestsPath, recursive: true);
        // 'All.yaml' is the synthesized union catalog written by makecatalogs — count
        // only the real, named catalogs so the Home tile matches what users see in the
        // Catalogs page (which already filters it out).
        repository.CatalogCount = CountCatalogs(repository.CatalogsPath);
        repository.LastScanned = DateTime.UtcNow;

        return Task.CompletedTask;
    }

    private static int CountYaml(string directory, bool recursive)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var yaml = Directory.EnumerateFiles(directory, "*" + Constants.FileExtensions.Yaml, option);
        var yml = Directory.EnumerateFiles(directory, "*" + Constants.FileExtensions.Yml, option);
        return yaml.Count() + yml.Count();
    }

    private static int CountCatalogs(string directory)
    {
        if (!Directory.Exists(directory)) return 0;
        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(p =>
                p.EndsWith(Constants.FileExtensions.Yaml, StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(Constants.FileExtensions.Yml, StringComparison.OrdinalIgnoreCase))
            .Count(p => !string.Equals(Path.GetFileNameWithoutExtension(p), "All", StringComparison.OrdinalIgnoreCase));
    }
}
