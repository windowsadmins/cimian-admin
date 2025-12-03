namespace CimianAdmin.Core.Models.Manifests;

using YamlDotNet.Serialization;

/// <summary>
/// Represents a conditional item in a manifest.
/// Allows dynamic package deployment based on system facts.
/// </summary>
public sealed class ConditionalItem
{
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    [YamlMember(Alias = "fact")]
    public string? Fact { get; set; }

    [YamlMember(Alias = "operator")]
    public string? Operator { get; set; }

    [YamlMember(Alias = "value")]
    public object? Value { get; set; }

    [YamlMember(Alias = "managed_installs")]
    public List<string>? ManagedInstalls { get; set; }

    [YamlMember(Alias = "managed_uninstalls")]
    public List<string>? ManagedUninstalls { get; set; }

    [YamlMember(Alias = "managed_updates")]
    public List<string>? ManagedUpdates { get; set; }

    [YamlMember(Alias = "optional_installs")]
    public List<string>? OptionalInstalls { get; set; }

    [YamlMember(Alias = "included_manifests")]
    public List<string>? IncludedManifests { get; set; }

    [YamlMember(Alias = "conditional_items")]
    public List<ConditionalItem>? NestedConditionalItems { get; set; }
}
