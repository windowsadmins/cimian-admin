namespace CimianStudio.Core.Models.Packages;

using YamlDotNet.Serialization;

/// <summary>
/// Detection rule emitted under the <c>installs:</c> array.
/// Field set mirrors <c>cli/makecatalogs/Models/PkgsInfo.cs::InstallItem</c>
/// and order matches the canonical layout written by <c>cli/cimiimport</c>:
/// <c>type, path, md5checksum, version, product_code, upgrade_code, identity_name</c>.
/// </summary>
public sealed class InstallsItem
{
    [YamlMember(Alias = "type", Order = 1)]
    public string? Type { get; set; }

    [YamlMember(Alias = "path", Order = 2)]
    public string? Path { get; set; }

    [YamlMember(Alias = "md5checksum", Order = 3)]
    public string? Md5Checksum { get; set; }

    [YamlMember(Alias = "version", Order = 4)]
    public string? Version { get; set; }

    [YamlMember(Alias = "product_code", Order = 5)]
    public string? ProductCode { get; set; }

    [YamlMember(Alias = "upgrade_code", Order = 6)]
    public string? UpgradeCode { get; set; }

    [YamlMember(Alias = "identity_name", Order = 7)]
    public string? IdentityName { get; set; }
}
