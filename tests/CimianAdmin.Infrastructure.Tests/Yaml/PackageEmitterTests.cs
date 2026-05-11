namespace CimianAdmin.Infrastructure.Tests.Yaml;

using CimianAdmin.Core.Models.Packages;
using CimianAdmin.Infrastructure.Yaml;
using FluentAssertions;

public class PackageEmitterTests
{
    [Fact]
    public void Metadata_RoundTrips()
    {
        var input = """
                    name: foo
                    version: '1.0'
                    catalogs:
                    - Production
                    unattended_install: true
                    unattended_uninstall: true
                    _metadata:
                      cimian-promoter_edit_date: '2026-04-29T18:01:30Z'
                    """;

        var pkg = PackageYamlSerializer.Deserialize(input);

        pkg.Should().NotBeNull();
        pkg!.Metadata.Should().NotBeNull("PackageYamlSerializer.Deserialize patches in _metadata");
        pkg.Metadata!.Should().ContainKey("cimian-promoter_edit_date");
    }

    [Fact]
    public void Metadata_RoundTrip_PreservedThroughEmitter()
    {
        var input = """
                    name: foo
                    version: '1.0'
                    catalogs:
                    - Production
                    unattended_install: true
                    unattended_uninstall: true
                    _metadata:
                      cimian-promoter_edit_date: '2026-04-29T18:01:30Z'
                    """;

        var pkg = PackageYamlSerializer.Deserialize(input);
        var emitted = PackageYamlSerializer.Serialize(pkg!);

        emitted.Should().Contain("_metadata:");
        emitted.Should().Contain("cimian-promoter_edit_date");
    }

    [Fact]
    public void KeyOrder_PutsNameDisplayNameVersionFirstAndMetadataLast()
    {
        var pkg = new Package
        {
            Name = "foo",
            DisplayName = "Foo",
            Version = "1.0",
            Catalogs = ["Production"],
            Description = "x",
            Metadata = new Dictionary<string, string?> { ["cimian-promoter_edit_date"] = "2026-04-29T18:01:30Z" },
        };

        var yaml = PackageYamlSerializer.Serialize(pkg);

        // Name comes first, then display_name, then version. _metadata is last.
        var nameIdx = yaml.IndexOf("name:", StringComparison.Ordinal);
        var displayIdx = yaml.IndexOf("display_name:", StringComparison.Ordinal);
        var versionIdx = yaml.IndexOf("version:", StringComparison.Ordinal);
        var descIdx = yaml.IndexOf("description:", StringComparison.Ordinal);
        var metadataIdx = yaml.IndexOf("_metadata:", StringComparison.Ordinal);

        nameIdx.Should().BeLessThan(displayIdx);
        displayIdx.Should().BeLessThan(versionIdx);
        versionIdx.Should().BeLessThan(descIdx);
        metadataIdx.Should().BeGreaterThan(descIdx);
    }

    [Fact]
    public void ScriptValue_EmittedWithLiteralBlockClipStyle()
    {
        var pkg = new Package
        {
            Name = "foo",
            Version = "1.0",
            InstallCheckScript = "if (Test-Path X) { exit 1 }\nelse { exit 0 }",
        };

        var yaml = PackageYamlSerializer.Serialize(pkg);

        // | is clip (single trailing newline). |- is strip (no trailing newline).
        // For PowerShell scripts we want | to match Cimian's convention.
        // Note: YamlDotNet emits with platform line endings, so check both \n and \r\n.
        yaml.Should().Match(s => s.Contains("installcheck_script: |\n", StringComparison.Ordinal)
                              || s.Contains("installcheck_script: |\r\n", StringComparison.Ordinal),
            $"expected | block scalar; got:\n{yaml}");
        yaml.Should().NotContain("installcheck_script: |-");
        yaml.Should().NotContain("installcheck_script: >-");
    }

    [Fact]
    public void Installer_FieldsEmittedInTypeSizeLocationHashOrder()
    {
        var pkg = new Package
        {
            Name = "foo",
            Version = "1.0",
            Installer = new Installer
            {
                Type = "msi",
                Location = "/apps/foo.msi",
                Hash = "deadbeef",
                Size = 12345,
            },
        };

        var yaml = PackageYamlSerializer.Serialize(pkg);

        var typeIdx = yaml.IndexOf("type:", StringComparison.Ordinal);
        var sizeIdx = yaml.IndexOf("size:", StringComparison.Ordinal);
        var locIdx = yaml.IndexOf("location:", StringComparison.Ordinal);
        var hashIdx = yaml.IndexOf("hash:", StringComparison.Ordinal);

        typeIdx.Should().BeLessThan(sizeIdx, "size goes right after type");
        sizeIdx.Should().BeLessThan(locIdx, "location follows size");
        locIdx.Should().BeLessThan(hashIdx, "hash is last of the four");
    }
}
