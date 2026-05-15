namespace CimianStudio.Infrastructure.Services;

using System.Collections.Concurrent;
using CimianStudio.Core.Models.Manifests;
using CimianStudio.Core.Models.Repository;
using CimianStudio.Core.Services;
using Cimian.Core.Services;
using CimianStudio.Shared;

/// <summary>
/// Filesystem-backed implementation of <see cref="IManifestService"/>.
/// </summary>
public sealed class ManifestService : IManifestService
{
    private readonly IRepositoryService _repositoryService;

    public ManifestService(IRepositoryService repositoryService)
    {
        ArgumentNullException.ThrowIfNull(repositoryService);
        _repositoryService = repositoryService;
    }

    public event EventHandler? ManifestsChanged;

    public async Task<IReadOnlyList<Manifest>> GetAllManifestsAsync(CancellationToken cancellationToken = default)
    {
        var repo = RequireRepository();
        if (!Directory.Exists(repo.ManifestsPath))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(repo.ManifestsPath, "*" + Constants.FileExtensions.Yaml, SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(repo.ManifestsPath, "*" + Constants.FileExtensions.Yml, SearchOption.AllDirectories))
            .ToArray();

        var bag = new ConcurrentBag<Manifest>();
        await Parallel.ForEachAsync(files, cancellationToken, async (file, ct) =>
        {
            var manifest = await ReadManifestAsync(file, repo.ManifestsPath, ct).ConfigureAwait(false);
            if (manifest is not null)
            {
                bag.Add(manifest);
            }
        }).ConfigureAwait(false);

        return [.. bag.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<Manifest?> GetManifestAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var all = await GetAllManifestsAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Manifest> GetManifestByPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var repo = _repositoryService.CurrentRepository;
        var basePath = repo?.ManifestsPath ?? Path.GetDirectoryName(filePath) ?? string.Empty;
        var manifest = await ReadManifestAsync(filePath, basePath, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            throw new InvalidOperationException($"Failed to load manifest from {filePath}");
        }

        return manifest;
    }

    public async Task SaveManifestAsync(Manifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.FilePath))
        {
            throw new InvalidOperationException("Manifest.FilePath must be set before saving. Use CreateManifestAsync for new manifests.");
        }

        await WriteManifestAsync(manifest, manifest.FilePath, cancellationToken).ConfigureAwait(false);
        ManifestsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task CreateManifestAsync(Manifest manifest, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var repo = RequireRepository();
        Directory.CreateDirectory(repo.ManifestsPath);

        var fileName = name.EndsWith(Constants.FileExtensions.Yaml, StringComparison.OrdinalIgnoreCase)
                       || name.EndsWith(Constants.FileExtensions.Yml, StringComparison.OrdinalIgnoreCase)
            ? name
            : name + Constants.FileExtensions.Yaml;

        var filePath = Path.Combine(repo.ManifestsPath, fileName);
        manifest.Name = Path.GetFileNameWithoutExtension(fileName);
        await WriteManifestAsync(manifest, filePath, cancellationToken).ConfigureAwait(false);
        ManifestsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task DeleteManifestAsync(Manifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.FilePath) || !File.Exists(manifest.FilePath))
        {
            throw new InvalidOperationException("Manifest has no file on disk to delete.");
        }

        File.Delete(manifest.FilePath);
        ManifestsChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<Manifest>> SearchManifestsAsync(string searchText, CancellationToken cancellationToken = default)
    {
        var all = await GetAllManifestsAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return all;
        }

        var needle = searchText.Trim();
        return [.. all.Where(m =>
            Contains(m.Name, needle) ||
            Contains(m.DisplayName, needle) ||
            Contains(m.Notes, needle))];
    }

    public async Task<IReadOnlyList<Manifest>> GetManifestsForPackageAsync(string packageName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);

        var all = await GetAllManifestsAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Where(m => ManifestReferencesPackage(m, packageName))];
    }

    private CimianRepository RequireRepository()
    {
        return _repositoryService.CurrentRepository
            ?? throw new InvalidOperationException("No repository is currently open.");
    }

    private static async Task<Manifest?> ReadManifestAsync(string filePath, string manifestsRoot, CancellationToken cancellationToken)
    {
        try
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var manifest = YamlUtils.DeserializeManifest<Manifest>(text);
            if (manifest is null)
            {
                return null;
            }

            manifest.FilePath = filePath;
            manifest.LastModified = File.GetLastWriteTimeUtc(filePath);
            manifest.Created = File.GetCreationTimeUtc(filePath);
            manifest.Name = DeriveManifestName(filePath, manifestsRoot);
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static string DeriveManifestName(string filePath, string manifestsRoot)
    {
        // Manifests can live in subdirectories (e.g. manifests/sites/eng.yaml -> "sites/eng").
        if (!string.IsNullOrEmpty(manifestsRoot)
            && filePath.StartsWith(manifestsRoot, StringComparison.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(manifestsRoot, filePath);
            var withoutExt = Path.ChangeExtension(relative, null);
            return withoutExt?.Replace(Path.DirectorySeparatorChar, '/') ?? string.Empty;
        }

        return Path.GetFileNameWithoutExtension(filePath);
    }

    private static async Task WriteManifestAsync(Manifest manifest, string filePath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var yaml = YamlUtils.SerializeManifest(manifest);
        await File.WriteAllTextAsync(filePath, yaml, cancellationToken).ConfigureAwait(false);
        manifest.FilePath = filePath;
        manifest.LastModified = File.GetLastWriteTimeUtc(filePath);
        manifest.Created = File.GetCreationTimeUtc(filePath);
    }

    private static bool ManifestReferencesPackage(Manifest manifest, string packageName)
    {
        return ListContains(manifest.ManagedInstalls, packageName)
            || ListContains(manifest.ManagedUninstalls, packageName)
            || ListContains(manifest.ManagedUpdates, packageName)
            || ListContains(manifest.OptionalInstalls, packageName)
            || ListContains(manifest.DefaultInstalls, packageName)
            || ConditionalReferences(manifest.ConditionalItems, packageName);
    }

    private static bool ConditionalReferences(List<ConditionalItem>? items, string packageName)
    {
        if (items is null)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (ListContains(item.ManagedInstalls, packageName)
                || ListContains(item.ManagedUninstalls, packageName)
                || ListContains(item.ManagedUpdates, packageName)
                || ListContains(item.OptionalInstalls, packageName)
                || ConditionalReferences(item.NestedConditionalItems, packageName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ListContains(List<string>? list, string value)
    {
        return list is not null
            && list.Any(s => string.Equals(s, value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Contains(string? haystack, string needle)
    {
        return !string.IsNullOrEmpty(haystack)
            && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}
