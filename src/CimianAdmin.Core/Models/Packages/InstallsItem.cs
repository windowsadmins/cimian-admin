namespace CimianAdmin.Core.Models.Packages;

using YamlDotNet.Serialization;

/// <summary>
/// Detection rule used to determine whether a package is installed.
/// </summary>
public sealed class InstallsItem
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    [YamlMember(Alias = "version")]
    public string? Version { get; set; }

    [YamlMember(Alias = "version_comparison")]
    public string? VersionComparison { get; set; }

    [YamlMember(Alias = "hash")]
    public string? Hash { get; set; }

    [YamlMember(Alias = "hash_algorithm")]
    public string? HashAlgorithm { get; set; }

    [YamlMember(Alias = "registry_key")]
    public string? RegistryKey { get; set; }

    [YamlMember(Alias = "registry_value")]
    public string? RegistryValue { get; set; }

    [YamlMember(Alias = "product_code")]
    public string? ProductCode { get; set; }

    [YamlMember(Alias = "upgrade_code")]
    public string? UpgradeCode { get; set; }
}
