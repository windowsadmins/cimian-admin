namespace CimianStudio.Infrastructure.Services;

using System.Diagnostics;
using CimianStudio.Core.Models.Catalogs;
using CimianStudio.Core.Models.Packages;
using CimianStudio.Core.Models.Repository;
using CimianStudio.Core.Services;
using CimianStudio.Infrastructure.Yaml;
using CimianStudio.Shared;

/// <summary>
/// Filesystem-backed implementation of <see cref="ICatalogService"/>.
/// </summary>
public sealed class CatalogService : ICatalogService
{
    private readonly IRepositoryService _repositoryService;
    private readonly IPackageService _packageService;

    public CatalogService(IRepositoryService repositoryService, IPackageService packageService)
    {
        ArgumentNullException.ThrowIfNull(repositoryService);
        ArgumentNullException.ThrowIfNull(packageService);
        _repositoryService = repositoryService;
        _packageService = packageService;
    }

    public event EventHandler? CatalogsChanged;

    public async Task<IReadOnlyList<Catalog>> GetAllCatalogsAsync(CancellationToken cancellationToken = default)
    {
        var repo = RequireRepository();
        if (!Directory.Exists(repo.CatalogsPath))
        {
            return [];
        }

        var files = Directory
            .EnumerateFiles(repo.CatalogsPath, "*" + Constants.FileExtensions.Yaml, SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(repo.CatalogsPath, "*" + Constants.FileExtensions.Yml, SearchOption.TopDirectoryOnly))
            .Where(f => !IsAllCatalog(f))
            .ToArray();

        var results = new List<Catalog>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var catalog = await ReadCatalogAsync(file, cancellationToken).ConfigureAwait(false);
            if (catalog is not null)
            {
                results.Add(catalog);
            }
        }

        return [.. results.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Treat <c>All.yaml</c> as a generated rollup, not a real catalog. Hidden everywhere.
    /// </summary>
    private static bool IsAllCatalog(string filePath) =>
        string.Equals(Path.GetFileNameWithoutExtension(filePath), "All", StringComparison.OrdinalIgnoreCase);

    public async Task<Catalog?> GetCatalogAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var all = await GetAllCatalogsAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<string>> GetCatalogNamesAsync(CancellationToken cancellationToken = default)
    {
        var repo = RequireRepository();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(repo.CatalogsPath))
        {
            foreach (var file in Directory.EnumerateFiles(repo.CatalogsPath, "*" + Constants.FileExtensions.Yaml, SearchOption.TopDirectoryOnly))
            {
                if (IsAllCatalog(file))
                {
                    continue;
                }
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        var packages = await _packageService.GetAllPackagesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var package in packages)
        {
            foreach (var catalog in package.Catalogs)
            {
                if (!string.IsNullOrWhiteSpace(catalog) && !string.Equals(catalog, "All", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(catalog);
                }
            }
        }

        return [.. names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<CatalogRebuildResult> RebuildCatalogsAsync(CancellationToken cancellationToken = default)
    {
        var repo = RequireRepository();
        var makecatalogs = ResolveTool("makecatalogs.exe");

        var psi = new ProcessStartInfo
        {
            FileName = makecatalogs,
            WorkingDirectory = repo.RootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // makecatalogs CLI expects the repo path under --repo_path (System.CommandLine),
        // not a positional argument. Passing it as a positional yields
        // "Unrecognized command or argument '<path>'".
        psi.ArgumentList.Add("--repo_path");
        psi.ArgumentList.Add(repo.RootPath);

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start {makecatalogs}.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            var rebuilt = await SnapshotCatalogNamesAsync(repo, cancellationToken).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                CatalogsChanged?.Invoke(this, EventArgs.Empty);
                return new CatalogRebuildResult
                {
                    Success = true,
                    Output = stdout,
                    RebuiltCatalogs = rebuilt,
                };
            }

            return new CatalogRebuildResult
            {
                Success = false,
                Output = stdout,
                ErrorMessage = string.IsNullOrWhiteSpace(stderr) ? $"makecatalogs exited with code {process.ExitCode}" : stderr,
                RebuiltCatalogs = rebuilt,
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return new CatalogRebuildResult
            {
                Success = false,
                ErrorMessage = $"Could not launch makecatalogs.exe: {ex.Message}",
            };
        }
    }

    private CimianRepository RequireRepository()
    {
        return _repositoryService.CurrentRepository
            ?? throw new InvalidOperationException("No repository is currently open.");
    }

    private static async Task<Catalog?> ReadCatalogAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);

            // Cimian catalogs are wrapped in `items: [...]`; older Munki-style catalogs are a bare list.
            // Try the wrapper first, fall back to the bare list shape on failure.
            List<Package> packages;
            try
            {
                var wrapper = YamlSerialization.Deserializer.Deserialize<CatalogFile>(text);
                packages = wrapper?.Items ?? [];
            }
            catch (YamlDotNet.Core.YamlException)
            {
                packages = YamlSerialization.Deserializer.Deserialize<List<Package>>(text) ?? [];
            }

            return new Catalog
            {
                Name = Path.GetFileNameWithoutExtension(filePath),
                FilePath = filePath,
                LastModified = File.GetLastWriteTimeUtc(filePath),
                Packages = packages,
            };
        }
        catch (Exception ex) when (ex is IOException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812",
        Justification = "Instantiated by YamlDotNet via reflection.")]
    private sealed class CatalogFile
    {
        [YamlDotNet.Serialization.YamlMember(Alias = "items")]
        public List<Package> Items { get; set; } = [];
    }

    private static Task<IReadOnlyList<string>> SnapshotCatalogNamesAsync(CimianRepository repo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(repo.CatalogsPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> names =
        [
            .. Directory
                .EnumerateFiles(repo.CatalogsPath, "*" + Constants.FileExtensions.Yaml, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
        ];
        return Task.FromResult(names);
    }

    private static string ResolveTool(string fileName)
    {
        var defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Cimian",
            fileName);

        return File.Exists(defaultPath) ? defaultPath : fileName;
    }
}
