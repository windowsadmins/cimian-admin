namespace CimianAdmin.Core.Models.Packages;

using YamlDotNet.Serialization;

/// <summary>
/// Cimian pkginfo file. Field set mirrors
/// <c>cli/makecatalogs/Models/PkgsInfo.cs::PkgsInfo</c> in CimianTools (the
/// canonical reader) plus the few keys that <c>cli/makepkginfo</c> emits.
/// Uses <see cref="Installer"/> for both <see cref="Installer"/> and the items
/// in <see cref="Uninstaller"/> (Cimian models uninstall as a list of operations).
/// </summary>
public sealed class Package
{
    // -------- identity --------

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "display_name")]
    public string? DisplayName { get; set; }

    /// <summary>Legacy / not authoritative. Cimian's runtime never reads
    /// <c>pkgsInfo.Identifier</c>; only one file in the live corpus uses it. Kept on the
    /// model for round-trip safety, but never surfaced in the editor UI.</summary>
    [YamlMember(Alias = "identifier")]
    public string? Identifier { get; set; }

    [YamlMember(Alias = "version")]
    public string Version { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "developer")]
    public string? Developer { get; set; }

    [YamlMember(Alias = "category")]
    public string? Category { get; set; }

    [YamlMember(Alias = "icon_name")]
    public string? IconName { get; set; }

    [YamlMember(Alias = "catalogs")]
    public List<string> Catalogs { get; set; } = [];

    // -------- top-level installer hint --------

    /// <summary>Legacy / not authoritative. <c>cimiimport</c> deliberately drops this key
    /// during serialization and no Cimian runtime consumer reads it. Use
    /// <c>Installer.Type</c> instead. Kept on the model for round-trip safety on the ~10
    /// legacy files that still carry the key.</summary>
    [YamlMember(Alias = "installer_type")]
    public string? InstallerType { get; set; }

    // -------- installer / uninstaller --------

    [YamlMember(Alias = "installer")]
    public Installer? Installer { get; set; }

    /// <summary>List of uninstall operations (Cimian models uninstall as a sequence).</summary>
    [YamlMember(Alias = "uninstaller")]
    public List<Installer>? Uninstaller { get; set; }

    [YamlMember(Alias = "uninstaller_path")]
    public string? UninstallerPath { get; set; }

    // -------- behaviour flags --------

    [YamlMember(Alias = "unattended_install")]
    public bool UnattendedInstall { get; set; }

    [YamlMember(Alias = "unattended_uninstall")]
    public bool UnattendedUninstall { get; set; }

    [YamlMember(Alias = "uninstallable")]
    public bool? Uninstallable { get; set; }

    [YamlMember(Alias = "OnDemand")]
    public bool OnDemand { get; set; }

    // -------- requirements --------

    [YamlMember(Alias = "installs")]
    public List<InstallsItem>? Installs { get; set; }

    [YamlMember(Alias = "supported_architectures")]
    public List<string>? SupportedArchitectures { get; set; }

    [YamlMember(Alias = "minimum_os_version")]
    public string? MinimumOsVersion { get; set; }

    [YamlMember(Alias = "maximum_os_version")]
    public string? MaximumOsVersion { get; set; }

    [YamlMember(Alias = "minimum_cimian_version")]
    public string? MinimumCimianVersion { get; set; }

    [YamlMember(Alias = "blocking_applications")]
    public List<string>? BlockingApplications { get; set; }

    [YamlMember(Alias = "requires")]
    public List<string>? Requires { get; set; }

    [YamlMember(Alias = "update_for")]
    public List<string>? UpdateFor { get; set; }

    [YamlMember(Alias = "managed_profiles")]
    public List<string>? ManagedProfiles { get; set; }

    [YamlMember(Alias = "managed_apps")]
    public List<string>? ManagedApps { get; set; }

    // -------- scripts --------

    [YamlMember(Alias = "preinstall_script")]
    public string? PreinstallScript { get; set; }

    [YamlMember(Alias = "postinstall_script")]
    public string? PostinstallScript { get; set; }

    [YamlMember(Alias = "preuninstall_script")]
    public string? PreuninstallScript { get; set; }

    [YamlMember(Alias = "postuninstall_script")]
    public string? PostuninstallScript { get; set; }

    [YamlMember(Alias = "installcheck_script")]
    public string? InstallCheckScript { get; set; }

    [YamlMember(Alias = "uninstallcheck_script")]
    public string? UninstallCheckScript { get; set; }

    // -------- schedule --------

    [YamlMember(Alias = "install_window")]
    public InstallWindow? InstallWindow { get; set; }

    // -------- arbitrary trailing metadata --------

    /// <summary>
    /// Free-form trailing dictionary used by tools like cimian-promoter
    /// (e.g. <c>cimian-promoter_edit_date</c>). Always emitted last, unchanged.
    /// Marked <see cref="YamlIgnoreAttribute"/> because YamlDotNet 16.3 does not bind
    /// <c>[YamlMember(Alias = "_metadata")]</c> (leading-underscore alias regression).
    /// <c>PackageService.ReadPackageAsync</c> populates this manually via
    /// <see cref="YamlDotNet.RepresentationModel"/>. Values can be scalars, lists or
    /// nested mappings — anything legal under <c>_metadata:</c> in the source file.
    /// </summary>
    [YamlIgnore]
    public Dictionary<string, object?>? Metadata { get; set; }

    // -------- repository metadata (not serialized) --------

    [YamlIgnore]
    public string? FilePath { get; set; }

    [YamlIgnore]
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// File creation time (UTC) — when this pkginfo was first written to the repo.
    /// On NTFS this survives content edits; on FAT-like filesystems it may match
    /// LastModified.
    /// </summary>
    [YamlIgnore]
    public DateTime? Created { get; set; }

    /// <summary>Display name if set, otherwise the package name.</summary>
    [YamlIgnore]
    public string EffectiveDisplayName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName!;
}
