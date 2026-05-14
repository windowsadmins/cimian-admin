namespace CimianStudio.Core.Models.Manifests;

using YamlDotNet.Serialization;

/// <summary>
/// Represents a Cimian deployment manifest.
/// Manifests define which packages should be installed or removed on target machines.
/// </summary>
public sealed class Manifest
{
    [YamlMember(Alias = "display_name")]
    public string? DisplayName { get; set; }

    [YamlMember(Alias = "catalogs")]
    public List<string> Catalogs { get; set; } = [];

    [YamlMember(Alias = "included_manifests")]
    public List<string>? IncludedManifests { get; set; }

    [YamlMember(Alias = "managed_installs")]
    public List<string>? ManagedInstalls { get; set; }

    [YamlMember(Alias = "managed_uninstalls")]
    public List<string>? ManagedUninstalls { get; set; }

    [YamlMember(Alias = "managed_updates")]
    public List<string>? ManagedUpdates { get; set; }

    [YamlMember(Alias = "optional_installs")]
    public List<string>? OptionalInstalls { get; set; }

    [YamlMember(Alias = "default_installs")]
    public List<string>? DefaultInstalls { get; set; }

    [YamlMember(Alias = "conditional_items")]
    public List<ConditionalItem>? ConditionalItems { get; set; }

    [YamlMember(Alias = "notes")]
    public string? Notes { get; set; }

    // Repository metadata (not serialized to YAML)
    [YamlIgnore]
    public string? Name { get; set; }

    [YamlIgnore]
    public string? FilePath { get; set; }

    [YamlIgnore]
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Filesystem creation time (UTC) of this manifest file in the current
    /// working copy. NOT a repo-history value — clone, copy, or fresh checkout
    /// resets it. Use git history if you need authoring time.
    /// </summary>
    [YamlIgnore]
    public DateTime? Created { get; set; }
}
