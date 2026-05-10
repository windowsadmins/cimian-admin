namespace CimianAdmin.Core.Models.Packages;

using YamlDotNet.Serialization;

/// <summary>
/// Represents the installer payload for a package.
/// </summary>
public sealed class Installer
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "location")]
    public string Location { get; set; } = string.Empty;

    [YamlMember(Alias = "arguments")]
    public List<string>? Arguments { get; set; }

    [YamlMember(Alias = "hash")]
    public string? Hash { get; set; }

    [YamlMember(Alias = "hash_algorithm")]
    public string? HashAlgorithm { get; set; }

    [YamlMember(Alias = "size")]
    public long? Size { get; set; }
}
