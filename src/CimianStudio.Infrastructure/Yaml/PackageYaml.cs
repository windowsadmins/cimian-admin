namespace CimianStudio.Infrastructure.Yaml;

using Cimian.Core.Services;
using CimianStudio.Core.Models.Packages;

/// <summary>
/// Thin wrapper around <see cref="YamlUtils.SerializePkgInfo{T}"/> /
/// <see cref="YamlUtils.DeserializePkgInfo{T}"/> that handles the trailing
/// <c>_metadata</c> block. Exists solely because YamlDotNet 16.3 silently drops
/// any <c>[YamlMember(Alias = "_metadata")]</c> binding (leading-underscore
/// alias regression), so <see cref="Package.Metadata"/> is <see cref="YamlIgnoreAttribute"/>
/// and we have to splice the block in/out by hand.
///
/// All canonical formatting decisions (key order, quoting, literal blocks,
/// boolean handling) live upstream in <c>shared/core/Services/YamlUtils.cs</c> —
/// this file deliberately holds none of them.
/// </summary>
public static class PackageYaml
{
    public static Package? Deserialize(string yaml)
    {
        if (string.IsNullOrEmpty(yaml)) return null;
        var pkg = YamlUtils.DeserializePkgInfo<Package>(yaml);
        if (pkg is null) return null;
        pkg.Metadata = YamlUtils.ExtractMetadataBlock(yaml);
        return pkg;
    }

    public static string Serialize(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        EnsureTrailingNewlinesForScripts(package);
        var yaml = YamlUtils.SerializePkgInfo(package);
        if (package.Metadata is { Count: > 0 } md)
        {
            // YamlUtils' top-level reorder always puts `_metadata` last, so
            // appending here lands at the canonical position. Wrapping in a
            // single-key dict gives YamlDotNet a target to render the block
            // shape (`_metadata:\n  key: value\n  ...`).
            var metaYaml = YamlUtils.Serializer.Serialize(new Dictionary<string, object?> { ["_metadata"] = md });
            yaml = yaml.TrimEnd('\n') + "\n" + metaYaml;
        }
        return yaml;
    }

    // Without a trailing newline, YamlDotNet picks `|-` (strip) instead of `|`
    // (clip) for the literal block scalar style. Real deployment files use `|`
    // exclusively — clip preserves the single trailing newline scripts almost
    // always have, and round-trips cleanly. Mutates the input package directly
    // (idempotent: re-running adds nothing). Same pattern YamlUtils itself uses
    // in NormalizeMultilineStrings; consider promoting upstream so every Cimian
    // tool gets the same behavior without needing this shim.
    private static void EnsureTrailingNewlinesForScripts(Package p)
    {
        p.PreinstallScript = AppendNewlineIfMissing(p.PreinstallScript);
        p.PostinstallScript = AppendNewlineIfMissing(p.PostinstallScript);
        p.PreuninstallScript = AppendNewlineIfMissing(p.PreuninstallScript);
        p.PostuninstallScript = AppendNewlineIfMissing(p.PostuninstallScript);
        p.InstallCheckScript = AppendNewlineIfMissing(p.InstallCheckScript);
        p.UninstallCheckScript = AppendNewlineIfMissing(p.UninstallCheckScript);
    }

    private static string? AppendNewlineIfMissing(string? s)
        => string.IsNullOrEmpty(s) || s.EndsWith('\n') ? s : s + "\n";
}
