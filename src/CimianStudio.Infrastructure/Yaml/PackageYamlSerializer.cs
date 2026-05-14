namespace CimianStudio.Infrastructure.Yaml;

using System.Collections.Generic;
using CimianStudio.Core.Models.Packages;

/// <summary>
/// Cimian-canonical pkginfo writer. Mirrors the key ordering used by
/// <c>cli/cimiimport/Services/ImportService.cs::SerializePkgsInfoWithKeyOrder</c>:
/// <list type="number">
///   <item><c>name</c></item>
///   <item><c>display_name</c> (when set)</item>
///   <item><c>version</c></item>
///   <item>everything else, sorted alphabetically (case-insensitive)</item>
///   <item><c>_metadata</c> (always last)</item>
/// </list>
/// Booleans <c>unattended_install</c>/<c>unattended_uninstall</c> are always emitted
/// (matches cimiimport); <c>OnDemand</c> and <c>uninstallable</c> are emitted only when
/// they carry a non-default truthy value.
/// </summary>
public static class PackageYamlSerializer
{
    /// <summary>
    /// Reads a pkginfo YAML document into a <see cref="Package"/> and patches the
    /// trailing <c>_metadata</c> block via the representation model — YamlDotNet 16.3
    /// does not bind <c>[YamlMember(Alias)]</c> values that start with an underscore.
    /// </summary>
    public static Package? Deserialize(string yaml)
    {
        if (string.IsNullOrEmpty(yaml))
        {
            return null;
        }

        var pkg = YamlSerialization.Deserializer.Deserialize<Package>(yaml);
        if (pkg is null)
        {
            return null;
        }

        pkg.Metadata = ExtractMetadataBlock(yaml);
        return pkg;
    }

    public static Dictionary<string, object?>? ExtractMetadataBlock(string yaml)
    {
        if (string.IsNullOrEmpty(yaml))
        {
            return null;
        }

        try
        {
            using var reader = new StringReader(yaml);
            var stream = new YamlDotNet.RepresentationModel.YamlStream();
            stream.Load(reader);
            if (stream.Documents.Count == 0)
            {
                return null;
            }

            if (stream.Documents[0].RootNode is not YamlDotNet.RepresentationModel.YamlMappingNode root)
            {
                return null;
            }

            foreach (var entry in root.Children)
            {
                if (entry.Key is YamlDotNet.RepresentationModel.YamlScalarNode keyNode
                    && string.Equals(keyNode.Value, "_metadata", StringComparison.Ordinal)
                    && entry.Value is YamlDotNet.RepresentationModel.YamlMappingNode mapping)
                {
                    var dict = new Dictionary<string, object?>(mapping.Children.Count);
                    foreach (var item in mapping.Children)
                    {
                        if (item.Key is YamlDotNet.RepresentationModel.YamlScalarNode k
                            && k.Value is { Length: > 0 } kv)
                        {
                            dict[kv] = ConvertNode(item.Value);
                        }
                    }
                    return dict.Count == 0 ? null : dict;
                }
            }
            return null;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Recursive YamlNode → CLR converter so non-scalar _metadata values (nested
    /// mappings / sequences) survive round-trip instead of being silently dropped.
    /// </summary>
    private static object? ConvertNode(YamlDotNet.RepresentationModel.YamlNode? node)
    {
        switch (node)
        {
            case YamlDotNet.RepresentationModel.YamlScalarNode scalar:
                return scalar.Value;
            case YamlDotNet.RepresentationModel.YamlMappingNode map:
                var dict = new Dictionary<object, object?>(map.Children.Count);
                foreach (var (k, v) in map.Children)
                {
                    var key = (k as YamlDotNet.RepresentationModel.YamlScalarNode)?.Value ?? k.ToString() ?? string.Empty;
                    dict[key] = ConvertNode(v);
                }
                return dict;
            case YamlDotNet.RepresentationModel.YamlSequenceNode seq:
                var list = new List<object?>(seq.Children.Count);
                foreach (var child in seq.Children) list.Add(ConvertNode(child));
                return list;
            default:
                return null;
        }
    }

    public static string Serialize(Package pkg)
    {
        ArgumentNullException.ThrowIfNull(pkg);

        // Insertion order is preserved when the Dictionary is iterated, which is what
        // YamlDotNet's mapping serializer uses.  We deliberately add priority keys first,
        // then alphabetical keys, then _metadata last.
        var top = new Dictionary<object, object?>(capacity: 32)
        {
            ["name"] = pkg.Name,
        };

        if (!string.IsNullOrEmpty(pkg.DisplayName))
        {
            top["display_name"] = pkg.DisplayName;
        }

        top["version"] = pkg.Version;

        var others = new SortedDictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        AddIfNonEmpty(others, "blocking_applications", pkg.BlockingApplications);
        AddIfNonEmpty(others, "catalogs", pkg.Catalogs);
        AddIfPresent(others, "category", pkg.Category);
        AddIfPresent(others, "description", pkg.Description);
        AddIfPresent(others, "developer", pkg.Developer);
        AddIfPresent(others, "identifier", pkg.Identifier);
        AddIfPresent(others, "installcheck_script", NormalizeScriptBlock(pkg.InstallCheckScript));
        if (pkg.Installer is not null && HasInstallerContent(pkg.Installer))
        {
            others["installer"] = pkg.Installer;
        }
        AddIfPresent(others, "installer_type", pkg.InstallerType);
        if (pkg.InstallWindow is not null && HasWindowContent(pkg.InstallWindow))
        {
            others["install_window"] = pkg.InstallWindow;
        }
        AddIfNonEmpty(others, "installs", pkg.Installs);
        AddIfNonEmpty(others, "managed_apps", pkg.ManagedApps);
        AddIfNonEmpty(others, "managed_profiles", pkg.ManagedProfiles);
        AddIfPresent(others, "maximum_os_version", pkg.MaximumOsVersion);
        AddIfPresent(others, "minimum_cimian_version", pkg.MinimumCimianVersion);
        AddIfPresent(others, "minimum_os_version", pkg.MinimumOsVersion);

        // Pascal-case key intentional to match Cimian's canonical YAML.
        if (pkg.OnDemand)
        {
            others["OnDemand"] = true;
        }

        AddIfPresent(others, "postinstall_script", NormalizeScriptBlock(pkg.PostinstallScript));
        AddIfPresent(others, "postuninstall_script", NormalizeScriptBlock(pkg.PostuninstallScript));
        AddIfPresent(others, "preinstall_script", NormalizeScriptBlock(pkg.PreinstallScript));
        AddIfPresent(others, "preuninstall_script", NormalizeScriptBlock(pkg.PreuninstallScript));
        AddIfNonEmpty(others, "requires", pkg.Requires);
        AddIfNonEmpty(others, "supported_architectures", pkg.SupportedArchitectures);

        // Always present per cimiimport convention.
        others["unattended_install"] = pkg.UnattendedInstall;
        others["unattended_uninstall"] = pkg.UnattendedUninstall;

        if (pkg.Uninstallable == true)
        {
            others["uninstallable"] = true;
        }

        AddIfPresent(others, "uninstallcheck_script", NormalizeScriptBlock(pkg.UninstallCheckScript));
        AddIfNonEmpty(others, "uninstaller", pkg.Uninstaller);
        AddIfPresent(others, "uninstaller_path", pkg.UninstallerPath);
        AddIfNonEmpty(others, "update_for", pkg.UpdateFor);

        foreach (var (key, value) in others)
        {
            top[key] = value;
        }

        if (pkg.Metadata is { Count: > 0 } metadata)
        {
            top["_metadata"] = metadata;
        }

        return YamlSerialization.Serializer.Serialize(top);
    }

    private static void AddIfPresent(SortedDictionary<string, object?> dict, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            dict[key] = value;
        }
    }

    /// <summary>
    /// Force script values to end with a newline so YamlDotNet picks <c>|</c> (clip)
    /// instead of <c>|-</c> (strip) for the literal block scalar style.  Idempotent.
    /// </summary>
    private static string? NormalizeScriptBlock(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }
        // Normalise CRLF/CR to LF first so the trailing-newline check is reliable.
        var lf = value.Replace("\r\n", "\n", StringComparison.Ordinal)
                      .Replace('\r', '\n');
        return lf.EndsWith('\n') ? lf : lf + "\n";
    }

    private static void AddIfNonEmpty<T>(SortedDictionary<string, object?> dict, string key, IReadOnlyCollection<T>? value)
    {
        if (value is not null && value.Count > 0)
        {
            dict[key] = value;
        }
    }

    /// <summary>
    /// True when at least one installer field carries content. Avoids emitting
    /// <c>installer: {}</c> when the model is shape-only (e.g. on packages that have
    /// no binary, just scripts).
    /// </summary>
    private static bool HasInstallerContent(Installer i) =>
        !string.IsNullOrEmpty(i.Type)
        || !string.IsNullOrEmpty(i.Location)
        || !string.IsNullOrEmpty(i.Hash)
        || i.Size.HasValue
        || (i.Arguments is { Count: > 0 })
        || (i.Args is { Count: > 0 })
        || (i.Switches is { Count: > 0 })
        || (i.Flags is { Count: > 0 })
        || !string.IsNullOrEmpty(i.Subcommand)
        || !string.IsNullOrEmpty(i.TempDir)
        || !string.IsNullOrEmpty(i.ProductCode)
        || !string.IsNullOrEmpty(i.UpgradeCode)
        || !string.IsNullOrEmpty(i.IdentityName);

    private static bool HasWindowContent(InstallWindow w) =>
        !string.IsNullOrEmpty(w.Start)
        || !string.IsNullOrEmpty(w.End)
        || (w.Weekdays is { Count: > 0 });
}
