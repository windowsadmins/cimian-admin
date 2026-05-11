namespace CimianAdmin.Core.Models.Packages;

using YamlDotNet.Serialization;

/// <summary>
/// Installer payload for a package. Field set mirrors
/// <c>cli/makecatalogs/Models/PkgsInfo.cs::Installer</c> in CimianTools.
/// Used both for <see cref="Package.Installer"/> and as the element type of
/// <see cref="Package.Uninstaller"/> (which is a list of operations).
/// Order matches the canonical layout produced by <c>cli/cimiimport</c>:
/// <c>type, size, location, hash, arguments</c> first, then less common fields.
/// </summary>
public sealed class Installer
{
    [YamlMember(Alias = "type", Order = 1)]
    public string? Type { get; set; }

    [YamlMember(Alias = "size", Order = 2)]
    public long? Size { get; set; }

    [YamlMember(Alias = "location", Order = 3)]
    public string? Location { get; set; }

    [YamlMember(Alias = "hash", Order = 4)]
    public string? Hash { get; set; }

    [YamlMember(Alias = "arguments", Order = 5)]
    public List<string>? Arguments { get; set; }

    [YamlMember(Alias = "args", Order = 6)]
    public List<string>? Args { get; set; }

    [YamlMember(Alias = "switches", Order = 7)]
    public List<string>? Switches { get; set; }

    [YamlMember(Alias = "flags", Order = 8)]
    public List<string>? Flags { get; set; }

    [YamlMember(Alias = "subcommand", Order = 9)]
    public string? Subcommand { get; set; }

    [YamlMember(Alias = "temp_dir", Order = 10)]
    public string? TempDir { get; set; }

    [YamlMember(Alias = "product_code", Order = 11)]
    public string? ProductCode { get; set; }

    [YamlMember(Alias = "upgrade_code", Order = 12)]
    public string? UpgradeCode { get; set; }

    [YamlMember(Alias = "identity_name", Order = 13)]
    public string? IdentityName { get; set; }
}
