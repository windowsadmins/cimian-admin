namespace CimianAdmin.Core.Models.Packages;

using YamlDotNet.Serialization;

/// <summary>
/// Represents a Cimian package definition (pkginfo).
/// </summary>
public sealed class Package
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "display_name")]
    public string? DisplayName { get; set; }

    [YamlMember(Alias = "version")]
    public string Version { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "developer")]
    public string? Developer { get; set; }

    [YamlMember(Alias = "category")]
    public string? Category { get; set; }

    [YamlMember(Alias = "catalogs")]
    public List<string> Catalogs { get; set; } = [];

    [YamlMember(Alias = "installer")]
    public Installer? Installer { get; set; }

    [YamlMember(Alias = "uninstaller")]
    public Uninstaller? Uninstaller { get; set; }

    [YamlMember(Alias = "unattended_install")]
    public bool UnattendedInstall { get; set; } = true;

    [YamlMember(Alias = "unattended_uninstall")]
    public bool UnattendedUninstall { get; set; } = true;

    [YamlMember(Alias = "autoremove")]
    public bool Autoremove { get; set; }

    [YamlMember(Alias = "installs")]
    public List<InstallsItem>? Installs { get; set; }

    [YamlMember(Alias = "supported_architectures")]
    public List<string>? SupportedArchitectures { get; set; }

    [YamlMember(Alias = "minimum_os_version")]
    public string? MinimumOsVersion { get; set; }

    [YamlMember(Alias = "blocking_applications")]
    public List<string>? BlockingApplications { get; set; }

    [YamlMember(Alias = "requires")]
    public List<string>? Requires { get; set; }

    [YamlMember(Alias = "update_for")]
    public List<string>? UpdateFor { get; set; }

    [YamlMember(Alias = "notes")]
    public string? Notes { get; set; }

    // Repository metadata (not serialized).
    [YamlIgnore]
    public string? FilePath { get; set; }

    [YamlIgnore]
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Display name if set, otherwise the package name.
    /// </summary>
    [YamlIgnore]
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName!;
}
