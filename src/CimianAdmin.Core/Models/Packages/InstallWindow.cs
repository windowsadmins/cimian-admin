namespace CimianAdmin.Core.Models.Packages;

using YamlDotNet.Serialization;

/// <summary>
/// Time window during which an installation is allowed.
/// Mirrors <c>cli/makecatalogs/Models/PkgsInfo.cs::InstallWindow</c>.
/// </summary>
public sealed class InstallWindow
{
    [YamlMember(Alias = "start")]
    public string? Start { get; set; }

    [YamlMember(Alias = "end")]
    public string? End { get; set; }

    [YamlMember(Alias = "weekdays")]
    public List<string>? Weekdays { get; set; }
}
