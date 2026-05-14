namespace CimianStudio.Core.Services;

using CimianStudio.Core.Models.Manifests;
using CimianStudio.Core.Models.Packages;

/// <summary>
/// Tracks which packages and manifests carry unsaved in-memory edits during the
/// current app session. Editors flush field state to model references on
/// switch-away so the user's typing isn't lost when they click another row.
/// The "Save all" command on the title bar then commits every dirty entry at
/// once. Identity is by reference — the same Package/Manifest instance held by
/// the list view models is what the editor edits and what the session tracks.
/// </summary>
public interface ISessionState
{
    void MarkPackageDirty(Package package);
    void MarkPackageClean(Package package);
    bool IsPackageDirty(Package package);
    IReadOnlyList<Package> DirtyPackages { get; }

    void MarkManifestDirty(Manifest manifest);
    void MarkManifestClean(Manifest manifest);
    bool IsManifestDirty(Manifest manifest);
    IReadOnlyList<Manifest> DirtyManifests { get; }

    int TotalDirtyCount { get; }
    event EventHandler? Changed;
}
