namespace CimianAdmin.Core.Models.Packages;

using YamlDotNet.Serialization;

/// <summary>
/// Represents the uninstaller payload for a package.
/// </summary>
public sealed class Uninstaller
{
    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "location")]
    public string? Location { get; set; }

    [YamlMember(Alias = "arguments")]
    public List<string>? Arguments { get; set; }
}
