namespace CimianAdmin.Infrastructure.Services;

using CimianAdmin.Core.Models.Manifests;
using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Core.Services;

public sealed class EditorSessionState : ISessionState
{
    private readonly HashSet<Package> _packages = [];
    private readonly HashSet<Manifest> _manifests = [];

    public event EventHandler? Changed;

    public IReadOnlyList<Package> DirtyPackages => [.. _packages];
    public IReadOnlyList<Manifest> DirtyManifests => [.. _manifests];
    public int TotalDirtyCount => _packages.Count + _manifests.Count;

    public void MarkPackageDirty(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_packages.Add(package)) Notify();
    }

    public void MarkPackageClean(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (_packages.Remove(package)) Notify();
    }

    public bool IsPackageDirty(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return _packages.Contains(package);
    }

    public void MarkManifestDirty(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (_manifests.Add(manifest)) Notify();
    }

    public void MarkManifestClean(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (_manifests.Remove(manifest)) Notify();
    }

    public bool IsManifestDirty(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return _manifests.Contains(manifest);
    }

    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}
