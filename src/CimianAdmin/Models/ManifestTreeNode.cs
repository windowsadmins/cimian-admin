namespace CimianAdmin.Models;

using CimianAdmin.Core.Models.Manifests;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Hierarchy node used to render the manifest TreeView.
/// A node may correspond to an actual manifest (leaf or branch with a manifest of its own)
/// or be a synthetic folder created from intermediate slash-segments.
/// </summary>
public sealed partial class ManifestTreeNode : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FullPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Manifest? Manifest { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public System.Collections.ObjectModel.ObservableCollection<ManifestTreeNode> Children { get; } = [];

    public bool HasManifest => Manifest is not null;
}
