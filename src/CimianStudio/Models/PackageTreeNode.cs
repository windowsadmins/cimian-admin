namespace CimianStudio.Models;

using CimianStudio.Core.Models.Packages;
using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// Hierarchy node for the Packages TreeView.
/// Top-level nodes are categories; their children are <see cref="Package"/>s.
/// </summary>
public sealed partial class PackageTreeNode : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Package? Package { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public System.Collections.ObjectModel.ObservableCollection<PackageTreeNode> Children { get; } = [];

    public bool HasPackage => Package is not null;
}
